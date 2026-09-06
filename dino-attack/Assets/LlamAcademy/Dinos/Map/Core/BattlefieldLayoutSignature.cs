using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    public readonly struct QuantizedBattlefieldPoint : IEquatable<QuantizedBattlefieldPoint>
    {
        public const float UnitsPerMeter = 1000f;

        public QuantizedBattlefieldPoint(Vector2 position)
        {
            if (!IsFinite(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Battlefield coordinates must be finite.");
            }

            X = Mathf.RoundToInt(position.x * UnitsPerMeter);
            Z = Mathf.RoundToInt(position.y * UnitsPerMeter);
        }

        public int X { get; }
        public int Z { get; }
        public Vector2 Position => new(X / UnitsPerMeter, Z / UnitsPerMeter);

        public bool Equals(QuantizedBattlefieldPoint other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is QuantizedBattlefieldPoint other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Z);
        public override string ToString() => FormattableString.Invariant($"{X},{Z}");

        private static bool IsFinite(Vector2 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }

    public readonly struct BattlefieldEntitySignature : IEquatable<BattlefieldEntitySignature>
    {
        public BattlefieldEntitySignature(string stableId, Vector2 position)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException("A stable entity ID is required.", nameof(stableId));
            }

            StableId = stableId;
            Position = new QuantizedBattlefieldPoint(position);
        }

        public string StableId { get; }
        public QuantizedBattlefieldPoint Position { get; }
        public bool HasQuantizedCoordinates => !string.IsNullOrWhiteSpace(StableId);

        public bool Equals(BattlefieldEntitySignature other) =>
            string.Equals(StableId, other.StableId, StringComparison.Ordinal) && Position.Equals(other.Position);
        public override bool Equals(object obj) => obj is BattlefieldEntitySignature other && Equals(other);
        public override int GetHashCode() => unchecked(((StableId == null ? 0 : StringComparer.Ordinal.GetHashCode(StableId)) * 397) ^ Position.GetHashCode());
        public override string ToString() => $"{StableId}@{Position}";
    }

    public sealed class BattlefieldZoneSignature
    {
        public BattlefieldZoneSignature(DeploymentZoneId zoneId, IEnumerable<Vector2> vertices)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            QuantizedBattlefieldPoint[] copied = vertices.Select(value => new QuantizedBattlefieldPoint(value)).ToArray();
            if (copied.Length != 4)
            {
                throw new ArgumentException("Each zone signature requires exactly four vertices.", nameof(vertices));
            }

            ZoneId = zoneId;
            Vertices = Array.AsReadOnly(copied);
        }

        public DeploymentZoneId ZoneId { get; }
        public IReadOnlyList<QuantizedBattlefieldPoint> Vertices { get; }
    }

    public sealed class BattlefieldLayoutSignature : IEquatable<BattlefieldLayoutSignature>
    {
        private BattlefieldLayoutSignature(
            string mapStableId,
            int layoutSeed,
            int zonePresetIndex,
            IEnumerable<BattlefieldZoneSignature> zoneVertices,
            IEnumerable<BattlefieldEntitySignature> houses,
            IEnumerable<BattlefieldEntitySignature> walls,
            IEnumerable<BattlefieldEntitySignature> guards,
            IEnumerable<BattlefieldEntitySignature> targets)
        {
            if (string.IsNullOrWhiteSpace(mapStableId))
            {
                throw new ArgumentException("A stable map ID is required.", nameof(mapStableId));
            }

            MapStableId = mapStableId;
            LayoutSeed = layoutSeed;
            ZonePresetIndex = zonePresetIndex;
            ZoneVertices = CopyZones(zoneVertices);
            Houses = CopyEntities(houses, nameof(houses));
            Walls = CopyEntities(walls, nameof(walls));
            Guards = CopyEntities(guards, nameof(guards));
            Targets = CopyEntities(targets, nameof(targets));
            CanonicalText = BuildCanonicalText();
        }

        public string MapStableId { get; }
        public int LayoutSeed { get; }
        public int ZonePresetIndex { get; }
        public IReadOnlyList<BattlefieldZoneSignature> ZoneVertices { get; }
        public IReadOnlyList<BattlefieldEntitySignature> Houses { get; }
        public IReadOnlyList<BattlefieldEntitySignature> Walls { get; }
        public IReadOnlyList<BattlefieldEntitySignature> Guards { get; }
        public IReadOnlyList<BattlefieldEntitySignature> Targets { get; }
        public string CanonicalText { get; }

        public static BattlefieldLayoutSignature Create(
            string mapStableId,
            int layoutSeed,
            int zonePresetIndex,
            IEnumerable<BattlefieldZoneSignature> zoneVertices,
            IEnumerable<BattlefieldEntitySignature> houses,
            IEnumerable<BattlefieldEntitySignature> walls,
            IEnumerable<BattlefieldEntitySignature> guards,
            IEnumerable<BattlefieldEntitySignature> targets) =>
            new(mapStableId, layoutSeed, zonePresetIndex, zoneVertices, houses, walls, guards, targets);

        public bool Equals(BattlefieldLayoutSignature other) =>
            other != null && string.Equals(CanonicalText, other.CanonicalText, StringComparison.Ordinal);
        public override bool Equals(object obj) => Equals(obj as BattlefieldLayoutSignature);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(CanonicalText);
        public override string ToString() => CanonicalText;

        private static IReadOnlyList<BattlefieldZoneSignature> CopyZones(IEnumerable<BattlefieldZoneSignature> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            BattlefieldZoneSignature[] copied = source.OrderBy(value => value.ZoneId).ToArray();
            if (copied.Length != 5 || copied.Any(value => value == null) || copied.Select(value => value.ZoneId).Distinct().Count() != 5)
            {
                throw new ArgumentException("A layout signature requires exactly five uniquely identified zones.", nameof(source));
            }
            return Array.AsReadOnly(copied);
        }

        private static IReadOnlyList<BattlefieldEntitySignature> CopyEntities(
            IEnumerable<BattlefieldEntitySignature> source,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            BattlefieldEntitySignature[] copied = source.OrderBy(value => value.StableId, StringComparer.Ordinal).ToArray();
            if (copied.Select(value => value.StableId).Distinct(StringComparer.Ordinal).Count() != copied.Length)
            {
                throw new ArgumentException("Entity stable IDs must be unique within their category.", parameterName);
            }
            return Array.AsReadOnly(copied);
        }

        private string BuildCanonicalText()
        {
            StringBuilder builder = new();
            builder.Append(MapStableId).Append('|')
                .Append(LayoutSeed.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(ZonePresetIndex.ToString(CultureInfo.InvariantCulture));
            foreach (BattlefieldZoneSignature zone in ZoneVertices)
            {
                builder.Append("|Z:").Append((int)zone.ZoneId);
                foreach (QuantizedBattlefieldPoint vertex in zone.Vertices)
                {
                    builder.Append('@').Append(vertex);
                }
            }
            AppendEntities(builder, "H", Houses);
            AppendEntities(builder, "W", Walls);
            AppendEntities(builder, "G", Guards);
            AppendEntities(builder, "T", Targets);
            return builder.ToString();
        }

        private static void AppendEntities(
            StringBuilder builder,
            string prefix,
            IReadOnlyList<BattlefieldEntitySignature> values)
        {
            foreach (BattlefieldEntitySignature value in values)
            {
                builder.Append('|').Append(prefix).Append(':').Append(value);
            }
        }
    }
}
