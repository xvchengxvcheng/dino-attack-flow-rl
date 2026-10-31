using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class DefenseBattlefieldSceneTests
    {
        private const string MapTwo = "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity";
        private const string MapThree = "Assets/LlamAcademy/Dinos/Scenes/DefenseBattlefield.unity";
        private const string MapSelect = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";
        // Approved Star Night presentation baseline; defense installers must still preserve Map 2.
        private const string FrozenMapTwoHash = "74DE3F69AC52585CA5E99F5D252C5EFC2F867CF8249C0EE4F3E4DAD9CEE8CDDA";

        [Test]
        public void DefenseScene_PreservesMapTwoAndStoresPlayerDefenseConfiguration()
        {
            Assert.That(Hash(MapTwo), Is.EqualTo(FrozenMapTwoHash));
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(MapThree), Is.Not.Null);
            Scene scene = EditorSceneManager.OpenScene(MapThree, OpenSceneMode.Single);

            Component placement = ExactlyOne(scene,
                "LlamAcademy.Dinos.DefenseBattle.DefensePlacementController");
            ExactlyOne(scene, "LlamAcademy.Dinos.DefenseBattle.DefenseBattleCoordinator");
            ExactlyOne(scene, "LlamAcademy.Dinos.DefenseBattle.DefenseBattleUI");
            Component enemy = ExactlyOne(scene, "LlamAcademy.Dinos.Enemy.EnemyAIController");
            SerializedObject placementData = new(placement);
            foreach (string field in new[]
                     {
                         "PlacementCamera", "WallPrefab", "ArcherPrefab", "MagePrefab", "SpawnRoot"
                     })
            {
                Assert.That(placementData.FindProperty(field)?.objectReferenceValue, Is.Not.Null, field);
            }
            Assert.That(placementData.FindProperty("DeploymentZones").arraySize, Is.EqualTo(15));
            Assert.That(placementData.FindProperty("DeploymentMin").vector2Value, Is.EqualTo(new Vector2(-24f, -49f)));
            Assert.That(placementData.FindProperty("DeploymentMax").vector2Value, Is.EqualTo(new Vector2(20f, -11f)));
            Assert.That(new SerializedObject(enemy).FindProperty("UsePlayerDefensePlacement").boolValue, Is.True);
            Assert.That(All(scene).Any(value => value.GetType().FullName == "LlamAcademy.Dinos.UI.RuntimeUI"), Is.False);
            Assert.That(scene.isDirty, Is.False);
        }

        [Test]
        public void MapSelect_HasDirectDefenseBattlefieldLaunchAndBuildSettingsEntry()
        {
            Scene scene = EditorSceneManager.OpenScene(MapSelect, OpenSceneMode.Single);
            Component controller = ExactlyOne(scene, "LlamAcademy.Dinos.UI.MapSelectUI");
            Button button = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(value => value.gameObject.scene == scene && value.name == "Defense Battlefield");
            Assert.That(new SerializedObject(controller).FindProperty("DefenseBattlefieldButton").objectReferenceValue,
                Is.SameAs(button));
            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo("LaunchDefenseBattlefield"));
            Assert.That(EditorBuildSettings.scenes.Any(value => value.enabled && value.path == MapThree), Is.True);
            Assert.That(scene.isDirty, Is.False);
        }

        [Test]
        public void PlayerBuild_StartsAtMapSelect()
        {
            EditorBuildSettingsScene firstEnabledScene = EditorBuildSettings.scenes
                .First(value => value.enabled);

            Assert.That(firstEnabledScene.path, Is.EqualTo(MapSelect));
        }

        private static Component ExactlyOne(Scene scene, string typeName) =>
            All(scene).Single(value => value.GetType().FullName == typeName);

        private static Component[] All(Scene scene) => Object.FindObjectsByType<Component>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(value => value != null && value.gameObject.scene == scene).ToArray();

        private static string Hash(string path)
        {
            using SHA256 hash = SHA256.Create();
            return System.BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty);
        }
    }
}
