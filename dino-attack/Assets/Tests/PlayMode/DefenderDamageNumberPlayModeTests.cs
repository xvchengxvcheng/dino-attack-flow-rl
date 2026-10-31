using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class DefenderDamageNumberPlayModeTests
    {
        private const string Report = "../../reports/phase8/map4-damage-numbers-20260911/";

        [UnityTest]
        public IEnumerator PlayerDamageShowsActualAmountsAndExpiresWithoutDependingOnTargetLifetime()
        {
            GameLaunchContext.Clear();
            Type.GetType("LlamAcademy.Dinos.DefenseBattle.DefenseBattleReplayContext, Assembly-CSharp")
                .GetMethod("Clear").Invoke(null, null);
            Load("FirstPersonDefenseBattlefield");
            for (int i = 0; i < 6; i++) yield return null;
            UIDocument document = Object.FindFirstObjectByType<UIDocument>();
            Assert.That(document.rootVisualElement.Q("defender-damage-numbers"), Is.Not.Null,
                "The player's HUD must own a damage-number layer.");
            Component player = Find("DefenseBattle.FirstPersonDefender");
            Component coordinator = Find("DefenseBattle.DefenseBattleCoordinator");
            Component placement = Find("DefenseBattle.DefensePlacementController");
            Component restart = Find("RoundManagement.GameSessionRestartService");
            int seed = (int)Read(restart, "CurrentStartSeed");
            var placements = (IEnumerable<DefensePlacementToken>)typeof(FirstPersonDefensePlayModeTests)
                .GetMethod("CompletePlacements", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var snapshot = new DefenseLayoutSnapshot(GameMapId.FirstPersonDefenseBattlefield, seed, "defense_battlefield_v1", placements);
            object[] restore = { snapshot, null };
            Assert.That(Call(placement, "TryRestoreSnapshot", restore), Is.True, restore[1] as string);
            Call(coordinator, "SelectStrategy", DefenseAiStrategy.Ppo);
            Assert.That(Call(coordinator, "TryStartBattle"), Is.True);
            for (int i = 0; i < 4; i++) yield return null;
            Camera camera = Camera.main;
            ((Behaviour)Find("DefenseBattle.ThirdPersonFollowCamera")).enabled = false;
            PanelSettings oldPanel = document.panelSettings;
            PanelSettings panel = Object.Instantiate(oldPanel);
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            RenderTexture oldTarget = camera.targetTexture;
            try
            {
                Time.timeScale = 0f;
                panel.targetTexture = target;
                panel.clearColor = false;
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.scale = 1;
                document.panelSettings = panel;
                camera.targetTexture = target;
                for (int i = 0; i < 5; i++) yield return null;
                var layer = document.rootVisualElement.Q("defender-damage-numbers");
                Assert.That(layer.pickingMode, Is.EqualTo(PickingMode.Ignore));
                Component enemy = Spawn(player.transform.position + Vector3.right * 12f);
                yield return null; // Let Unit.Start register its health bar before any lethal damage.
                Freeze(enemy);
                enemy.GetType().GetProperty("Health").SetValue(enemy, 1000);
                Collider body = enemy.GetComponentsInChildren<Collider>().First(c => !c.isTrigger);
                camera.transform.LookAt(body.bounds.center);
                Physics.SyncTransforms();
                Call(enemy, "TakeDamage", 3); // A guard/other source must not be reported as player damage.
                yield return null;
                Assert.That(Visible(layer), Is.Empty);
                Assert.That(Call(player, "TryCast"), Is.True);
                yield return null;
                Assert.That(Visible(layer).Select(l => l.text), Is.EquivalentTo(new[] { "-25" }));
                Label normal = Visible(layer).Single();
                Assert.That(normal.ClassListContains("staff-damage-number"), Is.True);
                float startY = normal.resolvedStyle.top;
                Assert.That(Call(player, "TryCast"), Is.False, "Cooldown must not create another number.");
                yield return new WaitForSecondsRealtime(0.16f);
                Assert.That(normal.resolvedStyle.top, Is.LessThan(startY), "Damage text should rise.");
                Save(target, "staff-25.png");
                Quaternion view = camera.transform.rotation;
                camera.transform.Rotate(0, 180, 0, Space.World);
                yield return null;
                Assert.That(Visible(layer), Is.Empty, "Do not mirror a target behind the camera onto the screen.");
                camera.transform.rotation = view;
                yield return new WaitForSecondsRealtime(1.3f);
                Assert.That(Visible(layer), Is.Empty, "Real-time expiry must work even when the simulation is paused.");

                // Advance only the original gameplay clock so the next normal attack is legal.
                Time.timeScale = 0.5f;
                yield return new WaitForSeconds(0.5f);
                Time.timeScale = 0f;
                enemy.GetType().GetProperty("Health").SetValue(enemy, 7);
                camera.transform.LookAt(body.bounds.center);
                Assert.That(Call(player, "TryCast"), Is.True);
                yield return null;
                Assert.That((int)Read(enemy, "Health"), Is.Zero);
                Assert.That(Visible(layer).Select(l => l.text), Is.EquivalentTo(new[] { "-7" }), "Overkill must show actual lost HP.");
                Object.Destroy(enemy.gameObject);
                yield return new WaitForSecondsRealtime(0.12f);
                Assert.That(Visible(layer).Single().text, Is.EqualTo("-7"), "A killing blow must survive target destruction.");
                Save(target, "killing-blow-7.png");
                yield return new WaitForSecondsRealtime(1.3f);

                // Keep the two targets away from the restored mage at (10, -45), which blocks skill line of sight.
                Vector3 center = player.transform.position + Vector3.right * 12f + Vector3.forward * 6f;
                Component first = Spawn(center + Vector3.forward * 3f);
                Component second = Spawn(center + Vector3.back * 3f);
                yield return null;
                Freeze(first);
                Freeze(second);
                first.GetType().GetProperty("Health").SetValue(first, 1000);
                second.GetType().GetProperty("Health").SetValue(second, 9);
                // Two solid colliders on one dinosaur must still produce a single area hit/number.
                BoxCollider extra = first.gameObject.AddComponent<BoxCollider>();
                extra.center = Vector3.up; extra.size = Vector3.one;
                camera.transform.LookAt(center + Vector3.up * 0.05f);
                Physics.SyncTransforms();
                object[] aim = { 30f, null };
                Assert.That(Call(player, "TryGetAimPoint", aim), Is.True);
                foreach (Component dino in new[] { first, second })
                {
                    Collider collider = dino.GetComponentsInChildren<Collider>().First(c => !c.isTrigger);
                    Assert.That(Call(player, "HasLineOfSight", aim[1], collider), Is.True,
                        "The multi-target fixture must have clear skill line of sight to both targets.");
                }
                Component abilities = Find("DefenseBattle.DefenderAbilities");
                Assert.That(Call(abilities, "TryBurst"), Is.True);
                yield return null;
                Assert.That((int)Read(first, "Health"), Is.EqualTo(945));
                Assert.That((int)Read(second, "Health"), Is.Zero);
                Assert.That(Visible(layer).Select(l => l.text), Is.EquivalentTo(new[] { "-55", "-9" }));
                Assert.That(Visible(layer).All(l => l.ClassListContains("skill-damage-number")), Is.True);
                Save(target, "burst-multiple.png");
                Assert.That(Call(abilities, "TryBurst"), Is.False);
                Assert.That(Visible(layer).Count, Is.EqualTo(2));
                Behaviour hud = (Behaviour)Find("DefenseBattle.FirstPersonDefenseHud");
                hud.enabled = false;
                yield return null;
                Assert.That(document.rootVisualElement.Q("defender-damage-numbers"), Is.Null);
                hud.enabled = true;
                for (int i = 0; i < 3; i++) yield return null;
                layer = document.rootVisualElement.Q("defender-damage-numbers");
                Assert.That(Visible(layer), Is.Empty);
                Assert.That(document.rootVisualElement.Query("defender-damage-numbers").ToList().Count, Is.EqualTo(1));
                Time.timeScale = 0.5f;
                yield return new WaitForSeconds(0.5f);
                Time.timeScale = 0f;
                Collider firstBody = first.GetComponentsInChildren<Collider>().First(c => !c.isTrigger);
                // Isolate listener lifetime in open ground; core_east_t4 blocks the previous staff ray.
                first.transform.position = new Vector3(30f, first.transform.position.y, -60f);
                CharacterController motor = player.GetComponent<CharacterController>();
                motor.enabled = false;
                player.transform.position = first.transform.position + Vector3.back * 3f;
                motor.enabled = true;
                camera.transform.position = player.transform.position + Vector3.up * 1.8f;
                Physics.SyncTransforms();
                camera.transform.LookAt(firstBody.bounds.center);
                int beforeReenabledHit = (int)Read(first, "Health");
                Assert.That((bool)Read(first, "IsDead"), Is.False);
                Assert.That(((IEnumerable)Read(Find("RoundManagement.RoundManager"), "ActiveDinoUnits")).Cast<object>(), Does.Contain(first));
                object[] reenabledAim = { 55f, null };
                Assert.That(Call(player, "TryGetAimPoint", reenabledAim), Is.True);
                Vector3 shotOrigin = player.transform.position + Vector3.up * 1.8f;
                Vector3 shotDelta = (Vector3)reenabledAim[1] - shotOrigin;
                object[] shot = { shotOrigin, shotDelta.normalized, shotDelta.magnitude + 0.15f, null };
                Assert.That(player.GetType().GetMethod("FindObstruction", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, shot), Is.True);
                RaycastHit shotHit = (RaycastHit)shot[3];
                Assert.That(shotHit.collider != null && shotHit.collider.transform.IsChildOf(first.transform), Is.True,
                    $"Lifecycle shot hit {shotHit.collider?.name}, origin {shotOrigin}, aim {reenabledAim[1]}, target {firstBody.bounds}.");
                Assert.That(Call(player, "TryCast"), Is.True);
                Assert.That((int)Read(first, "Health"), Is.EqualTo(beforeReenabledHit - 25),
                    "The listener-lifecycle fixture must hit its registered target.");
                yield return null;
                Assert.That(Visible(layer).Count, Is.EqualTo(1), "Re-enabling the HUD must not duplicate its listener.");
                Call(coordinator, "ForfeitBattle");
                yield return null;
                Assert.That(Visible(layer), Is.Empty, "Clear labels before the result overlay.");
            }
            finally
            {
                Time.timeScale = 0.5f;
                if (camera != null) camera.targetTexture = oldTarget;
                if (document != null) document.panelSettings = oldPanel;
                Object.Destroy(panel); Object.Destroy(target);
            }
            yield return new WaitForSeconds(3f);
            Call(coordinator, "RetrySameLayout");
            yield return new WaitForSecondsRealtime(1f);
            Assert.That(Visible(Object.FindFirstObjectByType<UIDocument>().rootVisualElement.Q("defender-damage-numbers")), Is.Empty);
            Load("DefenseBattlefield");
            for (int i = 0; i < 6; i++) yield return null;
            Assert.That(Object.FindFirstObjectByType<UIDocument>().rootVisualElement.Q("defender-damage-numbers"), Is.Null);
        }

        private static Component Spawn(Vector3 position)
        {
            Component spawner = Find("Player.DinoSpawner");
            spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 1000);
            Array types = (Array)Read(Find("Training.DinoStructuredObservationBuilder"), "ConfiguredDinoTypes");
            object[] args = { types.GetValue(0), position, null };
            Assert.That(spawner.GetType().GetMethod("SpawnAuthorizedDino", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(spawner, args), Is.True);
            return (Component)args[2];
        }

        private static void Freeze(Component dino)
        {
            // Start enables the graph and navigation, so freeze only after initialization.
            foreach (Behaviour b in dino.GetComponents<Behaviour>())
                if (b.GetType().Name.Contains("Graph") || b.GetType().Name == "NavMeshAgent") b.enabled = false;
        }

        private static List<Label> Visible(VisualElement layer) => layer.Query<Label>(className: "damage-number").ToList()
            .Where(l => l.style.display.value != DisplayStyle.None).ToList();

        private static void Save(RenderTexture target, string name)
        {
            RenderTexture old = RenderTexture.active;
            Texture2D image = new(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, Report, name)), image.EncodeToPNG());
            }
            finally { RenderTexture.active = old; Object.Destroy(image); }
        }

        private static void Load(string name) => Type.GetType("UnityEditor.SceneManagement.EditorSceneManager, UnityEditor")
            .GetMethod("LoadSceneInPlayMode", new[] { typeof(string), typeof(LoadSceneParameters) })
            .Invoke(null, new object[] { "Assets/LlamAcademy/Dinos/Scenes/" + name + ".unity", new LoadSceneParameters(LoadSceneMode.Single) });
        private static Component Find(string name) => Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(c => c != null && c.GetType().FullName == "LlamAcademy.Dinos." + name);
        private static object Read(Component c, string name) => c.GetType().GetProperty(name).GetValue(c);
        private static object Call(Component c, string name, params object[] args) => c.GetType().GetMethod(name).Invoke(c, args);
    }
}
