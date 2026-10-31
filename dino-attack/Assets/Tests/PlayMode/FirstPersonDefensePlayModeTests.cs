using System.Collections;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using LlamAcademy.Dinos.DefenseBattle;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class FirstPersonDefensePlayModeTests
    {
        [UnityTest]
        public IEnumerator OpeningDefenseBattlefield_StaysInSetupWithoutAutomaticMapTwoDefenses()
        {
            ClearLaunchState();
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
                "Assets/LlamAcademy/Dinos/Scenes/FirstPersonDefenseBattlefield.unity",
                new LoadSceneParameters(LoadSceneMode.Single)
            });

            for (int frame = 0; frame < 4; frame++) yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("FirstPersonDefenseBattlefield"));
            Component round = RuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component spawner = RuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            Component walls = RuntimeComponent("LlamAcademy.Dinos.Enemy.Defense.SessionDefenseLayoutController");
            Component ground = RuntimeComponent("LlamAcademy.Dinos.Enemy.Defense.SessionGroundDefenseController");
            Component placement = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefensePlacementController");
            Component layout = RuntimeComponent("LlamAcademy.Dinos.Map.Adapters.LayeredBattlefieldLayoutController");

            Assert.That(round.GetType().GetProperty("State").GetValue(round).ToString(), Is.EqualTo("Setup"));
            Assert.That((int)spawner.GetType().GetProperty("ResourcesToSpend").GetValue(spawner), Is.EqualTo(50));
            Assert.That(Count(walls, "ActiveWalls"), Is.Zero);
            Assert.That(Count(walls, "ActiveGuards"), Is.Zero);
            Assert.That(Count(ground, "ActiveGroundGuards"), Is.Zero);
            Assert.That((bool)placement.GetType().GetProperty("IsComplete").GetValue(placement), Is.False);
            Assert.That((bool)placement.GetType().GetProperty("IsLocked").GetValue(placement), Is.False);
            Assert.That((bool)layout.GetType().GetProperty("IsApplied").GetValue(layout), Is.True);
            GameObject deploymentArea = GameObject.Find("Defense Deployment Area");
            Assert.That(deploymentArea, Is.Not.Null);
            MeshRenderer fillRenderer = deploymentArea.transform.Find("Translucent Fill")
                ?.GetComponent<MeshRenderer>();
            Assert.That(fillRenderer, Is.Not.Null);
            Material fillMaterial = fillRenderer.sharedMaterial;
            Assert.That(fillMaterial, Is.Not.Null);
            Assert.That(fillMaterial.color.a, Is.InRange(0.05f, 0.25f));
            Assert.That(fillMaterial.renderQueue, Is.GreaterThanOrEqualTo(3000),
                "The deployment fill must use the transparent render queue.");
            if (fillMaterial.HasProperty("_Surface"))
                Assert.That(fillMaterial.GetFloat("_Surface"), Is.EqualTo(1f).Within(0.01f));

            UIDocument document = Object.FindFirstObjectByType<UIDocument>(FindObjectsInactive.Include);
            Assert.That(document, Is.Not.Null);
            VisualElement root = document.rootVisualElement;
            Assert.That(root.style.unityFontDefinition.value.font, Is.Not.Null,
                "Map3 must use a runtime font with Chinese glyph support in Player builds.");
            Button wallButton = root.Q<Button>("wall-button");
            Button archerButton = root.Q<Button>("archer-button");
            Button mageButton = root.Q<Button>("mage-button");
            Assert.That(wallButton.text, Does.Contain("城墙"));
            Assert.That(archerButton.text, Does.Contain("弓箭手"));
            Assert.That(mageButton.text, Does.Contain("法师"));
            Assert.That(root.Q<Label>("phase-label").resolvedStyle.fontSize, Is.GreaterThanOrEqualTo(26f));
            Assert.That(wallButton.resolvedStyle.fontSize, Is.GreaterThanOrEqualTo(22f));
            Assert.That(root.Q<Button>("start-button").resolvedStyle.fontSize, Is.GreaterThanOrEqualTo(22f));
        }

        [UnityTest]
        public IEnumerator CompleteDefenseLayout_StartsRealPpoInSameSceneAndLocksPlacement()
            => StartRealStrategyAndRetry(DefenseAiStrategy.Ppo, "PpoOption");

        [UnityTest]
        public IEnumerator CompleteDefenseLayout_StartsRealPolicyFlowInSameSceneAndLocksPlacement()
            => StartRealStrategyAndRetry(DefenseAiStrategy.PolicyFlow, "PolicyFlowOption");

        [UnityTest] public IEnumerator CompleteDefenseLayout_StartsRealFpo() => StartRealStrategyAndRetry(DefenseAiStrategy.Fpo, "FpoOption");
        private IEnumerator StartRealStrategyAndRetry(DefenseAiStrategy strategy, string optionProperty)
        {
            ClearLaunchState();
            System.Type editorScenes = System.Type.GetType(
                "UnityEditor.SceneManagement.EditorSceneManager, UnityEditor");
            MethodInfo loadMethod = editorScenes?.GetMethod(
                "LoadSceneInPlayMode",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(LoadSceneParameters) },
                null);
            loadMethod.Invoke(null, new object[]
            {
                "Assets/LlamAcademy/Dinos/Scenes/FirstPersonDefenseBattlefield.unity",
                new LoadSceneParameters(LoadSceneMode.Single)
            });
            for (int frame = 0; frame < 4; frame++) yield return null;

            Component placement = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefensePlacementController");
            Component coordinator = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefenseBattleCoordinator");
            Component round = RuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component enemy = RuntimeComponent("LlamAcademy.Dinos.Enemy.EnemyAIController");
            int seed = (int)RuntimeComponent("LlamAcademy.Dinos.RoundManagement.GameSessionRestartService")
                .GetType().GetProperty("CurrentStartSeed").GetValue(
                    RuntimeComponent("LlamAcademy.Dinos.RoundManagement.GameSessionRestartService"));
            DefenseLayoutSnapshot snapshot = new(
                GameMapId.FirstPersonDefenseBattlefield,
                seed,
                "defense_battlefield_v1",
                CompletePlacements());
            object[] restoreArgs = { snapshot, null };
            object[] wrongMapArgs = { new DefenseLayoutSnapshot(GameMapId.DefenseBattlefield, seed,
                "defense_battlefield_v1", CompletePlacements()), null };
            Assert.That((bool)placement.GetType().GetMethod("TryRestoreSnapshot").Invoke(placement, wrongMapArgs), Is.False);
            Assert.That((bool)placement.GetType().GetMethod("TryRestoreSnapshot").Invoke(placement, restoreArgs),
                Is.True, restoreArgs[1] as string);
            yield return null;

            Assert.That((int)round.GetType().GetProperty("AliveDefenderCount").GetValue(round), Is.EqualTo(11));
            IList wallData = (IList)enemy.GetType().GetField("WallDatas",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy);
            Assert.That(wallData.Count, Is.EqualTo(3));
            object ppoOption = coordinator.GetType().GetProperty(optionProperty).GetValue(coordinator);
            Assert.That((bool)ppoOption.GetType().GetProperty("IsAvailable").GetValue(ppoOption), Is.True);

            coordinator.GetType().GetMethod("SelectStrategy").Invoke(coordinator,
                new object[] { strategy });
            Assert.That((bool)coordinator.GetType().GetMethod("TryStartBattle").Invoke(coordinator, null), Is.True);
            yield return null;

            Assert.That(round.GetType().GetProperty("State").GetValue(round).ToString(), Is.EqualTo("Running"));
            Assert.That((bool)placement.GetType().GetProperty("IsLocked").GetValue(placement), Is.True);
            Assert.That(GameObject.Find("Defense Deployment Area"), Is.Null);

            Component player = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.FirstPersonDefender");
            Assert.That(player, Is.Not.Null);
            Assert.That((bool)player.GetType().GetProperty("IsControlling").GetValue(player), Is.True);
            Assert.That(Camera.main.name, Is.EqualTo("Third Person Camera"));
            Assert.That(player.GetComponent<CharacterController>().enabled, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
            Assert.That(Vector3.Distance(Camera.main.transform.position, player.transform.position), Is.GreaterThan(3f));
            Assert.That(player.GetComponentsInChildren<SkinnedMeshRenderer>().All(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly), Is.True);
            yield return new WaitForSecondsRealtime(0.4f);
            CaptureModelState(player);
            CaptureView(Camera.main, "map4-default-" + strategy.ToString().ToLowerInvariant() + ".png");
            if (strategy == DefenseAiStrategy.Ppo)
            {
                Camera view = Camera.main;
                Vector3 viewPosition = view.transform.position;
                Quaternion viewRotation = view.transform.rotation;
                view.transform.position = player.transform.position + new Vector3(0f, 2.1f, 4.8f);
                view.transform.LookAt(player.transform.position + Vector3.up * 1.5f);
                CaptureView(view, "map4-character-front.png");
                view.transform.SetPositionAndRotation(viewPosition, viewRotation);
                yield return CaptureGameView(view);
            }
            yield return ExerciseThirdPersonFeatures(player, round);
            CaptureView(Camera.main, "map4-" + strategy.ToString().ToLowerInvariant() + "-third-person.png");
            if (strategy == DefenseAiStrategy.Ppo)
            {
                yield return new WaitForSeconds(0.05f);
                Assert.That(Object.FindFirstObjectByType<UIDocument>().rootVisualElement.Q<Button>("skill-burst"), Is.Not.Null);
            }
            yield return ExerciseKeyboard(player);
            yield return ExerciseMovementFacing(player);
            yield return ExerciseStaff(player, round);
            player.GetType().GetMethod("ApplyLook").Invoke(player, new object[] { new Vector2(1000f, 10000f) });
            Assert.That((float)player.GetType().GetProperty("Pitch").GetValue(player), Is.InRange(-80f, 80f));
            Vector3 beforeMove = player.transform.position;
            for (int tick = 0; tick < 20; tick++)
                player.GetType().GetMethod("SimulateMovement").Invoke(player, new object[] { Vector2.right, false, false, 0.02f });
            Assert.That(Vector3.Distance(beforeMove, player.transform.position), Is.GreaterThan(0.2f));
            Vector3 finitePosition = player.transform.position;
            player.GetType().GetMethod("SimulateMovement").Invoke(player, new object[] { new Vector2(float.NaN, 0), false, false, 0.02f });
            Assert.That(player.transform.position, Is.EqualTo(finitePosition));
            GameObject collisionWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            collisionWall.name = "Player movement obstruction";
            collisionWall.layer = LayerMask.NameToLayer("Enemies");
            Vector3 movementOrigin = player.transform.position;
            Vector3 direction = CameraPlane() * Vector3.right;
            collisionWall.transform.position = movementOrigin + direction * 2f + Vector3.up * 1.3f;
            collisionWall.transform.rotation = CameraPlane();
            collisionWall.transform.localScale = new Vector3(1f, 3f, 4f);
            Physics.SyncTransforms();
            for (int tick = 0; tick < 40; tick++)
                player.GetType().GetMethod("SimulateMovement").Invoke(player, new object[] { Vector2.right, false, false, 0.02f });
            Assert.That(Vector3.Dot(player.transform.position - movementOrigin, direction), Is.LessThan(1.5f),
                "The player must not walk through defense-layer walls.");
            Assert.That(player.GetComponentInChildren<Animator>().GetFloat("Speed"), Is.LessThan(0.08f),
                "A blocked character must stop its gait instead of walking into the wall in place.");
            Object.Destroy(collisionWall);
            player.GetType().GetMethod("TakeDamage").Invoke(player, new object[] { -10 });
            Assert.That((int)player.GetType().GetProperty("Health").GetValue(player), Is.EqualTo(120));
            player.GetType().GetMethod("TakeDamage").Invoke(player, new object[] { 1000 });
            Assert.That((int)player.GetType().GetProperty("Health").GetValue(player), Is.Zero);
            Assert.That((bool)player.GetType().GetProperty("IsControlling").GetValue(player), Is.False);
            Assert.That(UnityEngine.Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(round.GetType().GetProperty("State").GetValue(round).ToString(), Is.EqualTo("Running"),
                "Player knockout must preserve the original village outcome rules.");
            coordinator.GetType().GetMethod("ForfeitBattle").Invoke(coordinator, null);
            yield return new WaitForSeconds(3f);
            Assert.That(coordinator.GetType().GetProperty("Phase").GetValue(coordinator).ToString(), Is.EqualTo("Result"));
            Assert.That((bool)coordinator.GetType().GetProperty("DefenderWon").GetValue(coordinator), Is.False,
                "Exiting during battle must be a defender defeat.");

            int oldPlacementId = placement.GetInstanceID();
            coordinator.GetType().GetMethod("RetrySameLayout").Invoke(coordinator, null);
            yield return new WaitForSecondsRealtime(0.3f);
            Component restoredPlacement = null;
            for (int frame = 0; frame < 120 && restoredPlacement == null; frame++)
            {
                yield return null;
                Component candidate = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefensePlacementController");
                if (candidate != null && candidate.GetInstanceID() != oldPlacementId &&
                    (bool)candidate.GetType().GetProperty("IsLocked").GetValue(candidate))
                {
                    restoredPlacement = candidate;
                }
            }

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("FirstPersonDefenseBattlefield"));
            Assert.That(restoredPlacement, Is.Not.Null, "Retry must restore and lock the exact defense snapshot.");
            object restoredInventory = restoredPlacement.GetType().GetProperty("Inventory").GetValue(restoredPlacement);
            Assert.That((bool)restoredInventory.GetType().GetProperty("IsComplete").GetValue(restoredInventory), Is.True);
            Component restoredRound = RuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component restoredCoordinator = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefenseBattleCoordinator");
            Assert.That(restoredRound.GetType().GetProperty("State").GetValue(restoredRound).ToString(), Is.EqualTo("Setup"));
            Assert.That(restoredCoordinator.GetType().GetProperty("SelectedStrategy").GetValue(restoredCoordinator).ToString(),
                Is.EqualTo("None"));
            Component restoredPlayer = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.FirstPersonDefender");
            Assert.That((int)restoredPlayer.GetType().GetProperty("Health").GetValue(restoredPlayer), Is.EqualTo(120));
            Assert.That((bool)restoredPlayer.GetType().GetProperty("IsControlling").GetValue(restoredPlayer), Is.False);
            Assert.That(restoredPlayer.GetComponent<CharacterController>().enabled, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
            Component restoredAbilities = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefenderAbilities");
            Assert.That((float)restoredAbilities.GetType().GetProperty("BurstCooldownRemaining").GetValue(restoredAbilities), Is.Zero);
            Assert.That((bool)restoredAbilities.GetType().GetMethod("TryBurst").Invoke(restoredAbilities, null), Is.False);
            if (strategy == DefenseAiStrategy.Ppo)
            {
                restoredCoordinator.GetType().GetMethod("SelectStrategy").Invoke(restoredCoordinator, new object[] { strategy });
                Assert.That((bool)restoredCoordinator.GetType().GetMethod("TryStartBattle").Invoke(restoredCoordinator, null), Is.True);
                yield return null;
                restoredCoordinator.GetType().GetMethod("ForfeitBattle").Invoke(restoredCoordinator, null);
                yield return new WaitForSeconds(3f);
                restoredCoordinator.GetType().GetMethod("CreateNewLayout").Invoke(restoredCoordinator, null);
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("FirstPersonDefenseBattlefield"));
                Component newPlacement = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefensePlacementController");
                Assert.That((bool)newPlacement.GetType().GetProperty("IsLocked").GetValue(newPlacement), Is.False);
                Assert.That((bool)newPlacement.GetType().GetProperty("IsComplete").GetValue(newPlacement), Is.False);
                Component restart = RuntimeComponent("LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                Assert.That((int)restart.GetType().GetProperty("CurrentStartSeed").GetValue(restart), Is.Not.EqualTo(seed));
                Component newCoordinator = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefenseBattleCoordinator");
                newCoordinator.GetType().GetMethod("ExitSetupToMapSelect").Invoke(newCoordinator, null);
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MapSelect"));
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                UnityEngine.UI.Button mapButton = Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
                    .Single(b => b.name == "First Person Defense Battlefield");
                mapButton.onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("FirstPersonDefenseBattlefield"));
            }
        }

        private static IEnumerator ExerciseMovementFacing(Component player)
        {
            player.GetType().GetMethod("SetLookCaptured").Invoke(player, new object[] { false });
            yield return new WaitForSeconds(0.5f);
            Vector3 start = player.transform.position;
            foreach (Vector2 input in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
            {
                Vector3 expected = CameraPlane() * new Vector3(input.x, 0, input.y);
                for (int frame = 0; frame < 20; frame++)
                    player.GetType().GetMethod("SimulateMovement").Invoke(player, new object[] { input, false, false, 0.02f });
                Assert.That(Vector3.Dot(player.transform.forward, expected), Is.GreaterThan(0.95f),
                    "Forward gait must turn toward the actual movement direction, including sideways and backwards input.");
            }
            player.GetComponent<CharacterController>().enabled = false;
            player.transform.position = start;
            player.GetComponent<CharacterController>().enabled = true;
            player.GetType().GetMethod("SetLookCaptured").Invoke(player, new object[] { true });
        }

        private static Quaternion CameraPlane()
        {
            Component follow = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.ThirdPersonFollowCamera");
            return (Quaternion)follow.GetType().GetProperty("PlanarRotation").GetValue(follow);
        }

        private static IEnumerable<DefensePlacementToken> CompletePlacements()
        {
            Vector3[] walls =
            {
                new(-20f, 0f, -24f), new(-5f, 0f, -24f), new(10f, 0f, -24f)
            };
            Vector3[] archers =
            {
                new(-20f, 0f, -30f), new(-14f, 0f, -38f), new(-5f, 0f, -38f),
                new(4f, 0f, -38f), new(15f, 0f, -30f)
            };
            Vector3[] mages =
            {
                new(-19f, 0f, -45f), new(10f, 0f, -45f), new(18f, 0f, -38f)
            };
            for (int index = 0; index < walls.Length; index++)
                yield return new DefensePlacementToken(DefensePlacementKind.Wall, index,
                    walls[index].x, walls[index].y, walls[index].z, 0f);
            for (int index = 0; index < archers.Length; index++)
                yield return new DefensePlacementToken(DefensePlacementKind.Archer, index,
                    archers[index].x, archers[index].y, archers[index].z, 0f);
            for (int index = 0; index < mages.Length; index++)
                yield return new DefensePlacementToken(DefensePlacementKind.Mage, index,
                    mages[index].x, mages[index].y, mages[index].z, 0f);
        }

        private static IEnumerator ExerciseThirdPersonFeatures(Component player, Component round)
        {
            Component speed = RuntimeComponent("LlamAcademy.Dinos.RoundManagement.GameSpeedControl");
            Assert.That(speed, Is.Not.Null);
            float fixedStep = Time.fixedDeltaTime;
            Assert.That((bool)speed.GetType().GetMethod("TrySetSpeed").Invoke(speed, new object[] { float.NaN }), Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
            Assert.That((bool)speed.GetType().GetMethod("TrySetSpeed").Invoke(speed, new object[] { 2f }), Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(2f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep));
            speed.GetType().GetMethod("TrySetSpeed").Invoke(speed, new object[] { 0.5f });
            Component abilities = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefenderAbilities");
            Assert.That(abilities, Is.Not.Null);
            Assert.That((bool)abilities.GetType().GetMethod("TryShield").Invoke(abilities, null), Is.True);
            yield return null;
            CaptureView(Camera.main, "map4-shield.png");
            player.GetType().GetMethod("TakeDamage").Invoke(player, new object[] { 40 });
            Assert.That((int)player.GetType().GetProperty("Health").GetValue(player), Is.EqualTo(120));
            Assert.That((int)abilities.GetType().GetProperty("ShieldRemaining").GetValue(abilities), Is.EqualTo(20));
            Assert.That((bool)abilities.GetType().GetMethod("TryShield").Invoke(abilities, null), Is.False);
            player.GetType().GetMethod("TakeDamage").Invoke(player, new object[] { 30 });
            Assert.That((int)player.GetType().GetProperty("Health").GetValue(player), Is.EqualTo(110));
            player.GetType().GetProperty("Health").SetValue(player, 120);
            Assert.That((bool)abilities.GetType().GetMethod("TryDash").Invoke(abilities, null), Is.True);
            Assert.That((bool)abilities.GetType().GetMethod("TryDash").Invoke(abilities, null), Is.False);
            yield return new WaitForSeconds(0.3f);
            Assert.That((float)abilities.GetType().GetProperty("DashCooldownRemaining").GetValue(abilities), Is.GreaterThan(0));
            Component follow = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.ThirdPersonFollowCamera");
            Assert.That(follow, Is.Not.Null);
            Camera camera = Camera.main;
            Vector3 pivot = player.transform.position + Vector3.up * 2f;
            Vector3 originalCamera = camera.transform.position;
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Camera obstruction regression";
            wall.transform.position = Vector3.Lerp(pivot, originalCamera, 0.5f);
            wall.transform.localScale = Vector3.one * 1.5f;
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(Vector3.Distance(pivot, camera.transform.position), Is.LessThan(Vector3.Distance(pivot, originalCamera) - 0.4f));
            Object.Destroy(wall);
            yield return new WaitForSecondsRealtime(0.4f);
        }

        private static IEnumerator ExerciseKeyboard(Component player)
        {
            InputSettings originalSettings = InputSystem.settings;
            InputSettings testSettings = Object.Instantiate(originalSettings);
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = testSettings;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.EnableDevice(keyboard);
            try
            {
                Vector3 before = player.transform.position;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                yield return new WaitForSeconds(0.1f);
                CaptureView(Camera.main, "map4-walk.png");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                Vector3 moved = player.transform.position - before;
                moved.y = 0;
                Assert.That(moved.magnitude, Is.GreaterThan(0.05f), "Keyboard input must move horizontally, independently of gravity.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return new WaitForSeconds(0.05f);
                Assert.That((bool)player.GetType().GetProperty("IsLookCaptured").GetValue(player), Is.False);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return new WaitForSeconds(0.05f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
                yield return new WaitForSeconds(0.05f);
                Assert.That((bool)player.GetType().GetProperty("IsLookCaptured").GetValue(player), Is.True);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F3));
                yield return new WaitForSecondsRealtime(0.06f);
                Assert.That(Time.timeScale, Is.EqualTo(2f));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1));
                yield return new WaitForSecondsRealtime(0.06f);
                Assert.That(Time.timeScale, Is.EqualTo(0.5f));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings = originalSettings;
                Object.Destroy(testSettings);
            }
        }

        private static IEnumerator ExerciseStaff(Component player, Component round)
        {
            Component observation = RuntimeComponent("LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder");
            var types = (System.Array)observation.GetType().GetProperty("ConfiguredDinoTypes").GetValue(observation);
            Component spawner = RuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 500);
            Vector3 spawn = player.transform.position + Vector3.right * 12f;
            object[] args = { types.GetValue(0), spawn, null };
            Assert.That((bool)spawner.GetType().GetMethod("SpawnAuthorizedDino", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(spawner, args), Is.True);
            Component dino = (Component)args[2];
            // A fixed target lets the test isolate ray obstruction and cooldown from AI movement.
            foreach (Behaviour behaviour in dino.GetComponents<Behaviour>())
                if (behaviour.GetType().Name.Contains("Graph") || behaviour.GetType().Name == "NavMeshAgent") behaviour.enabled = false;
            Camera camera = Camera.main;
            Collider body = dino.GetComponentsInChildren<Collider>().First(c => !c.isTrigger);
            camera.transform.LookAt(body.bounds.center);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Spell obstruction test";
            wall.transform.position = Vector3.Lerp(camera.transform.position, body.bounds.center, 0.5f);
            wall.transform.localScale = Vector3.one * 3f;
            Physics.SyncTransforms();
            int health = (int)dino.GetType().GetProperty("Health").GetValue(dino);
            Assert.That((bool)player.GetType().GetMethod("TryCast").Invoke(player, null), Is.True);
            Assert.That((int)dino.GetType().GetProperty("Health").GetValue(dino), Is.EqualTo(health), "A solid wall must absorb the spell.");
            Assert.That((bool)player.GetType().GetMethod("TryCast").Invoke(player, null), Is.False, "Cooldown prevents duplicate damage.");
            VisualElement damageLayer = Object.FindFirstObjectByType<UIDocument>().rootVisualElement.Q("defender-damage-numbers");
            Assert.That(damageLayer.Query<Label>(className: "damage-number").ToList(), Is.Empty,
                "Blocked shots and cooldown attempts must not display damage.");
            Object.Destroy(wall);
            yield return new WaitForSeconds(0.5f);
            camera.transform.LookAt(body.bounds.center);
            Physics.SyncTransforms();
            // Ignore unrelated damage dealt by the existing guards during the cooldown.
            health = (int)dino.GetType().GetProperty("Health").GetValue(dino);
            Assert.That(health, Is.GreaterThan(0));
            Assert.That((bool)player.GetType().GetMethod("TryCast").Invoke(player, null), Is.True);
            Assert.That((int)dino.GetType().GetProperty("Health").GetValue(dino), Is.EqualTo(Mathf.Max(0, health - 25)),
                "An unobstructed spell must damage a registered real dinosaur.");
            Assert.That(damageLayer.Query<Label>(className: "staff-damage-number").ToList().Select(l => l.text),
                Is.EquivalentTo(new[] { "-" + Mathf.Min(25, health) }));
            Component abilities = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefenderAbilities");
            camera.transform.LookAt(body.bounds.center);
            int beforeBurst = (int)dino.GetType().GetProperty("Health").GetValue(dino);
            Assert.That(beforeBurst, Is.GreaterThan(0));
            Assert.That((bool)abilities.GetType().GetMethod("TryBurst").Invoke(abilities, null), Is.True);
            Assert.That((int)dino.GetType().GetProperty("Health").GetValue(dino), Is.EqualTo(Mathf.Max(0, beforeBurst - 55)));
            Assert.That((bool)abilities.GetType().GetMethod("TryBurst").Invoke(abilities, null), Is.False);
            Assert.That(damageLayer.Query<Label>(className: "skill-damage-number").ToList().Select(l => l.text),
                Does.Contain("-" + Mathf.Min(55, beforeBurst)));
            // Restore this registered target for the independent sensor/attack check.
            object[] sensorArgs = { types.GetValue(0), player.transform.position + Vector3.right * 12f, null };
            Assert.That((bool)spawner.GetType().GetMethod("SpawnAuthorizedDino", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(spawner, sensorArgs), Is.True);
            dino = (Component)sensorArgs[2];
            foreach (Behaviour behaviour in dino.GetComponents<Behaviour>())
                if (behaviour.GetType().Name.Contains("Graph") || behaviour.GetType().Name == "NavMeshAgent") behaviour.enabled = false;
            dino.transform.position = player.transform.position + Vector3.forward * 2f;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            object tracker = dino.GetType().GetField("TargetTracker", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dino);
            var targets = new List<GameObject>();
            tracker.GetType().GetMethod("CopyLiveTargets").Invoke(tracker, new object[] { targets });
            Assert.That(targets, Does.Contain(player.gameObject), "Dinosaur sensors must discover the additional player.");
            var unitType = dino.GetType().GetProperty("UnitType").GetValue(dino);
            var attackConfig = unitType.GetType().GetProperty("AttackConfig").GetValue(unitType);
            var attacks = (IEnumerable)attackConfig.GetType().GetProperty("AttackTypes").GetValue(attackConfig);
            object directAttack = attacks.Cast<object>().First(a => a.GetType().Name == "DamagingAttackSO");
            int playerHealth = (int)player.GetType().GetProperty("Health").GetValue(player);
            directAttack.GetType().GetMethod("ApplyEffect").Invoke(directAttack, new object[] { player });
            Assert.That((int)player.GetType().GetProperty("Health").GetValue(player), Is.LessThan(playerHealth));
            player.GetType().GetProperty("Health").SetValue(player, 120);
            camera.transform.localRotation = Quaternion.identity;
        }

        private static IEnumerator CaptureGameView(Camera camera)
        {
            UIDocument document = Object.FindFirstObjectByType<UIDocument>();
            PanelSettings original = document.panelSettings;
            PanelSettings panel = Object.Instantiate(original);
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                panel.targetTexture = target;
                panel.clearColor = false;
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.scale = 1f;
                document.panelSettings = panel;
                camera.targetTexture = target;
                for (int i = 0; i < 5; i++) yield return null;
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                    "../../reports/phase8/map4-damage-numbers-20260911/map4-gameview.png")), pixels.EncodeToPNG());
                RenderTexture.active = previous;
                Object.Destroy(pixels);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                document.panelSettings = original;
                Object.Destroy(panel);
                Object.Destroy(target);
            }
            for (int i = 0; i < 3; i++) yield return null;
        }

        private static void CaptureModelState(Component player)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"Player {player.transform.position} scale {player.transform.lossyScale}; Camera {Camera.main.transform.position}");
            foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("Shino") || t.name.Contains("Root") || t.name.Contains("Hips") || t.name.Contains("Head") || t.name.Contains("Staff"))
                    text.AppendLine($"{t.name}: position {t.position}, local {t.localPosition}, scale {t.lossyScale}, local scale {t.localScale}");
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
            {
                text.AppendLine($"{r.name}: bounds {r.bounds}, enabled {r.enabled}, shadows {r.shadowCastingMode}");
                if (r is SkinnedMeshRenderer skin)
                {
                    var mesh = new Mesh(); skin.BakeMesh(mesh);
                    text.AppendLine($"Baked {mesh.bounds}, vertex {skin.transform.TransformPoint(mesh.vertices[0])}");
                    Object.Destroy(mesh);
                }
                foreach (Material m in r.sharedMaterials) text.AppendLine($"{m.name}: {m.shader.name}, texture {m.mainTexture?.name}");
            }
            Animator animator = player.GetComponentInChildren<Animator>();
            float feet = Mathf.Min(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,
                animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y);
            text.AppendLine($"Lowest ankle {feet}; humanScale {animator.humanScale}");
            System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../reports/phase8/map4-damage-numbers-20260911/model-state.txt")), text.ToString());
            Assert.That(feet, Is.InRange(player.transform.position.y - 0.1f, player.transform.position.y + 0.5f), "Idle character feet must be at ground level.");
            var bodyRenderer = player.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Body");
            Assert.That(bodyRenderer.bounds.min.y, Is.InRange(player.transform.position.y - 0.15f, player.transform.position.y + 0.6f),
                "The rendered body, not just the rig, must stay above the floor.");
            Renderer staff = player.GetComponentsInChildren<Renderer>().Single(r => r.name == "Wooden shaft");
            Assert.That(staff.bounds.size.magnitude, Is.LessThan(3f), "FBX hand units must not scale the Unity staff by 100.");
        }

        private static void CaptureView(Camera camera, string name)
        {
            var previous = RenderTexture.active;
            var target = camera.targetTexture;
            var texture = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                    "../../reports/phase8/map4-damage-numbers-20260911", name));
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = target;
                RenderTexture.active = previous;
                Object.Destroy(texture);
                Object.Destroy(image);
            }
        }

        private static Component RuntimeComponent(string fullName)
        {
            System.Type type = System.Type.GetType($"{fullName}, Assembly-CSharp", true);
            return Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SingleOrDefault(value => value != null && value.GetType() == type);
        }

        private static void ClearLaunchState()
        {
            GameLaunchContext.Clear();
            System.Type replay = System.Type.GetType(
                "LlamAcademy.Dinos.DefenseBattle.DefenseBattleReplayContext, Assembly-CSharp");
            replay?.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }

        private static int Count(Component component, string property)
        {
            object value = component.GetType().GetProperty(property).GetValue(component);
            return (int)value.GetType().GetProperty("Count").GetValue(value);
        }
    }
}

