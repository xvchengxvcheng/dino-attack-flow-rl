using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LlamAcademy.Dinos.Session;

namespace LlamAcademy.Dinos.DefenseBattle
{
    public sealed class DefenseLayoutSnapshot
    {
        private readonly DefensePlacementToken[] PlacementsValue;

        public DefenseLayoutSnapshot(
            GameMapId mapId,
            int layoutSeed,
            string configVersion,
            IEnumerable<DefensePlacementToken> placements)
        {
            if (mapId != GameMapId.DefenseBattlefield || layoutSeed == int.MinValue ||
                string.IsNullOrWhiteSpace(configVersion) || placements == null)
            {
                throw new ArgumentException("The defense layout snapshot identity is invalid.");
            }

            DefensePlacementToken[] values = placements
                .OrderBy(value => value.Kind)
                .ThenBy(value => value.Index)
                .ToArray();
            ValidateExactSet(values);

            MapId = mapId;
            LayoutSeed = layoutSeed;
            ConfigVersion = configVersion;
            PlacementsValue = values;
            CanonicalText = BuildCanonicalText();
            Sha256 = ComputeSha256(CanonicalText);
        }

        public GameMapId MapId { get; }
        public int LayoutSeed { get; }
        public string ConfigVersion { get; }
        public IReadOnlyList<DefensePlacementToken> Placements => PlacementsValue;
        public string CanonicalText { get; }
        public string Sha256 { get; }

        private string BuildCanonicalText() => string.Join("|",
            new[] { MapId.StableId, LayoutSeed.ToString(CultureInfo.InvariantCulture), ConfigVersion }
                .Concat(PlacementsValue.Select(value => value.CanonicalText)));

        private static void ValidateExactSet(IReadOnlyCollection<DefensePlacementToken> values)
        {
            if (values.Count != DefenseInventoryState.InitialWalls +
                DefenseInventoryState.InitialArchers + DefenseInventoryState.InitialMages)
            {
                throw new ArgumentException("A defense layout must contain exactly 3 walls, 5 archers, and 3 mages.");
            }

            foreach (DefensePlacementKind kind in Enum.GetValues(typeof(DefensePlacementKind)))
            {
                int expected = kind switch
                {
                    DefensePlacementKind.Wall => DefenseInventoryState.InitialWalls,
                    DefensePlacementKind.Archer => DefenseInventoryState.InitialArchers,
                    DefensePlacementKind.Mage => DefenseInventoryState.InitialMages,
                    _ => 0,
                };
                int[] indices = values.Where(value => value.Kind == kind)
                    .Select(value => value.Index).OrderBy(value => value).ToArray();
                if (indices.Length != expected || !indices.SequenceEqual(Enumerable.Range(0, expected)))
                {
                    throw new ArgumentException($"Defense layout tokens for {kind} are incomplete or duplicated.");
                }
            }
        }

        private static string ComputeSha256(string value)
        {
            using SHA256 algorithm = SHA256.Create();
            byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
            StringBuilder text = new(hash.Length * 2);
            foreach (byte item in hash) text.Append(item.ToString("x2"));
            return text.ToString();
        }
    }
}
