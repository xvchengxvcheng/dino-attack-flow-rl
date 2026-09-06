using System;
using System.Globalization;

namespace LlamAcademy.Dinos.DefenseBattle
{
    public readonly struct DefensePlacementToken : IEquatable<DefensePlacementToken>
    {
        public DefensePlacementToken(
            DefensePlacementKind kind,
            int index,
            float x,
            float y,
            float z,
            float rotationY)
        {
            int capacity = kind switch
            {
                DefensePlacementKind.Wall => DefenseInventoryState.InitialWalls,
                DefensePlacementKind.Archer => DefenseInventoryState.InitialArchers,
                DefensePlacementKind.Mage => DefenseInventoryState.InitialMages,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
            if (index < 0 || index >= capacity || !IsFinite(x) || !IsFinite(y) ||
                !IsFinite(z) || !IsFinite(rotationY))
            {
                throw new ArgumentException("The defense placement token is invalid.");
            }

            Kind = kind;
            Index = index;
            X = x;
            Y = y;
            Z = z;
            RotationY = rotationY;
        }

        public DefensePlacementKind Kind { get; }
        public int Index { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float RotationY { get; }

        public string CanonicalText => string.Join(",",
            ((int)Kind).ToString(CultureInfo.InvariantCulture),
            Index.ToString(CultureInfo.InvariantCulture),
            X.ToString("R", CultureInfo.InvariantCulture),
            Y.ToString("R", CultureInfo.InvariantCulture),
            Z.ToString("R", CultureInfo.InvariantCulture),
            RotationY.ToString("R", CultureInfo.InvariantCulture));

        public bool Equals(DefensePlacementToken other) =>
            Kind == other.Kind && Index == other.Index && X.Equals(other.X) &&
            Y.Equals(other.Y) && Z.Equals(other.Z) && RotationY.Equals(other.RotationY);

        public override bool Equals(object obj) => obj is DefensePlacementToken other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = hash * 397 ^ Index;
                hash = hash * 397 ^ X.GetHashCode();
                hash = hash * 397 ^ Y.GetHashCode();
                hash = hash * 397 ^ Z.GetHashCode();
                return hash * 397 ^ RotationY.GetHashCode();
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
