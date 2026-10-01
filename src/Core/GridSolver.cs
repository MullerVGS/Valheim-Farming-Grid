using System;
using System.Collections.Generic;

namespace FarmingGrid.Core
{
    public enum GridOrientation
    {
        /// <summary>Follows the field: the axis runs from the anchor to its nearest neighbour.</summary>
        Auto,

        /// <summary>Fixed angle in degrees from world north.</summary>
        Fixed,
    }

    public sealed class SnapSettings
    {
        public SpacingRule Rule = SpacingRule.Exact;
        public float ExtraSpacing;
        public GridOrientation Orientation = GridOrientation.Auto;
        public float FixedAngle;

        /// <summary>How many cells away from the anchor the ghost is still pulled onto the grid.</summary>
        public float ReachCells = 2.5f;

        /// <summary>Radius, in cells, of the search for a free point around the nearest one.</summary>
        public int SearchRadius = 2;

        /// <summary>Grid step in cells: 2 plants every other cell, 3 every third and so on. The spacing check still uses the real minimum.</summary>
        public int Step = 1;
    }

    /// <summary>The grid inferred from the field, ready to draw.</summary>
    public readonly struct Lattice
    {
        public readonly Vec2 Origin;
        public readonly Vec2 AxisU;
        public readonly float Cell;

        public Lattice(Vec2 origin, Vec2 axisU, float cell)
        {
            Origin = origin;
            AxisU = axisU;
            Cell = cell;
        }

        public Vec2 AxisV => AxisU.Perpendicular;

        public Vec2 PointAt(int u, int v) => Origin + AxisU * (u * Cell) + AxisV * (v * Cell);
    }

    public readonly struct SnapResult
    {
        /// <summary>Whether the ghost was pulled onto the grid; with no neighbours nearby placement is free.</summary>
        public readonly bool Snapped;
        public readonly Vec2 Position;
        public readonly Lattice Lattice;

        /// <summary>The final position intrudes on some crop's grow space (or vice versa).</summary>
        public readonly bool Crowded;

        public SnapResult(bool snapped, Vec2 position, Lattice lattice, bool crowded)
        {
            Snapped = snapped;
            Position = position;
            Lattice = lattice;
            Crowded = crowded;
        }
    }

    /// <summary>
    /// Decides where the sapling goes: finds the field around the aim (the same plant first, then the same family),
    /// fits the grid that most of it agrees on and picks the free grid point closest to where the player is aiming.
    /// A single stray crop or a gap in the field does not shift or tilt the grid, because it is outvoted by the rest.
    /// </summary>
    public static class GridSolver
    {
        /// <summary>A lone neighbour only sets the axis if it is at most this many cells from the anchor.</summary>
        private const float AxisNeighbourCells = 1.5f;

        /// <summary>How many crops, nearest to the aim first, vote on the grid.</summary>
        private const int FitCrops = 24;

        /// <summary>Crops farther than this many cells from the aim count half as much in the vote.</summary>
        private const float FitFalloffCells = 3f;

        /// <summary>Two crops set a direction if they are 1 to this many cells apart, give or take <see cref="PairTolerance"/> of a cell.</summary>
        private const int PairMaxCells = 3;
        private const float PairTolerance = 0.1f;

        /// <summary>Directions within this angle of each other agree on the axis.</summary>
        private const float AngleToleranceDegrees = 3f;

        /// <summary>A crop sits on a candidate grid if it is within this fraction of a cell of a grid point on both axes.</summary>
        private const float PhaseTolerance = 0.2f;

        [ThreadStatic] private static List<Crop> _field;
        [ThreadStatic] private static List<float> _weights;
        [ThreadStatic] private static List<Edge> _edges;

        private readonly struct Edge
        {
            /// <summary>Direction with four times its angle, so directions 90 degrees apart coincide.</summary>
            public readonly Vec2 Quad;
            public readonly float Weight;

            public Edge(Vec2 quad, float weight)
            {
                Quad = quad;
                Weight = weight;
            }
        }

