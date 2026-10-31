using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using LlamAcademy.Dinos.DefenseBattle;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class DefenseMinimapPlayModeTests
    {
        private const string Report = "../../reports/phase8/map4-damage-numbers-20260911/";

        [UnityTest]
        public IEnumerator MapTracksLiveBattleAndSurvivesRetryWithoutLeakingToMapThree()
        {
            GameLaunchContext.Clear();
            Type.GetType("LlamAcademy.Dinos.DefenseBattle.DefenseBattleReplayContext, Assembly-CSharp")
                .GetMethod("Clear").Invoke(null, null);
            Load("FirstPersonDefenseBattlefield");
            for (int i = 0; i < 6; i++) yield return null;
            UIDocument document = Object.FindFirstObjectByType<UIDocument>();
            VisualElement map = document.rootVisualElement.Q("defense-minimap");
            Assert.That(map, Is.Not.Null, "Map four must display a minimap.");
            Assert.That(map.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(map.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(map.Q("minimap-player"), Is.Not.Null);
            Assert.That(map.Q("minimap-player").resolvedStyle.display, Is.EqualTo(DisplayStyle.None),
                "The player has not spawned in setup; do not show its staging transform as a live unit.");
            Assert.That(map.Q("minimap-target"), Is.Not.Null);
            Assert.That(map.Query<Label>(className: "minimap-zone-label").ToList().Select(l => l.text),
                Is.EquivalentTo(new[] { "Z1", "Z2", "Z3", "Z4", "Z5" }));
            Assert.That(map.Query(className: "minimap-house").ToList().Count, Is.EqualTo(8));
            Component round = Find("RoundManagement.RoundManager");
            Component player = Find("DefenseBattle.FirstPersonDefender");
            Component placement = Find("DefenseBattle.DefensePlacementController");
            Component coordinator = Find("DefenseBattle.DefenseBattleCoordinator");
            Component restart = Find("RoundManagement.GameSessionRestartService");
            int seed = (int)Read(restart, "CurrentStartSeed");
            var placements = (System.Collections.Generic.IEnumerable<DefensePlacementToken>)
                typeof(FirstPersonDefensePlayModeTests).GetMethod("CompletePlacements", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, null);
            var snapshot = new DefenseLayoutSnapshot(GameMapId.FirstPersonDefenseBattlefield, seed, "defense_battlefield_v1", placements);
            object[] restore = { snapshot, null };
            Assert.That(Call(placement, "TryRestoreSnapshot", restore), Is.True, restore[1] as string);
            yield return null;
            Assert.That(map.Query(className: "minimap-wall").ToList().Count, Is.EqualTo(3));
            Assert.That(map.Query(className: "minimap-guard").ToList().Count, Is.EqualTo(11));
            yield return Capture(document, Camera.main, "setup-1280.png", 1280, 720);
            Call(coordinator, "SelectStrategy", DefenseAiStrategy.Ppo);
            Assert.That(Call(coordinator, "TryStartBattle"), Is.True);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
            Time.timeScale = 0f; // Presentation must continue even while simulation is paused.
            try
            {
                Component observation = Find("Training.DinoStructuredObservationBuilder");
                Array types = (Array)Read(observation, "ConfiguredDinoTypes");
                Component spawner = Find("Player.DinoSpawner");
                spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 1000);
                object[] spawn = { types.GetValue(0), player.transform.position + Vector3.right * 12f, null };
                Assert.That(spawner.GetType().GetMethod("SpawnAuthorizedDino", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(spawner, spawn), Is.True);
                Component dino = (Component)spawn[2];
                for (int i = 0; i < 4; i++) yield return null;
                VisualElement enemy = map.Query(className: "minimap-enemy").ToList().Single(m => Equals(m.userData, dino));
                Vector2 oldEnemy = enemy.layout.position;
                Vector2 oldPlayer = map.Q("minimap-player").layout.position;
                Vector3 oldPosition = player.transform.position;
                Quaternion oldRotation = player.transform.rotation;
                dino.transform.position += new Vector3(5, 0, 5);
                player.transform.position += new Vector3(4, 0, -4);
                player.transform.rotation = Quaternion.Euler(0, 90, 0);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(enemy.layout.x, Is.GreaterThan(oldEnemy.x));
                Assert.That(enemy.layout.y, Is.LessThan(oldEnemy.y), "North is screen-up.");
                Assert.That(map.Q("minimap-player").layout.x, Is.GreaterThan(oldPlayer.x));
                Assert.That(map.Q("minimap-player").layout.y, Is.GreaterThan(oldPlayer.y));
                Assert.That(map.Q("minimap-player").style.rotate.value.angle.value, Is.EqualTo(90).Within(0.1));
                player.transform.SetPositionAndRotation(oldPosition, oldRotation);
                yield return Capture(document, Camera.main, "battle-1280.png", 1280, 720);
                yield return Capture(document, Camera.main, "battle-1920.png", 1920, 1080);
                yield return Capture(document, Camera.main, "battle-1024.png", 1024, 768);
                Call(dino, "TakeDamage", 100000);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(map.Query(className: "minimap-enemy").ToList().Any(m => Equals(m.userData, dino)), Is.False,
                    "Dead enemies must leave the map before their corpse is destroyed.");
                Call(player, "TakeDamage", 100000);
                yield return null;
                Assert.That(map.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Keep the map during spectating.");
                Assert.That(map.Q("minimap-player").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Behaviour hud = (Behaviour)Find("DefenseBattle.FirstPersonDefenseHud");
                hud.enabled = false;
                yield return null;
                Assert.That(document.rootVisualElement.Q("defense-minimap"), Is.Null);
                hud.enabled = true;
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(document.rootVisualElement.Query("defense-minimap").ToList().Count, Is.EqualTo(1));
            }
            finally { Time.timeScale = 0.5f; }
            Call(coordinator, "ForfeitBattle");
            yield return new WaitForSeconds(3f);
            Assert.That(document.rootVisualElement.Q("defense-minimap").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Call(coordinator, "RetrySameLayout");
            yield return new WaitForSecondsRealtime(1f);
            document = Object.FindFirstObjectByType<UIDocument>();
            map = document.rootVisualElement.Q("defense-minimap");
            Assert.That(map, Is.Not.Null);
            Assert.That(map.Query(className: "minimap-enemy").ToList(), Is.Empty);
            Assert.That(map.Query(className: "minimap-wall").ToList().Count, Is.EqualTo(3));
            Load("DefenseBattlefield");
            for (int i = 0; i < 6; i++) yield return null;
            Assert.That(Object.FindFirstObjectByType<UIDocument>().rootVisualElement.Q("defense-minimap"), Is.Null);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        private static IEnumerator Capture(UIDocument document, Camera camera, string file, int width, int height)
        {
            PanelSettings original = document.panelSettings;
            PanelSettings panel = Object.Instantiate(original);
            RenderTexture target = new(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture oldTarget = camera.targetTexture;
            try
            {
                panel.targetTexture = target;
                panel.clearColor = false;
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.scale = 1;
                document.panelSettings = panel;
                camera.targetTexture = target;
                for (int i = 0; i < 8; i++) yield return null;
                var root = document.rootVisualElement;
                var map = root.Q("defense-minimap");
                Assert.That(map.worldBound.yMin, Is.GreaterThanOrEqualTo(root.Q("game-speed-settings").worldBound.yMax + 5));
                Assert.That(map.worldBound.xMax, Is.LessThanOrEqualTo(width));
                Assert.That(map.worldBound.yMax, Is.LessThan(height - 200));
                Assert.That(map.worldBound.Contains(root.Q("first-person-reticle").worldBound.center), Is.False);
                RenderTexture oldActive = RenderTexture.active;
                Texture2D pixels = new(width, height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    pixels.Apply();
                    string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, Report, file));
                    System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                }
                finally { RenderTexture.active = oldActive; Object.Destroy(pixels); }
            }
            finally
            {
                camera.targetTexture = oldTarget;
                document.panelSettings = original;
                Object.Destroy(panel);
                Object.Destroy(target);
            }
            for (int i = 0; i < 3; i++) yield return null;
        }

        private static void Load(string scene) => Type.GetType("UnityEditor.SceneManagement.EditorSceneManager, UnityEditor")
            .GetMethod("LoadSceneInPlayMode", new[] { typeof(string), typeof(LoadSceneParameters) })
            .Invoke(null, new object[] { "Assets/LlamAcademy/Dinos/Scenes/" + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single) });
        private static Component Find(string type) => Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(c => c != null && c.GetType().FullName == "LlamAcademy.Dinos." + type);
        private static object Read(Component c, string property) => c.GetType().GetProperty(property).GetValue(c);
        private static object Call(Component c, string method, params object[] args) => c.GetType().GetMethod(method).Invoke(c, args);
    }
}
