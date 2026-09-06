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

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class DefenseBattlefieldPlayModeTests
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
                "Assets/LlamAcademy/Dinos/Scenes/DefenseBattlefield.unity",
                new LoadSceneParameters(LoadSceneMode.Single)
            });

            for (int frame = 0; frame < 4; frame++) yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("DefenseBattlefield"));
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
                "Assets/LlamAcademy/Dinos/Scenes/DefenseBattlefield.unity",
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
                GameMapId.DefenseBattlefield,
                seed,
                "defense_battlefield_v1",
                CompletePlacements());
            object[] restoreArgs = { snapshot, null };
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

            Assert.That(restoredPlacement, Is.Not.Null, "Retry must restore and lock the exact defense snapshot.");
            object restoredInventory = restoredPlacement.GetType().GetProperty("Inventory").GetValue(restoredPlacement);
            Assert.That((bool)restoredInventory.GetType().GetProperty("IsComplete").GetValue(restoredInventory), Is.True);
            Component restoredRound = RuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component restoredCoordinator = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefenseBattleCoordinator");
            Assert.That(restoredRound.GetType().GetProperty("State").GetValue(restoredRound).ToString(), Is.EqualTo("Setup"));
            Assert.That(restoredCoordinator.GetType().GetProperty("SelectedStrategy").GetValue(restoredCoordinator).ToString(),
                Is.EqualTo("None"));
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