        public static SnapResult Solve(Crop ghost, IReadOnlyList<Crop> nearby, SnapSettings settings)
        {
            int step = Math.Max(1, settings.Step);
            bool bySpecies = TryAnchor(ghost, nearby, settings, step, bySpecies: true, out Crop anchor, out float cell);
            if (!bySpecies && !TryAnchor(ghost, nearby, settings, step, bySpecies: false, out anchor, out cell))
                return Free(ghost, nearby, settings);

            List<Crop> field = Field(ghost, nearby, bySpecies, cell, out List<float> weights);
            Vec2 axis = settings.Orientation == GridOrientation.Fixed
                ? Vec2.FromAngle(settings.FixedAngle)
                : FitAxis(field, weights, cell) ?? LoneAxis(ghost, anchor, field, cell);
            Vec2 origin = FitOrigin(field, weights, axis, cell);
            var lattice = new Lattice(origin, axis, cell * step);
            float stepped = lattice.Cell;

            Vec2 offset = ghost.Position - origin;
            int u0 = (int)Math.Round(Vec2.Dot(offset, lattice.AxisU) / stepped);
            int v0 = (int)Math.Round(Vec2.Dot(offset, lattice.AxisV) / stepped);

            bool found = false;
            Vec2 best = Vec2.Zero;
            float bestDistance = float.MaxValue;
            int r = Math.Max(0, settings.SearchRadius);
            for (int u = u0 - r; u <= u0 + r; u++)
            {
                for (int v = v0 - r; v <= v0 + r; v++)
                {
                    Vec2 candidate = lattice.PointAt(u, v);
                    float distance = (candidate - ghost.Position).SqrLength;
                    if (distance >= bestDistance || IsCrowded(ghost.At(candidate), nearby, settings))
                        continue;
                    found = true;
                    best = candidate;
                    bestDistance = distance;
                }
            }

            if (found)
                return new SnapResult(true, best, lattice, crowded: false);

            // Everything around is taken: show where it would go, but flagged as lacking room.
            if (u0 == 0 && v0 == 0)
                u0 = 1;
            return new SnapResult(true, lattice.PointAt(u0, v0), lattice, crowded: true);
        }

        public static bool IsCrowded(Crop crop, IReadOnlyList<Crop> nearby, SnapSettings settings)
        {
            for (int i = 0; i < nearby.Count; i++)
            {
                if (Spacing.Conflicts(crop, nearby[i], settings.Rule, settings.ExtraSpacing))
                    return true;
            }
            return false;
        }

        /// <summary>Free placement, no grid: only reports whether the spot is crowded.</summary>
        public static SnapResult Free(Crop ghost, IReadOnlyList<Crop> nearby, SnapSettings settings)
            => new SnapResult(false, ghost.Position, default, IsCrowded(ghost, nearby, settings));

        /// <summary>
        /// The crop nearest to the aim that the grid can follow, and the unstepped cell it sets; <c>false</c> if none is within reach.
        /// By species, only the same plant counts; otherwise any crop of the same family.
        /// </summary>
        private static bool TryAnchor(Crop ghost, IReadOnlyList<Crop> nearby, SnapSettings settings, int step, bool bySpecies,
            out Crop anchor, out float cell)
        {
            cell = 0f;
            if (bySpecies && ghost.Species == 0)
            {
                anchor = default;
                return false;
            }
            anchor = Nearest(nearby, ghost.Position, ghost, bySpecies, out int index);
            if (index < 0)
                return false;
            cell = Spacing.Cell(ghost, anchor, settings.Rule, settings.ExtraSpacing);
            return cell > Spacing.Tolerance && Vec2.Distance(ghost.Position, anchor.Position) <= cell * step * settings.ReachCells;
        }

        /// <summary>The crops that vote on the grid: the ones the grid follows, nearest to the aim first, each weighted by its distance.</summary>
        private static List<Crop> Field(Crop ghost, IReadOnlyList<Crop> nearby, bool bySpecies, float cell, out List<float> weights)
        {
            List<Crop> field = _field ?? (_field = new List<Crop>());
            weights = _weights ?? (_weights = new List<float>());
            field.Clear();
            weights.Clear();
            for (int i = 0; i < nearby.Count; i++)
            {
                if (Follows(nearby[i], ghost, bySpecies))
                    field.Add(nearby[i]);
            }
            Vec2 aim = ghost.Position;
            field.Sort((a, b) => (a.Position - aim).SqrLength.CompareTo((b.Position - aim).SqrLength));
            if (field.Count > FitCrops)
                field.RemoveRange(FitCrops, field.Count - FitCrops);

            float falloff = FitFalloffCells * cell;
            foreach (Crop crop in field)
            {
                float d = Vec2.Distance(crop.Position, aim) / falloff;
                weights.Add(1f / (1f + d * d));
            }
            return field;
        }

