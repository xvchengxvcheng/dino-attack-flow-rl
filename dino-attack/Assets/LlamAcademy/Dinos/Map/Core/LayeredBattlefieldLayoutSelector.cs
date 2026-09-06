using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    public readonly struct SelectedHousePlacement
    {
        public SelectedHousePlacement(int candidateIndex, BattlefieldHouseCandidate candidate)
            : this(candidateIndex, candidate, candidate.Position)
        {
        }

        public SelectedHousePlacement(int candidateIndex, BattlefieldHouseCandidate candidate, Vector2 position)
        {
            CandidateIndex = candidateIndex;
            StableId = candidate.StableId;
            Layer = candidate.Layer;
            Tier = candidate.Tier;
            Position = BattlefieldHouseCandidate.RequireFinite(position);
        }

        public int CandidateIndex { get; }
        public string StableId { get; }
        public BattlefieldLayer Layer { get; }
        public BattlefieldHouseTier Tier { get; }
        public Vector2 Position { get; }
    }

    public readonly struct SelectedWallPlacement
    {
        public SelectedWallPlacement(int candidateIndex, BattlefieldWallCandidate candidate)
        {
            CandidateIndex = candidateIndex;
            StableId = candidate.StableId;
            Layer = candidate.Layer;
            Position = candidate.Position;
            GuardPosition = candidate.GuardPosition;
        }

        public int CandidateIndex { get; }
        public string StableId { get; }
        public BattlefieldLayer Layer { get; }
        public Vector2 Position { get; }
        public Vector2 GuardPosition { get; }
    }

    public readonly struct SelectedGuardPlacement
    {
        public SelectedGuardPlacement(int candidateIndex, BattlefieldGuardCandidate candidate)
        {
            CandidateIndex = candidateIndex;
            StableId = candidate.StableId;
            Layer = candidate.Layer;
            Kind = candidate.Kind;
            Position = candidate.Position;
        }

        public SelectedGuardPlacement(string stableId, BattlefieldLayer layer, Vector2 position)
            : this(stableId, layer, BattlefieldGuardKind.WallArcher, position)
        {
        }

        public SelectedGuardPlacement(string stableId, BattlefieldLayer layer, BattlefieldGuardKind kind, Vector2 position)
        {
            CandidateIndex = -1;
            StableId = BattlefieldHouseCandidate.RequireStableId(stableId);
            Layer = layer;
            Kind = kind;
            Position = BattlefieldHouseCandidate.RequireFinite(position);
        }

        public int CandidateIndex { get; }
        public string StableId { get; }
        public BattlefieldLayer Layer { get; }
        public BattlefieldGuardKind Kind { get; }
        public Vector2 Position { get; }
    }

    public readonly struct SelectedTargetPlacement
    {
        public SelectedTargetPlacement(int candidateIndex, BattlefieldTargetCandidate candidate)
        {
            CandidateIndex = candidateIndex;
            StableId = candidate.StableId;
            Position = candidate.Position;
        }

        public int CandidateIndex { get; }
        public string StableId { get; }
        public Vector2 Position { get; }
    }

    public sealed class LayeredBattlefieldLayoutSelection
    {
        internal LayeredBattlefieldLayoutSelection(
            int layoutSeed,
            int zonePresetIndex,
            string legalLayoutStableId,
            IReadOnlyList<BattlefieldZoneDefinition> zones,
            IReadOnlyList<SelectedHousePlacement> houses,
            IReadOnlyList<SelectedWallPlacement> walls,
            IReadOnlyList<SelectedGuardPlacement> guards,
            SelectedTargetPlacement target,
            BattlefieldLayoutSignature signature)
        {
            LayoutSeed = layoutSeed;
            ZonePresetIndex = zonePresetIndex;
            LegalLayoutStableId = legalLayoutStableId;
            Zones = zones;
            Houses = houses;
            Walls = walls;
            Guards = guards;
            Target = target;
            Signature = signature;
        }

        public int LayoutSeed { get; }
        public int ZonePresetIndex { get; }
        public string LegalLayoutStableId { get; }
        public IReadOnlyList<BattlefieldZoneDefinition> Zones { get; }
        public IReadOnlyList<SelectedHousePlacement> Houses { get; }
        public IReadOnlyList<SelectedWallPlacement> Walls { get; }
        public IReadOnlyList<SelectedGuardPlacement> Guards { get; }
        public SelectedTargetPlacement Target { get; }
        public BattlefieldLayoutSignature Signature { get; }
    }

    public static class LayeredBattlefieldLayoutSelector
    {
        public static LayeredBattlefieldLayoutSelection Select(
            LayeredBattlefieldLayoutConfig config,
            int layoutSeed)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!config.IsValid(out string error)) throw new InvalidOperationException(error);

            LocalRandom random = new(unchecked((uint)layoutSeed) ^ 0xB5297A4Du);
            BattlefieldZonePreset preset = config.ZonePresets[random.NextIndex(config.ZonePresets.Count)];
            LayeredBattlefieldLegalLayout layout = config.LegalLayouts[random.NextIndex(config.LegalLayouts.Count)];

            SelectedHousePlacement[] authoredHouses = layout.HouseCandidateIndices
                .Select(index => new SelectedHousePlacement(index, config.HouseCandidates[index]))
                .ToArray();
            SelectedWallPlacement[] walls = layout.WallCandidateIndices
                .Select(index => new SelectedWallPlacement(index, config.WallCandidates[index]))
                .ToArray();
            SelectedTargetPlacement target = new(
                layout.TargetCandidateIndex,
                config.TargetCandidates[layout.TargetCandidateIndex]);
            SelectedGuardPlacement[] fixedGroundGuards = layout.GroundGuardCandidateIndices
                .Select(index => new SelectedGuardPlacement(index, config.GroundGuardCandidates[index]))
                .ToArray();
            SelectedHousePlacement[] houses = PlacePeripheralHouseGuardPairs(
                authoredHouses,
                preset.Zones,
                walls,
                fixedGroundGuards,
                target,
                ref random);
            List<SelectedGuardPlacement> guards = fixedGroundGuards.ToList();
            foreach (SelectedHousePlacement house in houses.Where(value => value.Layer != BattlefieldLayer.Core))
            {
                guards.Add(new SelectedGuardPlacement(
                    $"house_guard_{house.StableId}",
                    house.Layer,
                    house.Tier == BattlefieldHouseTier.Tier3 ? BattlefieldGuardKind.Mage : BattlefieldGuardKind.Archer,
                    house.Position + LayeredBattlefieldLayoutConfig.PeripheralGuardOffset));
            }
            foreach (SelectedWallPlacement wall in walls)
            {
                guards.Add(new SelectedGuardPlacement(
                    $"wall_archer_{wall.StableId}",
                    wall.Layer,
                    wall.GuardPosition));
            }
            BattlefieldZoneSignature[] zoneSignatures = preset.Zones.Select(zone =>
                new BattlefieldZoneSignature(zone.ZoneId, zone.Geometry.Vertices)).ToArray();
            BattlefieldLayoutSignature signature = BattlefieldLayoutSignature.Create(
                "layered_battlefield",
                layoutSeed,
                preset.StableIndex,
                zoneSignatures,
                houses.Select(value => new BattlefieldEntitySignature(value.StableId, value.Position)),
                walls.Select(value => new BattlefieldEntitySignature(value.StableId, value.Position)),
                guards.Select(value => new BattlefieldEntitySignature(value.StableId, value.Position)),
                new[] { new BattlefieldEntitySignature(target.StableId, target.Position) });

            return new LayeredBattlefieldLayoutSelection(
                layoutSeed,
                preset.StableIndex,
                layout.StableId,
                preset.Zones,
                Array.AsReadOnly(houses),
                Array.AsReadOnly(walls),
                guards.AsReadOnly(),
                target,
                signature);
        }

        private static SelectedHousePlacement[] PlacePeripheralHouseGuardPairs(
            IReadOnlyList<SelectedHousePlacement> authoredHouses,
            IReadOnlyList<BattlefieldZoneDefinition> zones,
            IReadOnlyList<SelectedWallPlacement> walls,
            IReadOnlyList<SelectedGuardPlacement> fixedGroundGuards,
            SelectedTargetPlacement target,
            ref LocalRandom random)
        {
            List<SelectedHousePlacement> placed = new(authoredHouses.Count);
            List<Vector2> peripheralHouses = new(6);
            List<Vector2> peripheralGuards = new(6);
            Vector2[] fixedObstacles = authoredHouses
                .Where(value => value.Layer == BattlefieldLayer.Core)
                .Select(value => value.Position)
                .Concat(walls.SelectMany(value => new[] { value.Position, value.GuardPosition }))
                .Concat(fixedGroundGuards.Select(value => value.Position))
                .Append(target.Position)
                .ToArray();

            foreach (SelectedHousePlacement house in authoredHouses)
            {
                if (house.Layer == BattlefieldLayer.Core)
                {
                    placed.Add(house);
                    continue;
                }

                bool found = false;
                for (int attempt = 0; attempt < LayeredBattlefieldLayoutConfig.PeripheralPlacementAttemptLimit; attempt++)
                {
                    Vector2 housePosition = random.NextPoint(LayeredBattlefieldLayoutConfig.PeripheralHouseBounds);
                    Vector2 guardPosition = housePosition + LayeredBattlefieldLayoutConfig.PeripheralGuardOffset;
                    if (!IsInsideMap(guardPosition) ||
                        zones.Any(zone => zone.Geometry.DistanceTo(housePosition) < LayeredBattlefieldLayoutConfig.PeripheralHouseZoneClearance ||
                                          zone.Geometry.DistanceTo(guardPosition) < LayeredBattlefieldLayoutConfig.PeripheralGuardZoneClearance) ||
                        peripheralHouses.Any(position => Vector2.Distance(position, housePosition) < LayeredBattlefieldLayoutConfig.PeripheralHouseMinimumSpacing) ||
                        peripheralGuards.Any(position => Vector2.Distance(position, housePosition) < LayeredBattlefieldLayoutConfig.PeripheralPairClearance) ||
                        peripheralHouses.Any(position => Vector2.Distance(position, guardPosition) < LayeredBattlefieldLayoutConfig.PeripheralPairClearance) ||
                        fixedObstacles.Any(position => Vector2.Distance(position, housePosition) < LayeredBattlefieldLayoutConfig.PeripheralPairClearance ||
                                                       Vector2.Distance(position, guardPosition) < LayeredBattlefieldLayoutConfig.PeripheralPairClearance))
                    {
                        continue;
                    }

                    placed.Add(new SelectedHousePlacement(house.CandidateIndex,
                        new BattlefieldHouseCandidate(house.StableId, house.Layer, house.Tier, house.Position),
                        housePosition));
                    peripheralHouses.Add(housePosition);
                    peripheralGuards.Add(guardPosition);
                    found = true;
                    break;
                }

                if (!found)
                {
                    throw new InvalidOperationException($"Could not place peripheral house '{house.StableId}' and its guard without overlap.");
                }
            }

            return placed.ToArray();
        }

        private static bool IsInsideMap(Vector2 point) =>
            point.x >= LayeredBattlefieldLayoutConfig.MapMinX &&
            point.x <= LayeredBattlefieldLayoutConfig.MapMaxX &&
            point.y >= LayeredBattlefieldLayoutConfig.MapMinZ &&
            point.y <= LayeredBattlefieldLayoutConfig.MapMaxZ;

        private struct LocalRandom
        {
            private uint State;

            public LocalRandom(uint seed)
            {
                State = seed;
            }

            public int NextIndex(int count)
            {
                if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
                State += 0x9E3779B9u;
                uint value = State;
                value = (value ^ (value >> 16)) * 0x85EBCA6Bu;
                value = (value ^ (value >> 13)) * 0xC2B2AE35u;
                value ^= value >> 16;
                return (int)(value % (uint)count);
            }

            public Vector2 NextPoint(Rect bounds) => new(
                bounds.xMin + NextUnitFloat() * bounds.width,
                bounds.yMin + NextUnitFloat() * bounds.height);

            private float NextUnitFloat()
            {
                State += 0x9E3779B9u;
                uint value = State;
                value = (value ^ (value >> 16)) * 0x85EBCA6Bu;
                value = (value ^ (value >> 13)) * 0xC2B2AE35u;
                value ^= value >> 16;
                return (value >> 8) * (1f / 16777216f);
            }
        }
    }
}
