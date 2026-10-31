using System.Linq;
using LlamAcademy.Dinos.DefenseBattle;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class FirstPersonDefenseSceneTests
    {
        private const string ScenePath = "Assets/LlamAcademy/Dinos/Scenes/FirstPersonDefenseBattlefield.unity";

        [Test]
        public void FourthMap_HasDistinctPlayableIdentityAndDefenseSnapshots()
        {
            Assert.That(GameMapId.TryFromSceneName("FirstPersonDefenseBattlefield", out GameMapId map), Is.True);
            Assert.That(map.IsPlayable, Is.True);
            Assert.That(map, Is.Not.EqualTo(GameMapId.DefenseBattlefield));
            var tokens = new[] { DefensePlacementKind.Wall, DefensePlacementKind.Archer, DefensePlacementKind.Mage }
                .SelectMany(kind => Enumerable.Range(0, kind == DefensePlacementKind.Archer ? 5 : 3)
                    .Select(i => new DefensePlacementToken(kind, i, i, 0, -20, 0)));
            var snapshot = new DefenseLayoutSnapshot(map, 12, "defense_battlefield_v1", tokens);
            Assert.That(snapshot.MapId, Is.EqualTo(map));
            Assert.That(snapshot.Placements.Count, Is.EqualTo(11));
        }

        [Test]
        public void FourthScene_HasOneConfiguredPlayerAndOriginalDefenseSystems()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var components = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)).ToArray();
            Assert.That(components.Any(c => c == null), Is.False, "Missing scripts");
            foreach (string type in new[] { "DefensePlacementController", "DefenseBattleCoordinator", "DefenseBattleUI", "FirstPersonDefender" })
                Assert.That(components.Count(c => c.GetType().Name == type), Is.EqualTo(1), type);
            Component player = components.Single(c => c.GetType().Name == "FirstPersonDefender");
            var serialized = new SerializedObject(player);
            foreach (string field in new[] { "CharacterModel", "FirstPersonCamera", "StrategyCamera", "Coordinator", "DamageType" })
                Assert.That(serialized.FindProperty(field)?.objectReferenceValue, Is.Not.Null, field);
            Assert.That(player.GetComponent<CharacterController>(), Is.Not.Null);
            GameObject model = (GameObject)serialized.FindProperty("CharacterModel").objectReferenceValue;
            Assert.That(model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.GreaterThan(0), "Map 4 needs a rigged anime character.");
            Animator animator = model.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman, Is.True);
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
            Assert.That(components.Count(c => c.GetType().Name == "ThirdPersonFollowCamera"), Is.EqualTo(1));
            Assert.That(components.Count(c => c.GetType().Name == "DefenderAbilities"), Is.EqualTo(1));
            Assert.That(scene.isDirty, Is.False);
        }

        [Test]
        public void MapSelect_OffersFourthMapAndBuildRetainsOriginalScenes()
        {
            EditorSceneManager.OpenScene("Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity", OpenSceneMode.Single);
            Button button = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SingleOrDefault(b => b.name == "First Person Defense Battlefield");
            Assert.That(button, Is.Not.Null);
            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo("LaunchFirstPersonDefenseBattlefield"));
            var names = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)).ToArray();
            Assert.That(names[0], Is.EqualTo("MapSelect"));
            foreach (string name in new[] { "Dinos", "LayeredBattlefield", "DefenseBattlefield", "FirstPersonDefenseBattlefield" })
                Assert.That(names, Does.Contain(name));
        }
    }
}