        /// <summary>
        /// The direction most pairs of crops agree on: pairs one, two or three cells apart (a field planted at a wider step,
        /// or with gaps, still counts; diagonals do not match any of these distances). <c>null</c> if no pair qualifies.
        /// </summary>
        private static Vec2? FitAxis(List<Crop> field, List<float> weights, float cell)
        {
            List<Edge> edges = _edges ?? (_edges = new List<Edge>());
            edges.Clear();
            for (int i = 0; i < field.Count; i++)
            {
                for (int j = i + 1; j < field.Count; j++)
                {
                    Vec2 d = field[j].Position - field[i].Position;
                    float length = d.Length;
                    int k = (int)Math.Round(length / cell);
                    if (k < 1 || k > PairMaxCells || Math.Abs(length - k * cell) > PairTolerance * cell)
                        continue;
                    double quad = 4.0 * Math.Atan2(d.Z, d.X);
                    edges.Add(new Edge(new Vec2((float)Math.Cos(quad), (float)Math.Sin(quad)), weights[i] * weights[j] / k));
                }
            }
            if (edges.Count == 0)
                return null;

            float agree = (float)Math.Cos(4.0 * AngleToleranceDegrees * Math.PI / 180.0);
            int bestEdge = 0;
            float bestScore = -1f;
            for (int e = 0; e < edges.Count; e++)
            {
                float score = 0f;
                foreach (Edge other in edges)
                {
                    if (Vec2.Dot(edges[e].Quad, other.Quad) >= agree)
                        score += other.Weight;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEdge = e;
                }
            }

            Vec2 sum = Vec2.Zero;
            foreach (Edge other in edges)
            {
                if (Vec2.Dot(edges[bestEdge].Quad, other.Quad) >= agree)
                    sum += other.Quad * other.Weight;
            }
            double angle = Math.Atan2(sum.Z, sum.X) / 4.0;
            return new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        /// <summary>No pair of crops at grid distance: the anchor's nearest neighbour sets the axis, or the aim when the anchor is alone.</summary>
        private static Vec2 LoneAxis(Crop ghost, Crop anchor, List<Crop> field, float cell)
        {
            for (int i = 0; i < field.Count; i++)
            {
                Vec2 towardNeighbour = field[i].Position - anchor.Position;
                float distance = towardNeighbour.Length;
                if (distance > Spacing.Tolerance && distance <= cell * AxisNeighbourCells)
                    return towardNeighbour.Normalized(Vec2.UnitX);
            }

            // Lone anchor: the second sapling orbits freely around it, toward where the player is aiming.
            return (ghost.Position - anchor.Position).Normalized(Vec2.UnitX);
        }

        /// <summary>
        /// Where the grid points fall along the axis: each crop proposes a grid through itself and the one most crops sit on wins;
        /// the small offsets of those crops are then averaged out. Returns the grid point of the agreeing crop nearest to the aim,
        /// so a wider step keeps the cells of the field right next to the player.
        /// </summary>
        private static Vec2 FitOrigin(List<Crop> field, List<float> weights, Vec2 axisU, float cell)
        {
            Vec2 axisV = axisU.Perpendicular;
            int bestSeed = 0;
            float bestScore = -1f;
            for (int j = 0; j < field.Count; j++)
            {
                float score = 0f;
                for (int i = 0; i < field.Count; i++)
                {
                    if (Residual(field[i].Position - field[j].Position, axisU, axisV, cell, out _, out _))
                        score += weights[i];
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSeed = j;
                }
            }

            Vec2 seed = field[bestSeed].Position;
            float su = 0f, sv = 0f, total = 0f;
            int nearest = -1;
            for (int i = 0; i < field.Count; i++)
            {
                if (!Residual(field[i].Position - seed, axisU, axisV, cell, out float du, out float dv))
                    continue;
                su += du * weights[i];
                sv += dv * weights[i];
                total += weights[i];
                if (nearest < 0)
                    nearest = i;
            }
            Vec2 origin = seed + axisU * (su / total * cell) + axisV * (sv / total * cell);

            Vec2 offset = field[nearest].Position - origin;
            float u = (float)Math.Round(Vec2.Dot(offset, axisU) / cell);
            float v = (float)Math.Round(Vec2.Dot(offset, axisV) / cell);
            return origin + axisU * (u * cell) + axisV * (v * cell);
        }

        /// <summary>How far, in cells, <paramref name="offset"/> is from the nearest grid point; <c>true</c> if within tolerance.</summary>
        private static bool Residual(Vec2 offset, Vec2 axisU, Vec2 axisV, float cell, out float du, out float dv)
        {
            float u = Vec2.Dot(offset, axisU) / cell;
            float v = Vec2.Dot(offset, axisV) / cell;
            du = u - (float)Math.Round(u);
            dv = v - (float)Math.Round(v);
            return Math.Abs(du) <= PhaseTolerance && Math.Abs(dv) <= PhaseTolerance;
        }

        /// <summary>
        /// The grid follows the same plant, or, failing that, the same family: a tree or bush next to the garden does not
        /// dictate the garden's grid, and a turnip field next to the carrots does not shift the carrots.
        /// </summary>
        private static bool Follows(Crop crop, Crop ghost, bool bySpecies)
            => bySpecies ? crop.Species == ghost.Species : crop.Family == ghost.Family;

        private static Crop Nearest(IReadOnlyList<Crop> crops, Vec2 point, Crop ghost, bool bySpecies, out int index)
        {
            index = -1;
            float best = float.MaxValue;
            for (int i = 0; i < crops.Count; i++)
            {
                if (!Follows(crops[i], ghost, bySpecies))
                    continue;
                float distance = (crops[i].Position - point).SqrLength;
                if (distance < best)
                {
                    best = distance;
                    index = i;
                }
            }
            return index >= 0 ? crops[index] : default;
        }
    }
}
