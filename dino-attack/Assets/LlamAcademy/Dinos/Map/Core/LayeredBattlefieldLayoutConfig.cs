using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    public enum BattlefieldLayer
    {
        Outer,
        Middle,
        Core
    }

    public enum BattlefieldHouseTier
    {
        Tier1 = 1,
        Tier2 = 2,
        Tier3 = 3,
        Tier4 = 4
    }

    public enum BattlefieldGuardKind
    {
        Archer,
        Mage,
        WallArcher
    }

    public sealed class BattlefieldZoneDefinition
    {
        public BattlefieldZoneDefinition(
            DeploymentZoneId zoneId,
            TerrainSurfaceKind surface,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            ZoneId = zoneId;
            Surface = surface;
            if (!QuadrilateralXZ.TryCreate(a, b, c, d, out QuadrilateralXZ geometry))
            {
                throw new ArgumentException($"Zone {zoneId} must be a strict convex quadrilateral.");
            }
            Geometry = geometry;
        }

        public DeploymentZoneId ZoneId { get; }
        public TerrainSurfaceKind Surface { get; }
        public QuadrilateralXZ Geometry { get; }
    }

    public sealed class BattlefieldZonePreset
    {
        public BattlefieldZonePreset(int stableIndex, IEnumerable<BattlefieldZoneDefinition> zones)
        {
            StableIndex = stableIndex;
            Zones = Array.AsReadOnly((zones ?? throw new ArgumentNullException(nameof(zones)))
                .OrderBy(value => value.ZoneId)
                .ToArray());
        }

        public int StableIndex { get; }
        public IReadOnlyList<BattlefieldZoneDefinition> Zones { get; }
    }

    public sealed class BattlefieldHouseCandidate
    {
        public BattlefieldHouseCandidate(
            string stableId,
            BattlefieldLayer layer,
            BattlefieldHouseTier tier,
            Vector2 position,
            float visualYawDegrees = 0f,
            float visualScale = 0.6f)
        {
            StableId = RequireStableId(stableId);
            Layer = layer;
            Tier = tier;
            Position = RequireFinite(position);
            if (!float.IsFinite(visualYawDegrees) || !float.IsFinite(visualScale) || visualScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(visualScale),
                    "House visual rotation and scale must be finite, with a positive scale.");
            }
            VisualYawDegrees = visualYawDegrees;
            VisualScale = visualScale;
        }

        public string StableId { get; }
        public BattlefieldLayer Layer { get; }
        public BattlefieldHouseTier Tier { get; }
        public Vector2 Position { get; }
        public float VisualYawDegrees { get; }
        public float VisualScale { get; }

        internal static string RequireStableId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A stable candidate ID is required.");
            return value;
        }

        internal static Vector2 RequireFinite(Vector2 value)
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Candidate coordinates must be finite.");
            }
            return value;
        }
    }

    public sealed class BattlefieldWallCandidate
    {
        public BattlefieldWallCandidate(string stableId, BattlefieldLayer layer, Vector2 position, Vector2 guardPosition)
        {
            StableId = BattlefieldHouseCandidate.RequireStableId(stableId);
            Layer = layer;
            Position = BattlefieldHouseCandidate.RequireFinite(position);
            GuardPosition = BattlefieldHouseCandidate.RequireFinite(guardPosition);
        }

        public string StableId { get; }
        public BattlefieldLayer Layer { get; }
        public Vector2 Position { get; }
        public Vector2 GuardPosition { get; }
    }

    public sealed class BattlefieldGuardCandidate
    {
        public BattlefieldGuardCandidate(string stableId, BattlefieldLayer layer, BattlefieldGuardKind kind, Vector2 position)
        {
            if (kind == BattlefieldGuardKind.WallArcher)
            {
                throw new ArgumentException("Wall archers are derived from selected walls, not ground guard candidates.", nameof(kind));
            }
            StableId = BattlefieldHouseCandidate.RequireStableId(stableId);
            Layer = layer;
            Kind = kind;
            Position = BattlefieldHouseCandidate.RequireFinite(position);
        }

        public string StableId { get; }
        public BattlefieldLayer Layer { get; }
        public BattlefieldGuardKind Kind { get; }
        public Vector2 Position { get; }
    }

    public sealed class BattlefieldTargetCandidate
    {
        public BattlefieldTargetCandidate(string stableId, Vector2 position)
        {
            StableId = BattlefieldHouseCandidate.RequireStableId(stableId);
            Position = BattlefieldHouseCandidate.RequireFinite(position);
        }

        public string StableId { get; }
        public Vector2 Position { get; }
    }

    public sealed class LayeredBattlefieldLegalLayout
    {
        public LayeredBattlefieldLegalLayout(
            string stableId,
            IEnumerable<int> houseCandidateIndices,
            IEnumerable<int> wallCandidateIndices,
            IEnumerable<int> groundGuardCandidateIndices,
            int targetCandidateIndex)
        {
            StableId = BattlefieldHouseCandidate.RequireStableId(stableId);
            HouseCandidateIndices = Array.AsReadOnly((houseCandidateIndices ?? throw new ArgumentNullException(nameof(houseCandidateIndices))).ToArray());
            WallCandidateIndices = Array.AsReadOnly((wallCandidateIndices ?? throw new ArgumentNullException(nameof(wallCandidateIndices))).ToArray());
            GroundGuardCandidateIndices = Array.AsReadOnly((groundGuardCandidateIndices ?? throw new ArgumentNullException(nameof(groundGuardCandidateIndices))).ToArray());
            TargetCandidateIndex = targetCandidateIndex;
        }

        public string StableId { get; }
        public IReadOnlyList<int> HouseCandidateIndices { get; }
        public IReadOnlyList<int> WallCandidateIndices { get; }
        public IReadOnlyList<int> GroundGuardCandidateIndices { get; }
        public int TargetCandidateIndex { get; }
    }

    public sealed class LayeredBattlefieldLayoutConfig
    {
        public const float MapMinX = -55.5f;
        public const float MapMaxX = 48.5f;
        public const float MapMinZ = -53f;
        public const float MapMaxZ = 29f;
        public static readonly Rect PeripheralHouseBounds = Rect.MinMaxRect(-45f, -25f, 39f, -3f);
        public static readonly Vector2 PeripheralGuardOffset = new(0f, -3.5f);
        public const float PeripheralHouseMinimumSpacing = 9f;
        public const float PeripheralPairClearance = 5f;
        public const float PeripheralHouseZoneClearance = 3.5f;
        public const float PeripheralGuardZoneClearance = 1.5f;
        public const int PeripheralPlacementAttemptLimit = 4096;
        public LayeredBattlefieldLayoutConfig(
            IEnumerable<BattlefieldZonePreset> zonePresets,
            IEnumerable<BattlefieldHouseCandidate> houseCandidates,
            IEnumerable<BattlefieldWallCandidate> wallCandidates,
            IEnumerable<BattlefieldGuardCandidate> groundGuardCandidates,
            IEnumerable<BattlefieldTargetCandidate> targetCandidates,
            IEnumerable<LayeredBattlefieldLegalLayout> legalLayouts)
        {
            ZonePresets = Copy(zonePresets, nameof(zonePresets));
            HouseCandidates = Copy(houseCandidates, nameof(houseCandidates));
            WallCandidates = Copy(wallCandidates, nameof(wallCandidates));
            GroundGuardCandidates = Copy(groundGuardCandidates, nameof(groundGuardCandidates));
            TargetCandidates = Copy(targetCandidates, nameof(targetCandidates));
            LegalLayouts = Copy(legalLayouts, nameof(legalLayouts));
        }

        public IReadOnlyList<BattlefieldZonePreset> ZonePresets { get; }
        public IReadOnlyList<BattlefieldHouseCandidate> HouseCandidates { get; }
        public IReadOnlyList<BattlefieldWallCandidate> WallCandidates { get; }
        public IReadOnlyList<BattlefieldGuardCandidate> GroundGuardCandidates { get; }
        public IReadOnlyList<BattlefieldTargetCandidate> TargetCandidates { get; }
        public IReadOnlyList<LayeredBattlefieldLegalLayout> LegalLayouts { get; }

        public bool IsValid(out string error)
        {
            if (ZonePresets.Count != 3 || ZonePresets.Select(value => value.StableIndex).Distinct().Count() != 3)
            {
                error = "Exactly three uniquely indexed zone presets are required.";
                return false;
            }
            foreach (BattlefieldZonePreset preset in ZonePresets)
            {
                if (preset == null || preset.Zones.Count != 5 || preset.Zones.Select(value => value.ZoneId).Distinct().Count() != 5)
                {
                    error = "Every preset must contain exactly five uniquely identified zones.";
                    return false;
                }
                foreach (BattlefieldZoneDefinition zone in preset.Zones)
                {
                    Vector2[] vertices = { zone.Geometry.A, zone.Geometry.B, zone.Geometry.C, zone.Geometry.D };
                    if (vertices.Any(vertex => vertex.x < MapMinX || vertex.x > MapMaxX ||
                                               vertex.y < MapMinZ || vertex.y > MapMaxZ))
                    {
                        error = $"Zone preset {preset.StableIndex} contains a vertex outside the fixed map bounds.";
                        return false;
                    }
                }
                for (int first = 0; first < preset.Zones.Count; first++)
                {
                    for (int second = first + 1; second < preset.Zones.Count; second++)
                    {
                        if (preset.Zones[first].Geometry.Overlaps(preset.Zones[second].Geometry))
                        {
                            error = $"Zone preset {preset.StableIndex} contains overlapping quadrilaterals.";
                            return false;
                        }
                    }
                }
            }
            if (LegalLayouts.Count == 0)
            {
                error = "At least one prevalidated legal layout is required.";
                return false;
            }
            foreach (LayeredBattlefieldLegalLayout layout in LegalLayouts)
            {
                if (layout == null || layout.HouseCandidateIndices.Count != 8 || layout.WallCandidateIndices.Count != 3 ||
                    layout.GroundGuardCandidateIndices.Count != 2 || !IndicesAreUniqueAndInRange(layout.HouseCandidateIndices, HouseCandidates.Count) ||
                    !IndicesAreUniqueAndInRange(layout.WallCandidateIndices, WallCandidates.Count) ||
                    !IndicesAreUniqueAndInRange(layout.GroundGuardCandidateIndices, GroundGuardCandidates.Count) ||
                    layout.TargetCandidateIndex < 0 || layout.TargetCandidateIndex >= TargetCandidates.Count)
                {
                    error = $"Legal layout '{layout?.StableId ?? "<null>"}' references an invalid finite candidate set.";
                    return false;
                }

                BattlefieldHouseCandidate[] houses = layout.HouseCandidateIndices.Select(index => HouseCandidates[index]).ToArray();
                BattlefieldWallCandidate[] walls = layout.WallCandidateIndices.Select(index => WallCandidates[index]).ToArray();
                BattlefieldGuardCandidate[] guards = layout.GroundGuardCandidateIndices.Select(index => GroundGuardCandidates[index]).ToArray();
                if (houses.Count(value => value.Tier == BattlefieldHouseTier.Tier1) != 2 ||
                    houses.Count(value => value.Tier == BattlefieldHouseTier.Tier2) != 2 ||
                    houses.Count(value => value.Tier == BattlefieldHouseTier.Tier3) != 2 ||
                    houses.Count(value => value.Tier == BattlefieldHouseTier.Tier4) != 2 ||
                    houses.Any(value => (value.Tier == BattlefieldHouseTier.Tier4) != (value.Layer == BattlefieldLayer.Core)) ||
                    walls.Count(value => value.Layer == BattlefieldLayer.Middle) != 2 ||
                    walls.Count(value => value.Layer == BattlefieldLayer.Core) != 1 ||
                    guards.Count(value => value.Kind == BattlefieldGuardKind.Archer) != 1 ||
                    guards.Count(value => value.Kind == BattlefieldGuardKind.Mage) != 1 ||
                    guards.Any(value => value.Layer != BattlefieldLayer.Core) ||
                    !guards.Any(value => value.StableId == "core_archer_center") ||
                    !guards.Any(value => value.StableId == "core_mage_center"))
                {
                    error = $"Legal layout '{layout.StableId}' violates the approved layered counts.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        public static LayeredBattlefieldLayoutConfig CreateApprovedDefault()
        {
            BattlefieldZonePreset[] presets =
            {
                CreatePreset(0, 0f, 0f, 25f),
                CreatePreset(1, 2f, 1f, 25f),
                CreatePreset(2, -2f, 1.5f, 25f)
            };

            BattlefieldHouseCandidate[] houses =
            {
                new("outer_west_a_t1", BattlefieldLayer.Outer, BattlefieldHouseTier.Tier1, VillagePosition(-31f, -20f), 18f, 0.65f),
                new("outer_west_b_t1", BattlefieldLayer.Outer, BattlefieldHouseTier.Tier1, VillagePosition(-34f, -16f), -10f, 0.65f),
                new("outer_east_a_t1", BattlefieldLayer.Outer, BattlefieldHouseTier.Tier1, VillagePosition(25f, -20f), -18f, 0.65f),
                new("outer_east_b_t1", BattlefieldLayer.Outer, BattlefieldHouseTier.Tier1, VillagePosition(28f, -16f), 10f, 0.65f),
                new("middle_west_a_t2", BattlefieldLayer.Middle, BattlefieldHouseTier.Tier2, VillagePosition(-15f, -9f), 12f, 0.63f),
                new("middle_west_b_t3", BattlefieldLayer.Middle, BattlefieldHouseTier.Tier3, VillagePosition(-8f, -5f), -8f, 0.56f),
                new("middle_east_a_t2", BattlefieldLayer.Middle, BattlefieldHouseTier.Tier2, VillagePosition(13f, -9f), -12f, 0.63f),
                new("middle_east_b_t3", BattlefieldLayer.Middle, BattlefieldHouseTier.Tier3, VillagePosition(7f, -5f), 8f, 0.56f),
                new("middle_west_a_t3", BattlefieldLayer.Middle, BattlefieldHouseTier.Tier3, VillagePosition(-15f, -9f), 12f, 0.56f),
                new("middle_west_b_t2", BattlefieldLayer.Middle, BattlefieldHouseTier.Tier2, VillagePosition(-8f, -5f), -8f, 0.63f),
                new("middle_east_a_t3", BattlefieldLayer.Middle, BattlefieldHouseTier.Tier3, VillagePosition(13f, -9f), -12f, 0.56f),
                new("middle_east_b_t2", BattlefieldLayer.Middle, BattlefieldHouseTier.Tier2, VillagePosition(7f, -5f), 8f, 0.63f),
                new("core_west_t4", BattlefieldLayer.Core, BattlefieldHouseTier.Tier4, VillagePosition(-5f, 4f), 7f, 0.53f),
                new("core_east_t4", BattlefieldLayer.Core, BattlefieldHouseTier.Tier4, VillagePosition(5f, 4f), -7f, 0.53f)
            };

            BattlefieldWallCandidate[] walls =
            {
                new("middle_wall_west_outer", BattlefieldLayer.Middle, VillagePosition(-18f, -14f), VillagePosition(-18f, -13f)),
                new("middle_wall_east_outer", BattlefieldLayer.Middle, VillagePosition(18f, -14f), VillagePosition(18f, -13f)),
                new("middle_wall_west_inner", BattlefieldLayer.Middle, VillagePosition(-11f, -10f), VillagePosition(-11f, -9f)),
                new("middle_wall_east_inner", BattlefieldLayer.Middle, VillagePosition(11f, -10f), VillagePosition(11f, -9f)),
                new("core_wall_west", BattlefieldLayer.Core, VillagePosition(-6f, 0f), VillagePosition(-6f, 1f)),
                new("core_wall_east", BattlefieldLayer.Core, VillagePosition(6f, 0f), VillagePosition(6f, 1f))
            };

            BattlefieldGuardCandidate[] guards =
            {
                new("outer_archer_west_a", BattlefieldLayer.Outer, BattlefieldGuardKind.Archer, VillagePosition(-29f, -22f)),
                new("outer_archer_west_b", BattlefieldLayer.Outer, BattlefieldGuardKind.Archer, VillagePosition(-32f, -18f)),
                new("outer_archer_east_a", BattlefieldLayer.Outer, BattlefieldGuardKind.Archer, VillagePosition(23f, -22f)),
                new("outer_archer_east_b", BattlefieldLayer.Outer, BattlefieldGuardKind.Archer, VillagePosition(26f, -18f)),
                new("middle_archer_a", BattlefieldLayer.Middle, BattlefieldGuardKind.Archer, VillagePosition(-2f, -12f)),
                new("middle_archer_b", BattlefieldLayer.Middle, BattlefieldGuardKind.Archer, VillagePosition(2f, -12f)),
                new("middle_mage_a", BattlefieldLayer.Middle, BattlefieldGuardKind.Mage, VillagePosition(-1f, -7f)),
                new("middle_mage_b", BattlefieldLayer.Middle, BattlefieldGuardKind.Mage, VillagePosition(1f, -7f)),
                new("core_archer_west", BattlefieldLayer.Core, BattlefieldGuardKind.Archer, VillagePosition(-8f, 3f)),
                new("core_archer_center", BattlefieldLayer.Core, BattlefieldGuardKind.Archer, VillagePosition(0f, 2f)),
                new("core_archer_east", BattlefieldLayer.Core, BattlefieldGuardKind.Archer, VillagePosition(8f, 3f)),
                new("core_mage_west", BattlefieldLayer.Core, BattlefieldGuardKind.Mage, VillagePosition(-5f, 7f)),
                new("core_mage_center", BattlefieldLayer.Core, BattlefieldGuardKind.Mage, VillagePosition(0f, 7f)),
                new("core_mage_east", BattlefieldLayer.Core, BattlefieldGuardKind.Mage, VillagePosition(5f, 7f))
            };

            BattlefieldTargetCandidate[] targets =
            {
                new("core_target_center", VillagePosition(0f, 12f)),
                new("core_target_west", VillagePosition(-3f, 11f)),
                new("core_target_east", VillagePosition(3f, 11f))
            };

            int[] housesA = { 0, 2, 4, 5, 6, 7, 12, 13 };
            int[] housesB = { 1, 3, 8, 9, 10, 11, 12, 13 };
            LayeredBattlefieldLegalLayout[] layouts =
            {
                new("layout_0", housesA, new[] { 0, 1, 4 }, new[] { 9, 12 }, 0),
                new("layout_1", housesB, new[] { 2, 3, 5 }, new[] { 9, 12 }, 1),
                new("layout_2", housesA, new[] { 0, 3, 5 }, new[] { 9, 12 }, 2),
                new("layout_3", housesB, new[] { 1, 2, 4 }, new[] { 9, 12 }, 0)
            };
            LayeredBattlefieldLayoutConfig config = new(presets, houses, walls, guards, targets, layouts);
            if (!config.IsValid(out string error))
            {
                throw new InvalidOperationException($"Approved layered battlefield configuration is invalid: {error}");
            }
            return config;
        }

        private static BattlefieldZonePreset CreatePreset(
            int index,
            float sideShift,
            float centerShift,
            float zoneZShift)
        {
            Vector2 ZonePosition(
                float x,
                float z,
                float bandShift = 0f)
            {
                return DeploymentPosition(
                    x,
                    z + zoneZShift + bandShift);
            }

            return new BattlefieldZonePreset(index, new[]
            {
                new BattlefieldZoneDefinition(DeploymentZoneId.Z1RuinsForecourt, TerrainSurfaceKind.StoneRoad,
                    ZonePosition(-52f + sideShift, -47f, -5f), ZonePosition(-48f + sideShift, -34f, -5f), ZonePosition(-29f + sideShift, -31f, -5f), ZonePosition(-33f + sideShift, -43f, -5f)),
                new BattlefieldZoneDefinition(DeploymentZoneId.Z2OpenMeadow, TerrainSurfaceKind.Grass,
                    ZonePosition(-51f + sideShift, -28f, +5f), ZonePosition(-47f + sideShift, -16f, +5f), ZonePosition(-27f + sideShift, -14f, +5f), ZonePosition(-32f + sideShift, -27f, +5f)),
                new BattlefieldZoneDefinition(DeploymentZoneId.Z3RiverTerrace, TerrainSurfaceKind.ShallowWater,
                    ZonePosition(-13f + centerShift, -51f, -5f), ZonePosition(-15f + centerShift, -37f, -5f), ZonePosition(12f + centerShift, -37f, -5f), ZonePosition(10f + centerShift, -51f, -5f)),
                new BattlefieldZoneDefinition(DeploymentZoneId.Z4PalmGrove, TerrainSurfaceKind.Mud,
                    ZonePosition(26f - sideShift, -28f, +5f), ZonePosition(23f - sideShift, -14f, +5f), ZonePosition(43f - sideShift, -16f, +5f), ZonePosition(46f - sideShift, -29f, +5f)),
                new BattlefieldZoneDefinition(DeploymentZoneId.Z5RockyShelf, TerrainSurfaceKind.Slope,
                    ZonePosition(28f - sideShift, -47f, -5f), ZonePosition(25f - sideShift, -32f, -5f), ZonePosition(45f - sideShift, -34f, -5f), ZonePosition(46.5f - sideShift, -48f, -5f))
            });
        }

        private static Vector2 DeploymentPosition(float x, float z) => new(x, -24f - z);

        // Keep the village's centerline fixed while spreading every gameplay candidate outward.
        private const float VillageLayoutSpread = 1.5f;

        private static Vector2 VillagePosition(float x, float z) => new(
            x * VillageLayoutSpread,
            -27f - z * VillageLayoutSpread);

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source, string parameterName)
        {
            if (source == null) throw new ArgumentNullException(parameterName);
            return Array.AsReadOnly(source.ToArray());
        }

        private static bool IndicesAreUniqueAndInRange(IReadOnlyList<int> values, int count) =>
            values.All(value => value >= 0 && value < count) && values.Distinct().Count() == values.Count;
    }
}
