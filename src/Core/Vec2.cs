using System;

namespace FarmingGrid.Core
{
    /// <summary>Point or direction on the ground plane (x, z). The field is 2D; height comes from the terrain later.</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Z;

        public Vec2(float x, float z)
        {
            X = x;
            Z = z;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 UnitX = new Vec2(1f, 0f);

        public float Length => (float)Math.Sqrt(X * X + Z * Z);
        public float SqrLength => X * X + Z * Z;

        /// <summary>Counter-clockwise perpendicular, seen from above.</summary>
        public Vec2 Perpendicular => new Vec2(-Z, X);

        public Vec2 Normalized(Vec2 fallback)
        {
            float length = Length;
            return length < 1e-5f ? fallback : new Vec2(X / length, Z / length);
        }

        public static Vec2 FromAngle(float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            return new Vec2((float)Math.Sin(radians), (float)Math.Cos(radians));
        }

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Z + b.Z);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Z - b.Z);
        public static Vec2 operator *(Vec2 a, float k) => new Vec2(a.X * k, a.Z * k);

        public bool Equals(Vec2 other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Z.GetHashCode();
        public override string ToString() => $"({X:0.###}, {Z:0.###})";
    }
}
