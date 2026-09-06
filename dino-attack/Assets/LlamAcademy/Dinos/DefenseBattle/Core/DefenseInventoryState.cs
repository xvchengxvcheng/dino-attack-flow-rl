using System;

namespace LlamAcademy.Dinos.DefenseBattle
{
    public readonly struct DefenseInventoryState : IEquatable<DefenseInventoryState>
    {
        public const int InitialWalls = 3;
        public const int InitialArchers = 5;
        public const int InitialMages = 3;

        public static DefenseInventoryState Full =>
            new(InitialWalls, InitialArchers, InitialMages);

        public DefenseInventoryState(int walls, int archers, int mages)
        {
            if (walls < 0 || walls > InitialWalls ||
                archers < 0 || archers > InitialArchers ||
                mages < 0 || mages > InitialMages)
            {
                throw new ArgumentOutOfRangeException(nameof(walls),
                    "Defense inventory must stay within its frozen capacity.");
            }

            Walls = walls;
            Archers = archers;
            Mages = mages;
        }

        public int Walls { get; }
        public int Archers { get; }
        public int Mages { get; }
        public bool IsComplete => Walls == 0 && Archers == 0 && Mages == 0;

        public int Remaining(DefensePlacementKind kind) => kind switch
        {
            DefensePlacementKind.Wall => Walls,
            DefensePlacementKind.Archer => Archers,
            DefensePlacementKind.Mage => Mages,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        public bool TryPlace(DefensePlacementKind kind, out DefenseInventoryState next)
        {
            if (Remaining(kind) == 0)
            {
                next = this;
                return false;
            }

            next = kind switch
            {
                DefensePlacementKind.Wall => new DefenseInventoryState(Walls - 1, Archers, Mages),
                DefensePlacementKind.Archer => new DefenseInventoryState(Walls, Archers - 1, Mages),
                DefensePlacementKind.Mage => new DefenseInventoryState(Walls, Archers, Mages - 1),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
            return true;
        }

        public bool TryReturn(DefensePlacementKind kind, out DefenseInventoryState next)
        {
            int capacity = kind switch
            {
                DefensePlacementKind.Wall => InitialWalls,
                DefensePlacementKind.Archer => InitialArchers,
                DefensePlacementKind.Mage => InitialMages,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
            if (Remaining(kind) >= capacity)
            {
                next = this;
                return false;
            }

            next = kind switch
            {
                DefensePlacementKind.Wall => new DefenseInventoryState(Walls + 1, Archers, Mages),
                DefensePlacementKind.Archer => new DefenseInventoryState(Walls, Archers + 1, Mages),
                DefensePlacementKind.Mage => new DefenseInventoryState(Walls, Archers, Mages + 1),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
            return true;
        }

        public string BuildMissingMessage(bool hasStrategy)
        {
            string units = string.Empty;
            AppendMissing(ref units, "城墙", Walls);
            AppendMissing(ref units, "弓箭手", Archers);
            AppendMissing(ref units, "法师", Mages);
            if (!hasStrategy)
            {
                if (units.Length > 0) units += "；";
                units += "请选择 AI Strategy";
            }
            return units.Length == 0 ? string.Empty : $"还需放置：{units}";
        }

        public bool Equals(DefenseInventoryState other) =>
            Walls == other.Walls && Archers == other.Archers && Mages == other.Mages;

        public override bool Equals(object obj) => obj is DefenseInventoryState other && Equals(other);
        public override int GetHashCode() => ((Walls * 397) ^ Archers) * 397 ^ Mages;

        private static void AppendMissing(ref string text, string label, int count)
        {
            if (count <= 0) return;
            if (text.Length > 0) text += "、";
            text += $"{label} {count}";
        }
    }
}
