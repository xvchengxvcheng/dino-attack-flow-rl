using System.Linq;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Training;
using NUnit.Framework;
using UnityEngine;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class LayeredBattlefieldLayoutSelectorTests
    {
        [Test]
        public void Select_SameSeedProducesTheSameCompleteSignature()
        {
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();

            LayeredBattlefieldLayoutSelection first = LayeredBattlefieldLayoutSelector.Select(config, 20260831);
            LayeredBattlefieldLayoutSelection second = LayeredBattlefieldLayoutSelector.Select(config, 20260831);

            Assert.That(second.Signature, Is.EqualTo(first.Signature));
            Assert.That(first.Signature.MapStableId, Is.EqualTo("layered_battlefield"));
            Assert.That(first.Signature.LayoutSeed, Is.EqualTo(20260831));
            Assert.That(first.Signature.ZoneVertices, Has.Count.EqualTo(5));
            Assert.That(first.Signature.Houses, Has.Count.EqualTo(8));
            Assert.That(first.Signature.Walls, Has.Count.EqualTo(3));
            Assert.That(first.Signature.Guards, Has.Count.EqualTo(11));
            Assert.That(first.Signature.Targets, Has.Count.EqualTo(1));
        }

        [Test]
        public void Select_FixedSeedSetPreservesApprovedCountsAndCoversMultipleLayouts()
        {
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            LayeredBattlefieldLayoutSelection[] selections = Enumerable.Range(0, 24)
                .Select(seed => LayeredBattlefieldLayoutSelector.Select(config, seed))
                .ToArray();

            Assert.That(selections.Select(value => value.Signature.CanonicalText).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(selections.Select(value => value.ZonePresetIndex).Distinct().Count(), Is.GreaterThan(1));
            foreach (LayeredBattlefieldLayoutSelection selection in selections)
            {
                Assert.That(selection.ZonePresetIndex, Is.InRange(0, 2));
                Assert.That(selection.Houses.Count(value => value.Tier == BattlefieldHouseTier.Tier1), Is.EqualTo(2));
                Assert.That(selection.Houses.Count(value => value.Tier == BattlefieldHouseTier.Tier2), Is.EqualTo(2));
                Assert.That(selection.Houses.Count(value => value.Tier == BattlefieldHouseTier.Tier3), Is.EqualTo(2));
                Assert.That(selection.Houses.Count(value => value.Tier == BattlefieldHouseTier.Tier4), Is.EqualTo(2));
                Assert.That(selection.Walls.Count, Is.EqualTo(3));
                Assert.That(selection.Guards.Count(value => value.Kind == BattlefieldGuardKind.Archer), Is.EqualTo(5));
                Assert.That(selection.Guards.Count(value => value.Kind == BattlefieldGuardKind.Mage), Is.EqualTo(3));
                Assert.That(selection.Guards.Count(value => value.Kind == BattlefieldGuardKind.WallArcher), Is.EqualTo(3));
                Assert.That(selection.Target.StableId, Is.Not.Empty);
                Assert.That(selection.Signature.Houses.All(value => value.HasQuantizedCoordinates), Is.True);
                Assert.That(selection.Signature.Walls.All(value => value.HasQuantizedCoordinates), Is.True);
                Assert.That(selection.Signature.Guards.All(value => value.HasQuantizedCoordinates), Is.True);
                Assert.That(selection.Signature.Targets.All(value => value.HasQuantizedCoordinates), Is.True);
            }
        }

        [Test]
        public void Select_DoesNotConsumeUnityGlobalRandomState()
        {
            Random.InitState(917);
            _ = Random.value;
            Random.State expectedState = Random.state;

            _ = LayeredBattlefieldLayoutSelector.Select(
                LayeredBattlefieldLayoutConfig.CreateApprovedDefault(),
                33);
            float actualNext = Random.value;

            Random.state = expectedState;
            float expectedNext = Random.value;
            Assert.That(actualNext, Is.EqualTo(expectedNext));
        }

        [Test]
        public void Select_PeripheralHousesUseSeededPositionsInsideTheApprovedRectangle()
        {
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            LayeredBattlefieldLayoutSelection first = LayeredBattlefieldLayoutSelector.Select(config, 41);
            LayeredBattlefieldLayoutSelection replay = LayeredBattlefieldLayoutSelector.Select(config, 41);
            LayeredBattlefieldLayoutSelection different = LayeredBattlefieldLayoutSelector.Select(config, 42);

            SelectedHousePlacement[] peripheral = first.Houses
                .Where(value => value.Layer != BattlefieldLayer.Core)
                .OrderBy(value => value.StableId)
                .ToArray();
            SelectedHousePlacement[] replayPeripheral = replay.Houses
                .Where(value => value.Layer != BattlefieldLayer.Core)
                .OrderBy(value => value.StableId)
                .ToArray();
            SelectedHousePlacement[] differentPeripheral = different.Houses
                .Where(value => value.Layer != BattlefieldLayer.Core)
                .OrderBy(value => value.StableId)
                .ToArray();

            Assert.That(peripheral, Has.Length.EqualTo(6));
            Assert.That(peripheral.All(value => value.Position.x >= -45f && value.Position.x <= 39f &&
                                                value.Position.y >= -25f && value.Position.y <= -3f), Is.True);
            Assert.That(replayPeripheral.Select(value => value.Position),
                Is.EqualTo(peripheral.Select(value => value.Position)));
            Assert.That(differentPeripheral.Select(value => value.Position),
                Is.Not.EqualTo(peripheral.Select(value => value.Position)));

            foreach (SelectedHousePlacement core in first.Houses.Where(value => value.Layer == BattlefieldLayer.Core))
            {
                Vector2 authored = config.HouseCandidates.Single(value => value.StableId == core.StableId).Position;
                Assert.That(core.Position, Is.EqualTo(authored));
            }
        }

        [Test]
        public void Select_SixHouseGuardsFollowTheirHouseWhileTwoCoreGuardsStayFixed()
        {
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            LayeredBattlefieldLayoutSelection selection = LayeredBattlefieldLayoutSelector.Select(config, 73);
            SelectedGuardPlacement[] groundGuards = selection.Guards
                .Where(value => value.Kind != BattlefieldGuardKind.WallArcher)
                .ToArray();
            SelectedGuardPlacement[] followers = groundGuards
                .Where(value => value.StableId.StartsWith("house_guard_", System.StringComparison.Ordinal))
                .ToArray();
            SelectedGuardPlacement[] core = groundGuards
                .Where(value => !value.StableId.StartsWith("house_guard_", System.StringComparison.Ordinal))
                .OrderBy(value => value.StableId)
                .ToArray();

            Assert.That(followers, Has.Length.EqualTo(6));
            Assert.That(followers.Count(value => value.Kind == BattlefieldGuardKind.Archer), Is.EqualTo(4));
            Assert.That(followers.Count(value => value.Kind == BattlefieldGuardKind.Mage), Is.EqualTo(2));
            foreach (SelectedGuardPlacement guard in followers)
            {
                string houseId = guard.StableId.Substring("house_guard_".Length);
                SelectedHousePlacement house = selection.Houses.Single(value => value.StableId == houseId);
                Assert.That(house.Layer, Is.Not.EqualTo(BattlefieldLayer.Core));
                Assert.That(guard.Position, Is.EqualTo(house.Position + new Vector2(0f, -3.5f)));
            }

            Assert.That(core.Select(value => value.StableId),
                Is.EqualTo(new[] { "core_archer_center", "core_mage_center" }));
            Assert.That(core[0].Position,
                Is.EqualTo(config.GroundGuardCandidates.Single(value => value.StableId == "core_archer_center").Position));
            Assert.That(core[1].Position,
                Is.EqualTo(config.GroundGuardCandidates.Single(value => value.StableId == "core_mage_center").Position));
            Assert.That(groundGuards.Count(value => value.Kind == BattlefieldGuardKind.Archer), Is.EqualTo(5));
            Assert.That(groundGuards.Count(value => value.Kind == BattlefieldGuardKind.Mage), Is.EqualTo(3));
        }

        [Test]
        public void Select_PeripheralHouseGuardPairsRespectSpacingAndAvoidActiveDeploymentZones()
        {
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            for (int seed = 0; seed < 32; seed++)
            {
                LayeredBattlefieldLayoutSelection selection = LayeredBattlefieldLayoutSelector.Select(config, seed);
                SelectedHousePlacement[] peripheral = selection.Houses
                    .Where(value => value.Layer != BattlefieldLayer.Core)
                    .ToArray();
                SelectedGuardPlacement[] followers = selection.Guards
                    .Where(value => value.StableId.StartsWith("house_guard_", System.StringComparison.Ordinal))
                    .ToArray();

                for (int first = 0; first < peripheral.Length; first++)
                for (int second = first + 1; second < peripheral.Length; second++)
                {
                    Assert.That(Vector2.Distance(peripheral[first].Position, peripheral[second].Position),
                        Is.GreaterThanOrEqualTo(9f), $"seed {seed} house spacing");
                }

                foreach (SelectedHousePlacement house in peripheral)
                {
                    Assert.That(selection.Zones.Any(zone => zone.Geometry.Contains(house.Position)), Is.False,
                        $"seed {seed} house {house.StableId} entered a deployment zone");
                    Assert.That(selection.Zones.Min(zone => zone.Geometry.DistanceTo(house.Position)),
                        Is.GreaterThanOrEqualTo(3.5f),
                        $"seed {seed} house {house.StableId} footprint entered a deployment zone");
                }
                foreach (SelectedGuardPlacement guard in followers)
                {
                    Assert.That(selection.Zones.Any(zone => zone.Geometry.Contains(guard.Position)), Is.False,
                        $"seed {seed} guard {guard.StableId} entered a deployment zone");
                    Assert.That(selection.Zones.Min(zone => zone.Geometry.DistanceTo(guard.Position)),
                        Is.GreaterThanOrEqualTo(1.5f),
                        $"seed {seed} guard {guard.StableId} footprint entered a deployment zone");
                }
            }
        }

        [Test]
        public void Select_PeripheralPairsKeepClearOfSelectedFixedVillageObjects()
        {
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            for (int seed = 0; seed < 128; seed++)
            {
                LayeredBattlefieldLayoutSelection selection = LayeredBattlefieldLayoutSelector.Select(config, seed);
                Vector2[] fixedPositions = selection.Houses
                    .Where(value => value.Layer == BattlefieldLayer.Core)
                    .Select(value => value.Position)
                    .Concat(selection.Walls.SelectMany(value => new[] { value.Position, value.GuardPosition }))
                    .Append(selection.Target.Position)
                    .Concat(selection.Guards
                        .Where(value => value.StableId == "core_archer_center" || value.StableId == "core_mage_center")
                        .Select(value => value.Position))
                    .ToArray();
                Vector2[] pairPositions = selection.Houses
                    .Where(value => value.Layer != BattlefieldLayer.Core)
                    .Select(value => value.Position)
                    .Concat(selection.Guards
                        .Where(value => value.StableId.StartsWith("house_guard_", System.StringComparison.Ordinal))
                        .Select(value => value.Position))
                    .ToArray();

                foreach (Vector2 pairPosition in pairPositions)
                foreach (Vector2 fixedPosition in fixedPositions)
                {
                    Assert.That(Vector2.Distance(pairPosition, fixedPosition), Is.GreaterThanOrEqualTo(5f),
                        $"seed {seed} pair position {pairPosition} overlaps fixed village object {fixedPosition}");
                }
            }
        }

        [Test]
        public void Select_RejectsAnUnapprovedOrMalformedConfiguration()
        {
            Assert.That(
                () => LayeredBattlefieldLayoutSelector.Select(null, 1),
                Throws.ArgumentNullException);
            Assert.That(
                () => LayeredBattlefieldLayoutSelector.Select(
                    new LayeredBattlefieldLayoutConfig(
                        LayeredBattlefieldLayoutConfig.CreateApprovedDefault().ZonePresets,
                        LayeredBattlefieldLayoutConfig.CreateApprovedDefault().HouseCandidates,
                        LayeredBattlefieldLayoutConfig.CreateApprovedDefault().WallCandidates,
                        LayeredBattlefieldLayoutConfig.CreateApprovedDefault().GroundGuardCandidates,
                        LayeredBattlefieldLayoutConfig.CreateApprovedDefault().TargetCandidates,
                        System.Array.Empty<LayeredBattlefieldLegalLayout>()),
                    1),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ApprovedConfig_AllZoneVerticesRemainInsideTheFixedMapBounds()
        {
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            foreach (BattlefieldZonePreset preset in config.ZonePresets)
            foreach (BattlefieldZoneDefinition zone in preset.Zones)
            foreach (Vector2 vertex in new[] { zone.Geometry.A, zone.Geometry.B, zone.Geometry.C, zone.Geometry.D })
            {
                Assert.That(vertex.x, Is.InRange(LayeredBattlefieldLayoutConfig.MapMinX, LayeredBattlefieldLayoutConfig.MapMaxX));
                Assert.That(vertex.y, Is.InRange(LayeredBattlefieldLayoutConfig.MapMinZ, LayeredBattlefieldLayoutConfig.MapMaxZ));
            }
        }

        [Test]
        public void Config_OutOfBoundsZoneFailsClosed()
        {
            LayeredBattlefieldLayoutConfig approved = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            BattlefieldZonePreset original = approved.ZonePresets[2];
            BattlefieldZoneDefinition[] zones = original.Zones.ToArray();
            zones[4] = new BattlefieldZoneDefinition(DeploymentZoneId.Z5RockyShelf, TerrainSurfaceKind.Slope,
                new Vector2(49f, 15f), new Vector2(50f, 15f), new Vector2(50f, 25f), new Vector2(49f, 25f));
            BattlefieldZonePreset[] presets = approved.ZonePresets.ToArray();
            presets[2] = new BattlefieldZonePreset(original.StableIndex, zones);
            LayeredBattlefieldLayoutConfig invalid = new(presets, approved.HouseCandidates, approved.WallCandidates,
                approved.GroundGuardCandidates, approved.TargetCandidates, approved.LegalLayouts);

            Assert.That(invalid.IsValid(out string error), Is.False);
            Assert.That(error, Does.Contain("bounds"));
            Assert.That(() => LayeredBattlefieldLayoutSelector.Select(invalid, 7), Throws.InvalidOperationException);
        }

        [Test]
        public void Config_TierFourOutsideCoreFailsClosed()
        {
            LayeredBattlefieldLayoutConfig approved = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            BattlefieldHouseCandidate[] houses = approved.HouseCandidates.ToArray();
            BattlefieldHouseCandidate core = houses[12];
            houses[12] = new BattlefieldHouseCandidate(core.StableId, BattlefieldLayer.Middle, core.Tier, core.Position);
            LayeredBattlefieldLayoutConfig invalid = new(
                approved.ZonePresets,
                houses,
                approved.WallCandidates,
                approved.GroundGuardCandidates,
                approved.TargetCandidates,
                approved.LegalLayouts);

            Assert.That(invalid.IsValid(out string error), Is.False);
            Assert.That(error, Does.Contain("layered counts"));
        }

        [Test]
        public void TrainingMapSelector_UsesStableExactHalfPairsWithoutSceneState()
        {
            for (int seed = -20; seed <= 20; seed += 2)
            {
                DinoTrainingMap first = DinoTrainingMapSelector.Select(seed);
                DinoTrainingMap second = DinoTrainingMapSelector.Select(seed + 1);

                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(DinoTrainingMapSelector.Select(seed), Is.EqualTo(first));
            }
        }

    }
}
