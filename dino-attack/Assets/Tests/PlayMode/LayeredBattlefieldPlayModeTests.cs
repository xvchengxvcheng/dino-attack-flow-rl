using System.Collections;
using System.Linq;
using System.Reflection;
using LlamAcademy.Dinos.Inference;
using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.Training;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class LayeredBattlefieldPlayModeTests
    {
        private const string SyntheticTrainedHash =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        [UnityTest]
        public IEnumerator GameResultCollector_RejectsDifferentActualLayoutSignature()
        {
            const int layoutSeed = 20260831;
            GameResultSnapshot player = new(
                GameMapId.LayeredBattlefield,
                GameMapId.LayeredBattlefield.SceneStableId,
                layoutSeed,
                GameControllerMode.Human,
                true,
                15f,
                200,
                12f,
                "layered_battlefield|20260831|layout-a",
                string.Empty,
                string.Empty,
                GameLaunchRequest.CurrentProtocolVersion);
            GameResultSnapshot ai = new(
                GameMapId.LayeredBattlefield,
                GameMapId.LayeredBattlefield.SceneStableId,
                layoutSeed,
                GameControllerMode.InferenceAI,
                false,
                40f,
                25,
                -1f,
                "layered_battlefield|20260831|layout-b",
                "default-v2",
                SyntheticTrainedHash,
                GameLaunchRequest.CurrentProtocolVersion);
            GameLaunchRequest request = GameLaunchContext.DeriveInferenceReplay(
                GameLaunchRequest.CreateHuman(GameMapId.LayeredBattlefield, layoutSeed),
                17,
                "default-v2",
                SyntheticTrainedHash,
                GameLaunchRequest.CurrentProtocolVersion);

            GameObject owner = new("GameResultCollector.Synthetic.Layered");
            System.Type collectorType = System.Type.GetType(
                "LlamAcademy.Dinos.Session.GameResultCollector, Assembly-CSharp",
                true);
            Component collector = owner.AddComponent(collectorType);
            try
            {
                collectorType.GetMethod("Configure").Invoke(
                    collector,
                    new object[] { request, (GameResultSnapshot?)player });
                Assert.That(
                    (bool)collectorType.GetMethod("TryFreeze").Invoke(collector, new object[] { ai }),
                    Is.True);
                Assert.That(Property<GameResultPairingFailure>(collector, "PairingFailure"),
                    Is.EqualTo(GameResultPairingFailure.LayoutSignatureMismatch));
                Assert.That(Property<GameResultComparison?>(collector, "Comparison"), Is.Null);
            }
            finally
            {
                Object.Destroy(owner);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator OpenLayeredSceneAppliesFormalCountsWithoutRuntimeBake()
        {
            {
                System.Type editorScenes = System.Type.GetType(
                    "UnityEditor.SceneManagement.EditorSceneManager, UnityEditor");
                MethodInfo loadMethod = editorScenes?.GetMethod(
                    "LoadSceneInPlayMode",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(LoadSceneParameters) },
                    null);
                Assert.That(loadMethod, Is.Not.Null,
                    "Editor PlayMode scene loading must be available without changing Build Settings.");
                loadMethod.Invoke(null, new object[]
                {
                    "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity",
                    new LoadSceneParameters(LoadSceneMode.Single)
                });
                yield return null;
            }
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("LayeredBattlefield"));
            yield return null;
            yield return null;

            Component layout = RuntimeComponent("LlamAcademy.Dinos.Map.Adapters.LayeredBattlefieldLayoutController");
            Component walls = RuntimeComponent("LlamAcademy.Dinos.Enemy.Defense.SessionDefenseLayoutController");
            Component ground = RuntimeComponent("LlamAcademy.Dinos.Enemy.Defense.SessionGroundDefenseController");
            Component navigation = RuntimeComponent("LlamAcademy.Dinos.RoundManagement.NavMeshManager");
            Component round = RuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component observations = RuntimeComponent("LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder");
            Component spawner = RuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");

            Assert.That(layout, Is.Not.Null);
            Assert.That(Property<bool>(layout, "IsApplied"), Is.True);
            Assert.That(Count(round, "SessionHouses"), Is.EqualTo(8));
            Assert.That(Property<int>(spawner, "ResourcesToSpend"), Is.EqualTo(50));
            Transform selectedTarget = Property<Transform>(round, "DinoTarget");
            Component eggRadius = (Component)round.GetType().GetField("EggRadius", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(round);
            Assert.That(eggRadius, Is.Not.Null);
            Assert.That(eggRadius.transform == selectedTarget || eggRadius.transform.IsChildOf(selectedTarget), Is.True,
                "The selected core target and the victory trigger must be the same candidate.");
            Assert.That(Count(walls, "ActiveWalls"), Is.EqualTo(3));
            Assert.That(Count(walls, "ActiveGuards"), Is.EqualTo(3));
            Assert.That(Count(ground, "ActiveGroundGuards"), Is.EqualTo(8));
            Assert.That(Count(walls, "ActiveGuards") + Count(ground, "ActiveGroundGuards"), Is.EqualTo(11));
            Assert.That(Property<int>(navigation, "RuntimeBuildCount"), Is.Zero);
            Assert.That(() => observations.GetType().GetMethod("BuildFrame", BindingFlags.Instance | BindingFlags.Public)
                .Invoke(observations, null), Throws.Nothing,
                "The formal v2 six-stream builder must consume all selected zones, houses, walls and 11 guards.");
        }

        [UnityTest]
        public IEnumerator DestroyedLayeredHouseHidesItsOwnHutAndShowsLocalBeams()
        {
            System.Type editorScenes = System.Type.GetType(
                "UnityEditor.SceneManagement.EditorSceneManager, UnityEditor");
            MethodInfo loadMethod = editorScenes?.GetMethod(
                "LoadSceneInPlayMode",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(LoadSceneParameters) },
                null);
            Assert.That(loadMethod, Is.Not.Null);
            loadMethod.Invoke(null, new object[]
            {
                "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity",
                new LoadSceneParameters(LoadSceneMode.Single)
            });

            for (int frame = 0; frame < 4; frame++)
            {
                yield return null;
            }

            System.Type houseType = System.Type.GetType(
                "LlamAcademy.Dinos.Unit.VillageHouse, Assembly-CSharp",
                true);
            Component house = Object.FindObjectsByType<Component>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .First(value => value.GetType() == houseType && value.gameObject.activeInHierarchy);
            Transform hut = house.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value.name.StartsWith("Hut Visual"));

            Assert.That(hut, Is.Not.Null, "An active map-two house must own its approved Hut visual.");
            Assert.That(hut.IsChildOf(house.transform), Is.True);
            Assert.That(hut.GetComponentsInChildren<Renderer>(true)
                .Any(value => value.enabled && value.gameObject.activeInHierarchy), Is.True);

            int health = (int)houseType.GetProperty("Health").GetValue(house);
            houseType.GetMethod("TakeDamage").Invoke(house, new object[] { health });
            yield return null;

            Assert.That((bool)houseType.GetProperty("IsDestroyed").GetValue(house), Is.True);
            Assert.That(hut.GetComponentsInChildren<Renderer>(true)
                .Any(value => value.enabled && value.gameObject.activeInHierarchy), Is.False,
                "Destroying a map-two house must hide the Hut that is actually visible on that house.");
            GameObject beams = (GameObject)houseType.GetProperty("DestroyedBeamRoot").GetValue(house);
            Collider occupancy = (Collider)houseType.GetProperty("Occupancy").GetValue(house);
            Assert.That(beams, Is.Not.Null);
            Assert.That(beams.transform.IsChildOf(house.transform), Is.True,
                "Each map-two house must use a local beam presentation instead of a disabled map-one reference.");
            Assert.That(beams.activeInHierarchy, Is.True);
            Assert.That(beams.GetComponentsInChildren<Renderer>(true)
                .Count(value => value.enabled && value.gameObject.activeInHierarchy), Is.InRange(3, 5));
            Assert.That(occupancy, Is.Not.Null);
            Assert.That(occupancy.enabled, Is.True,
                "The hidden gameplay occupancy must remain after the intact Hut disappears.");
        }

        [UnityTest]
        public IEnumerator InferenceControllerDisablesPlayerInputStartsRoundAndDeploysDecodedActionOnce()
        {
            {
                System.Type editorScenes = System.Type.GetType(
                    "UnityEditor.SceneManagement.EditorSceneManager, UnityEditor");
                MethodInfo loadMethod = editorScenes?.GetMethod(
                    "LoadSceneInPlayMode",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(LoadSceneParameters) },
                    null);
                Assert.That(loadMethod, Is.Not.Null);
                loadMethod.Invoke(null, new object[]
                {
                    "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity",
                    new LoadSceneParameters(LoadSceneMode.Single)
                });
                yield return null;
            }

            yield return null;
            yield return null;

            Component spawner = RuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            Component round = RuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component builder = RuntimeComponent("LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder");
            Assert.That(spawner, Is.Not.Null);
            Assert.That(round, Is.Not.Null);
            Assert.That(builder, Is.Not.Null);

            System.Type configType = System.Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig, Assembly-CSharp");
            System.Type controllerType = System.Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoInferenceController, Assembly-CSharp");
            Assert.That(configType, Is.Not.Null);
            Assert.That(controllerType, Is.Not.Null, "The runtime inference controller is missing.");

            ScriptableObject config = ScriptableObject.CreateInstance(configType);
            ScriptableObject syntheticModel = null;
            Component controller = null;
            try
            {
                void SetField(string field, object value) => configType
                    .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(config, value);
                FieldInfo modelField = configType.GetField(
                    "ModelAsset", BindingFlags.Instance | BindingFlags.NonPublic);
                syntheticModel = ScriptableObject.CreateInstance(modelField.FieldType);
                syntheticModel.name = "SyntheticTrainedModelForMockRunner";
                SetField("ModelAsset", syntheticModel);
                SetField("ModelId", "synthetic-controller-test");
                SetField("ModelSha256", SyntheticTrainedHash);
                SetField("ProtocolVersion", DinoInferenceModelContract.V2ProtocolVersion);
                SetField("ArtifactKind", DinoInferenceModelContract.TrainedCheckpointArtifactKind);

                GameLaunchRequest request = new(
                    GameMapId.LayeredBattlefield,
                    20260831,
                    GameControllerMode.InferenceAI,
                    20260831,
                    "synthetic-controller-test",
                    SyntheticTrainedHash,
                    GameLaunchRequest.CurrentProtocolVersion,
                    true,
                    true);
                FakeInferenceRunner runner = new(new[] { -1f, 0f, 0f, -0.25f });
                controller = spawner.gameObject.AddComponent(controllerType);
                MethodInfo configure = controllerType.GetMethod("TryConfigure", BindingFlags.Instance | BindingFlags.Public);
                object[] configureArgs = { request, config, runner, null };
                Assert.That((bool)configure.Invoke(controller, configureArgs), Is.True, configureArgs[3] as string);

                int foodBefore = Property<int>(spawner, "ResourcesToSpend");
                MethodInfo process = controllerType.GetMethod("TryProcessDecision", BindingFlags.Instance | BindingFlags.Public);
                object[] processArgs = { 100, null };
                Assert.That((bool)process.Invoke(controller, processArgs), Is.True, processArgs[1] as string);
                Assert.That(runner.CallCount, Is.EqualTo(1));
                Assert.That(runner.LastFrame, Is.SameAs(
                    builder.GetType().GetMethod("GetFrameForAcademyStep").Invoke(builder, new object[] { 100 })),
                    "The controller must pass the builder's six-stream frame directly to the runner.");
                Assert.That(Property<bool>(spawner, "PlayerInputEnabled"), Is.False);
                Assert.That(Property<int>(spawner, "ResourcesToSpend"), Is.LessThan(foodBefore),
                    "The decoded action must reach DinoSpawner.TryDeploy.");
                Assert.That(round.GetType().GetProperty("State").GetValue(round).ToString(), Is.EqualTo("Running"));

                object[] duplicateArgs = { 100, null };
                Assert.That((bool)process.Invoke(controller, duplicateArgs), Is.False);
                Assert.That(runner.CallCount, Is.EqualTo(1), "A duplicate decision step must be ignored.");

                spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 0);
                object[] rejectedArgs = { 125, null };
                Assert.That((bool)process.Invoke(controller, rejectedArgs), Is.False,
                    "A deployment without enough meat must be rejected.");
                object[] feedbackArgs = { 150, null };
                process.Invoke(controller, feedbackArgs);
                Assert.That(runner.LastFrame.Global[2], Is.EqualTo(0f),
                    "The next inference observation must report the rejected deployment exactly as training does.");
            }
            finally
            {
                if (controller != null)
                {
                    Object.Destroy(controller);
                }
                if (syntheticModel != null)
                {
                    Object.Destroy(syntheticModel);
                }
                Object.Destroy(config);
            }
        }

        private sealed class FakeInferenceRunner : IDinoInferencePolicyRunner
        {
            private readonly float[] Actions;

            public FakeInferenceRunner(float[] actions) => Actions = actions;

            public int CallCount { get; private set; }
            public DinoStructuredObservationFrame LastFrame { get; private set; }

            public bool TryRun(
                DinoStructuredObservationFrame frame,
                out float[] actions,
                out string failureReason)
            {
                CallCount++;
                LastFrame = frame;
                actions = (float[])Actions.Clone();
                failureReason = string.Empty;
                return true;
            }
        }

        private static Component RuntimeComponent(string fullName) =>
            Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SingleOrDefault(value => value != null && value.GetType().FullName == fullName);

        private static T Property<T>(Component component, string name) =>
            (T)component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).GetValue(component);

        private static int Count(Component component, string name) =>
            ((ICollection)component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .GetValue(component)).Count;
    }
}
