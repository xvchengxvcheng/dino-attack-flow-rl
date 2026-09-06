using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class LayeredBattlefieldSceneTests
    {
        private const string Source = "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity";
        private const string Backup = "Assets/LlamAcademy/Dinos/Scenes/Backups/Dinos.pre-map2-task4-20260831.unity";
        private const string Layered = "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity";
        private const string Select = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";
        private const string ProtectedBackupHash = "1815D6F7CA861B7F9428FF3CA081E9EC24B52BBB0B11BFEB8C001A256C21F735";

        [Test]
        public void Task4ScenesValidateWithoutChangingProtectedSource()
        {
            string before = Hash(Source);
            Assert.That(Hash(Backup), Is.EqualTo(ProtectedBackupHash));
            Assert.That(AssetDatabase.AssetPathToGUID(Layered), Is.Not.Empty);
            Assert.That(AssetDatabase.AssetPathToGUID(Select), Is.Not.Empty);
            Assert.That(AssetDatabase.AssetPathToGUID(Layered), Is.Not.EqualTo(AssetDatabase.AssetPathToGUID(Source)));

            Assert.That(() => InvokeStatic(
                "LlamAcademy.Dinos.LayeredBattlefield.Editor.LayeredBattlefieldValidator",
                "ValidateAssets"), Throws.Nothing);
            Assert.That(Hash(Source), Is.EqualTo(before));
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False);
        }

        [Test]
        public void LayeredSceneStoresCompleteFiniteCandidateBindings()
        {
            Scene scene = EditorSceneManager.OpenScene(Layered, OpenSceneMode.Single);
            Component layout = Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SingleOrDefault(value => value != null && value.GetType().FullName ==
                    "LlamAcademy.Dinos.Map.Adapters.LayeredBattlefieldLayoutController");
            Assert.That(layout, Is.Not.Null);
            SerializedObject serialized = new(layout);
            Assert.That(serialized.FindProperty("ZoneBindings").arraySize, Is.EqualTo(15));
            Assert.That(serialized.FindProperty("HouseBindings").arraySize, Is.EqualTo(14));
            Assert.That(serialized.FindProperty("WallBindings").arraySize, Is.EqualTo(6));
            Assert.That(serialized.FindProperty("GroundGuardBindings").arraySize, Is.EqualTo(14));
            Assert.That(serialized.FindProperty("TargetBindings").arraySize, Is.EqualTo(3));
            Assert.That(scene.isDirty, Is.False);
            Assert.That(Hash(Backup), Is.EqualTo(ProtectedBackupHash));
        }

        [Test]
        public void MapSelectUsesInputSystemAndEachMapButtonLaunchesDirectly()
        {
            Scene scene = EditorSceneManager.OpenScene(Select, OpenSceneMode.Single);
            Component controller = Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SingleOrDefault(value => value != null && value.GetType().FullName ==
                    "LlamAcademy.Dinos.UI.MapSelectUI");
            Assert.That(controller, Is.Not.Null, "MapSelect requires its basic Human selection controller.");
            SerializedObject controllerData = new(controller);
            foreach (string field in new[]
                     {
                         "OpenTropicalButton", "LayeredBattlefieldButton", "DefenseBattlefieldButton", "DinosaurIntroductionButton",
                         "DinosaurIntroductionPanel", "CloseIntroductionButton", "RestartService", "StatusText"
                     })
                Assert.That(controllerData.FindProperty(field).objectReferenceValue, Is.Not.Null,
                    $"MapSelect controller field {field} must be wired.");
            Assert.That(controllerData.FindProperty("StartButton"), Is.Null,
                "Selecting a map launches it directly; MapSelect must not retain a redundant Start field.");

            Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(buttons.Select(value => value.name), Is.EquivalentTo(new[]
            {
                "Open Tropical Battlefield", "Layered Battlefield", "Dinosaur Introduction",
                "Defense Battlefield",
                "Close Dinosaur Introduction"
            }));
            foreach ((string name, string method) in new[]
                     {
                         ("Open Tropical Battlefield", "LaunchOpenTropicalBattlefield"),
                         ("Layered Battlefield", "LaunchLayeredBattlefield"),
                         ("Defense Battlefield", "LaunchDefenseBattlefield")
                     })
            {
                Button button = buttons.Single(value => value.name == name);
                Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1),
                    $"{name} must store exactly one persistent scene listener.");
                Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo(method),
                    $"{name} must launch its map without a second Start click.");
            }

            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            Assert.That(eventSystem, Is.Not.Null);
            Component inputModule = eventSystem.GetComponents<Component>().SingleOrDefault(component => component != null &&
                component.GetType().FullName == "UnityEngine.InputSystem.UI.InputSystemUIInputModule");
            Assert.That(inputModule, Is.Not.Null,
                "The project uses the new Input System, so MapSelect must use InputSystemUIInputModule.");
            SerializedObject inputData = new(inputModule);
            foreach (string field in new[]
                     {
                         "m_ActionsAsset", "m_PointAction", "m_LeftClickAction", "m_SubmitAction", "m_CancelAction"
                     })
                Assert.That(inputData.FindProperty(field)?.objectReferenceValue, Is.Not.Null,
                    $"MapSelect UI input action {field} must be assigned so pointer clicks reach the buttons.");
            Assert.That(eventSystem.GetComponent<StandaloneInputModule>(), Is.Null,
                "StandaloneInputModule throws at runtime when legacy input is disabled.");

            Image logo = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SingleOrDefault(value => value.name == "Dino Attack Logo");
            Assert.That(logo, Is.Not.Null, "The Dino Attack logo belongs on MapSelect.");
            Assert.That(logo.sprite, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/LlamAcademy/Dinos/UI/Textures/Dinos Logo.png")));
            Assert.That(logo.raycastTarget, Is.False);

            System.Type type = controller.GetType();
            MethodInfo buildRequest = type.GetMethod(
                "BuildLaunchRequest",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(GameMapId) },
                null);
            Assert.That(buildRequest, Is.Not.Null);
            object request = buildRequest.Invoke(controller, new object[] { GameMapId.LayeredBattlefield });
            object secondRequest = buildRequest.Invoke(controller, new object[] { GameMapId.LayeredBattlefield });
            System.Type requestType = request.GetType();
            Assert.That(requestType.GetProperty("MapId").GetValue(request).ToString(), Is.EqualTo("layered_battlefield"));
            Assert.That(requestType.GetProperty("Controller").GetValue(request).ToString(), Is.EqualTo("Human"));
            int firstSeed = (int)requestType.GetProperty("LayoutSeed").GetValue(request);
            int secondSeed = (int)requestType.GetProperty("LayoutSeed").GetValue(secondRequest);
            Assert.That(firstSeed, Is.Not.EqualTo(int.MinValue));
            Assert.That(secondSeed, Is.Not.EqualTo(int.MinValue));
            Assert.That(secondSeed, Is.Not.EqualTo(firstSeed),
                "Every new Human launch request must consume a fresh process-monotonic layout seed.");
            Assert.That((int)requestType.GetProperty("InferenceSeed").GetValue(request), Is.Zero);
            Assert.That((string)requestType.GetProperty("ModelId").GetValue(request), Is.Empty);
            Assert.That((string)requestType.GetProperty("ProtocolVersion").GetValue(request),
                Is.EqualTo("dino_attack_structured_set_v2"));
            scene = EditorSceneManager.OpenScene(Select, OpenSceneMode.Single);
            Assert.That(scene.isDirty, Is.False);
            Assert.That(Hash(Backup), Is.EqualTo(ProtectedBackupHash));
        }

        [Test]
        public void BothMapsUseVisibleBaseGroundAndContainNoRiverbedLayer()
        {
            Scene mapOne = EditorSceneManager.OpenScene(Source, OpenSceneMode.Single);
            Transform mapOneTerrain = GameObject.Find("World/Open Tropical Battlefield/Terrain Surfaces")?.transform;
            Assert.That(mapOneTerrain, Is.Not.Null);
            Assert.That(mapOneTerrain.Find("Neutral Ground")?.gameObject.activeInHierarchy, Is.True);
            Assert.That(mapOneTerrain.Find("ShallowWater/Riverbed"), Is.Null,
                "Map 1 must not retain the removed riverbed layer.");
            Assert.That(mapOne.isDirty, Is.False);

            Scene mapTwo = EditorSceneManager.OpenScene(Layered, OpenSceneMode.Single);
            Transform mapTwoTerrain = GameObject.Find("World/Open Tropical Battlefield/Terrain Surfaces")?.transform;
            Assert.That(mapTwoTerrain, Is.Not.Null);
            Assert.That(mapTwoTerrain.gameObject.activeInHierarchy, Is.True,
                "Map 2 must not disable the root that owns its shared base ground.");
            Transform neutral = mapTwoTerrain.Find("Neutral Ground");
            Assert.That(neutral, Is.Not.Null);
            Assert.That(neutral.gameObject.activeInHierarchy, Is.True,
                "Map 2 requires the shared neutral base ground beneath its zone meshes.");
            Assert.That(neutral.GetComponent<Renderer>()?.sharedMaterial, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Any(value => value.name == "Riverbed"), Is.False,
                "Neither inactive legacy terrain nor active Map 2 zones may retain a riverbed layer.");
            Assert.That(mapTwo.isDirty, Is.False);
            Assert.That(Hash(Backup), Is.EqualTo(ProtectedBackupHash));
        }

        private static string Hash(string path)
        {
            using SHA256 sha = SHA256.Create();
            return System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty);
        }

        private static void InvokeStatic(string fullName, string method)
        {
            System.Type type = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(value => value != null)
                ?? throw new System.TypeLoadException(fullName);
            type.GetMethod(method, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }
    }
}
