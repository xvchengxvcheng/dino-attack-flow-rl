using System;
using System.Linq;
using System.Reflection;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEngine;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class GameLaunchContextTests
    {
        [SetUp]
        public void SetUp()
        {
            GameLaunchContext.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GameLaunchContext.Clear();
        }

        [Test]
        public void FourEndedActions_RouteMapSeedControllerAndSnapshotExplicitly()
        {
            GameLaunchRequest current = GameLaunchRequest.CreateHuman(
                GameMapId.OpenTropicalBattlefield,
                12345);
            GameResultSnapshot player = new(
                GameMapId.OpenTropicalBattlefield,
                GameMapId.OpenTropicalBattlefield.SceneStableId,
                12345,
                GameControllerMode.Human,
                true,
                12.5f,
                175,
                11.75f,
                "open_tropical_battlefield|12345|test-layout",
                string.Empty,
                string.Empty,
                GameLaunchRequest.CurrentProtocolVersion);

            GameLaunchRequest retry = GameLaunchContext.DeriveRetry(current);
            GameLaunchRequest newLayout = GameLaunchContext.DeriveNewLayout(current);
            GameLaunchRequest inference = GameLaunchContext.DeriveInferenceReplay(
                current,
                77,
                "default-v2",
                new string('a', 64),
                GameLaunchRequest.CurrentProtocolVersion);
            GameLaunchRequest mapSelect = GameLaunchContext.CreateMapSelectionRequest();

            Assert.That(retry.MapId, Is.EqualTo(current.MapId));
            Assert.That(retry.LayoutSeed, Is.EqualTo(current.LayoutSeed));
            Assert.That(retry.Controller, Is.EqualTo(GameControllerMode.Human));
            Assert.That(retry.ReuseLayoutSeed, Is.True);
            Assert.That(newLayout.MapId, Is.EqualTo(current.MapId));
            Assert.That(newLayout.LayoutSeed, Is.Not.EqualTo(current.LayoutSeed));
            Assert.That(newLayout.Controller, Is.EqualTo(GameControllerMode.Human));
            Assert.That(newLayout.ReuseLayoutSeed, Is.False);
            Assert.That(inference.MapId, Is.EqualTo(current.MapId));
            Assert.That(inference.LayoutSeed, Is.EqualTo(current.LayoutSeed));
            Assert.That(inference.Controller, Is.EqualTo(GameControllerMode.InferenceAI));
            Assert.That(inference.ReuseLayoutSeed, Is.True);
            Assert.That(mapSelect.MapId, Is.EqualTo(GameMapId.MapSelect));
            Assert.That(mapSelect.Controller, Is.EqualTo(GameControllerMode.Human));
            Assert.That(mapSelect.TargetSceneName, Is.EqualTo("MapSelect"));
            Assert.That(retry.TargetSceneName, Is.EqualTo("Dinos"));
            Assert.That(inference.TargetSceneStableId, Is.EqualTo("open_tropical_battlefield"));

            Assert.That(GameLaunchContext.TryPublish(inference, player), Is.True);
            Assert.That(
                GameLaunchContext.TryConsume(
                    GameMapId.OpenTropicalBattlefield.SceneStableId,
                    out GameLaunchRequest consumed,
                    out GameResultSnapshot? consumedPlayer),
                Is.True);
            Assert.That(consumed, Is.EqualTo(inference));
            Assert.That(consumedPlayer, Is.EqualTo(player));
        }

        [Test]
        public void Values_DoNotRetainUnityEngineObjectReferences()
        {
            Type[] valueTypes =
            {
                typeof(GameMapId),
                typeof(GameLaunchRequest),
                typeof(GameResultSnapshot),
            };

            foreach (Type valueType in valueTypes)
            {
                FieldInfo[] fields = valueType.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.That(
                    fields.Any(field => typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)),
                    Is.False,
                    $"{valueType.Name} must not retain a scene object reference.");
            }
        }

        [Test]
        public void InvalidMapSeedOrProtocol_FailsClosed()
        {
            Assert.That(
                GameLaunchRequest.TryCreate(
                    GameMapId.Unknown,
                    10,
                    GameControllerMode.Human,
                    0,
                    string.Empty,
                    string.Empty,
                    GameLaunchRequest.CurrentProtocolVersion,
                    out _),
                Is.False);
            Assert.That(
                GameLaunchRequest.TryCreate(
                    GameMapId.OpenTropicalBattlefield,
                    int.MinValue,
                    GameControllerMode.Human,
                    0,
                    string.Empty,
                    string.Empty,
                    GameLaunchRequest.CurrentProtocolVersion,
                    out _),
                Is.False);
            Assert.That(
                GameLaunchRequest.TryCreate(
                    GameMapId.OpenTropicalBattlefield,
                    10,
                    GameControllerMode.InferenceAI,
                    1,
                    "default-v2",
                    new string('b', 64),
                    "dino_attack_structured_set_v1",
                    out _),
                Is.False);
        }

        [Test]
        public void SeedDerivation_IsDeterministicAndNeverUsesReservedInvalidSeed()
        {
            int first = GameLaunchContext.DeriveNextLayoutSeed(12345);
            int repeated = GameLaunchContext.DeriveNextLayoutSeed(12345);

            Assert.That(first, Is.EqualTo(repeated));
            Assert.That(first, Is.Not.EqualTo(12345));
            Assert.That(first, Is.Not.EqualTo(int.MinValue));
        }

        [Test]
        public void PendingRequest_IsOneShotSceneBoundAndCannotBeOverwritten()
        {
            GameLaunchRequest first = GameLaunchRequest.CreateHuman(
                GameMapId.OpenTropicalBattlefield,
                100);
            GameLaunchRequest second = GameLaunchRequest.CreateHuman(
                GameMapId.LayeredBattlefield,
                200);

            Assert.That(GameLaunchContext.TryPublish(first), Is.True);
            Assert.That(GameLaunchContext.TryPublish(second), Is.False,
                "A duplicate click must not replace the already scheduled launch.");
            Assert.That(
                GameLaunchContext.TryConsume(
                    GameMapId.LayeredBattlefield.SceneStableId,
                    out _,
                    out _),
                Is.False,
                "The wrong scene must not consume another scene's launch.");
            Assert.That(
                GameLaunchContext.TryConsume(
                    GameMapId.OpenTropicalBattlefield.SceneStableId,
                    out GameLaunchRequest consumed,
                    out _),
                Is.True);
            Assert.That(consumed, Is.EqualTo(first));
            Assert.That(
                GameLaunchContext.TryConsume(
                    GameMapId.OpenTropicalBattlefield.SceneStableId,
                    out _,
                    out _),
                Is.False,
                "A consumed launch must not leak into the next session.");
        }
    }
}
