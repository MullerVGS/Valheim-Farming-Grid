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
    /// Decides where the sapling goes: finds the nearest crop (the anchor), infers the grid from it
    /// and picks the free grid point closest to where the player is aiming.
    /// </summary>
    public static class GridSolver
    {
        /// <summary>A neighbour only sets the axis if it is at most this many cells from the anchor.</summary>
        private const float AxisNeighbourCells = 1.5f;

        public static SnapResult Solve(Crop ghost, IReadOnlyList<Crop> nearby, SnapSettings settings)
        {
            Crop anchor = Nearest(nearby, ghost.Position, ghost.Family, exclude: -1, out int anchorIndex);
            if (anchorIndex < 0)
                return Free(ghost, nearby, settings);

            float cell = Spacing.Cell(ghost, anchor, settings.Rule, settings.ExtraSpacing);
            if (cell <= Spacing.Tolerance)
                return Free(ghost, nearby, settings);
            cell *= Math.Max(1, settings.Step);

            if (Vec2.Distance(ghost.Position, anchor.Position) > cell * settings.ReachCells)
                return Free(ghost, nearby, settings);

            Vec2 axis = Axis(ghost, anchor, anchorIndex, nearby, cell, settings);
            var lattice = new Lattice(anchor.Position, axis, cell);

            Vec2 offset = ghost.Position - anchor.Position;
            int u0 = (int)Math.Round(Vec2.Dot(offset, lattice.AxisU) / cell);
            int v0 = (int)Math.Round(Vec2.Dot(offset, lattice.AxisV) / cell);

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

        private static Vec2 Axis(Crop ghost, Crop anchor, int anchorIndex, IReadOnlyList<Crop> nearby, float cell, SnapSettings settings)
        {
            if (settings.Orientation == GridOrientation.Fixed)
                return Vec2.FromAngle(settings.FixedAngle);

            Crop neighbour = Nearest(nearby, anchor.Position, ghost.Family, anchorIndex, out int neighbourIndex);
            if (neighbourIndex >= 0)
            {
                Vec2 towardNeighbour = neighbour.Position - anchor.Position;
                float distance = towardNeighbour.Length;
                if (distance > Spacing.Tolerance && distance <= cell * AxisNeighbourCells)
                    return towardNeighbour.Normalized(Vec2.UnitX);
            }

            // Lone anchor: the second sapling orbits freely around it, toward where the player is aiming.
            return (ghost.Position - anchor.Position).Normalized(Vec2.UnitX);
        }

        /// <summary>The nearest one of the same family: a tree or bush next to the garden does not dictate the garden's grid.</summary>
        private static Crop Nearest(IReadOnlyList<Crop> crops, Vec2 point, int family, int exclude, out int index)
        {
            index = -1;
            float best = float.MaxValue;
            for (int i = 0; i < crops.Count; i++)
            {
                if (i == exclude || crops[i].Family != family)
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
