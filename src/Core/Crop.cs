using System;

namespace FarmingGrid.Core
{
    /// <summary>
    /// A crop's footprint on the ground.
    /// <see cref="GrowRadius"/> is the radius it still needs clear to grow (<c>Plant.m_growRadius</c>;
    /// zero once fully grown). <see cref="BodyRadius"/> is the horizontal reach of its own collider,
    /// which is what neighbours see.
    /// <see cref="LatticeGrow"/> and <see cref="LatticeBody"/> belong to the sapling it came from: they set the grid step,
    /// so a harvested and replanted field lands on the same points.
    /// <see cref="Family"/> separates cultivated-ground crops from everything else (trees, bushes): only the same family becomes an anchor.
    /// </summary>
    public readonly struct Crop
    {
        public readonly Vec2 Position;
        public readonly float GrowRadius;
        public readonly float BodyRadius;
        public readonly float LatticeGrow;
        public readonly float LatticeBody;
        public readonly int Family;

        public Crop(Vec2 position, float growRadius, float bodyRadius, int family = 0)
            : this(position, growRadius, bodyRadius, growRadius, bodyRadius, family)
        {
        }

        private Crop(Vec2 position, float growRadius, float bodyRadius, float latticeGrow, float latticeBody, int family)
        {
            Position = position;
            GrowRadius = growRadius;
            BodyRadius = bodyRadius;
            LatticeGrow = latticeGrow;
            LatticeBody = latticeBody;
            Family = family;
        }

        /// <summary>Fully grown harvest: no longer grows, only occupies its spot with its body; the grid step is the sapling's.</summary>
        public static Crop Grown(Vec2 position, float bodyRadius, float saplingGrow, float saplingBody, int family)
            => new Crop(position, 0f, bodyRadius, saplingGrow, saplingBody, family);

        public Crop At(Vec2 position) => new Crop(position, GrowRadius, BodyRadius, LatticeGrow, LatticeBody, Family);
    }

    public enum SpacingRule
    {
        /// <summary>
        /// The game's rule: <c>Plant.HaveGrowSpace</c> looks for colliders in a sphere of <c>m_growRadius</c>.
        /// Two crops can coexist if neither sphere reaches the other's body.
        /// </summary>
        Exact,

        /// <summary>Roomier spacing: twice the largest grow radius.</summary>
        Wide,
    }

    public static class Spacing
    {
        /// <summary>Numeric slack so crops sitting exactly at the minimum distance do not count as overlapping.</summary>
        public const float Tolerance = 1e-3f;

        /// <summary>Minimum distance for both to grow (or, if one is already grown, for the other to grow).</summary>
        public static float Required(Crop a, Crop b, SpacingRule rule, float extra)
            => Distance(a.GrowRadius, a.BodyRadius, b.GrowRadius, b.BodyRadius, rule, extra);

        /// <summary>Grid step between the sapling in hand and the anchor, treating the anchor as the sapling it once was.</summary>
        public static float Cell(Crop ghost, Crop anchor, SpacingRule rule, float extra)
            => Distance(ghost.GrowRadius, ghost.BodyRadius, anchor.LatticeGrow, anchor.LatticeBody, rule, extra);

        public static bool Conflicts(Crop a, Crop b, SpacingRule rule, float extra)
            => Vec2.Distance(a.Position, b.Position) < Required(a, b, rule, extra) - Tolerance;

        private static float Distance(float growA, float bodyA, float growB, float bodyB, SpacingRule rule, float extra)
        {
            float distance = rule == SpacingRule.Wide
                ? 2f * Math.Max(growA, growB)
                : Math.Max(growA + bodyB, growB + bodyA);
            return distance + Math.Max(0f, extra);
        }
    }
}
