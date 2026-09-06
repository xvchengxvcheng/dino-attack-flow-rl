using System;
using System.Linq;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Map.Adapters;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using LlamAcademy.Dinos.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace LlamAcademy.Dinos.LayeredBattlefield.Editor
{
    public static class LayeredBattlefieldValidator
    {
        [MenuItem("Dino Attack/Maps/Validate Task 4 Scenes")]
        public static void ValidateAssets()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
                throw new InvalidOperationException($"Refusing to close dirty scene '{active.path}'.");
            Scene layered = EditorSceneManager.OpenScene(LayeredBattlefieldSceneBuilder.LayeredScenePath, OpenSceneMode.Single);
            ValidateLayered(layered);
            Scene select = EditorSceneManager.OpenScene(LayeredBattlefieldSceneBuilder.MapSelectScenePath, OpenSceneMode.Single);
            ValidateMapSelect(select);
        }

        public static void ValidateLayered(Scene scene)
        {
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            if (!config.IsValid(out string error)) throw new InvalidOperationException(error);
            if (Find<LayeredBattlefieldLayoutController>(scene).Length != 1)
                throw new InvalidOperationException("Layered scene requires one layout controller.");
            DinoDeploymentZone[] zones = Find<DinoDeploymentZone>(scene)
                .Where(value => value.transform.root.name == "Layered Battlefield Runtime").ToArray();
            if (zones.Length != 15 || zones.Count(value => value.IsGeometryValid) != 15)
                throw new InvalidOperationException("Layered scene requires three complete valid five-zone presets.");
            if (Find<VillageHouse>(scene).Count(value => value.transform.root.name == "Layered Battlefield Runtime") != config.HouseCandidates.Count)
                throw new InvalidOperationException("Layered house candidate count is incomplete.");
            if (Find<WallSpawnSlot>(scene).Length != 6)
                throw new InvalidOperationException("Layered scene requires six stable wall slots.");
            if (Find<SessionDefenseLayoutController>(scene).Length != 1 ||
                Find<SessionGroundDefenseController>(scene).Length != 1 ||
                Find<RoundManager>(scene).Length != 1)
                throw new InvalidOperationException("Layered scene core gameplay controllers are incomplete.");
            NavMeshManager nav = Find<NavMeshManager>(scene).Single();
            SerializedProperty allowBuild = new SerializedObject(nav).FindProperty("AllowRuntimeBuild");
            if (allowBuild == null || allowBuild.boolValue)
                throw new InvalidOperationException("Layered scene must use pre-baked NavMeshData without runtime BuildNavMesh.");
        }

        private static void ValidateMapSelect(Scene scene)
        {
            Button[] buttons = Find<Button>(scene);
            string[] required = { "Open Tropical Battlefield", "Layered Battlefield" };
            string[] names = buttons.Select(value => value.name).ToArray();
            if (required.Any(name => names.Count(value => value == name) != 1))
                throw new InvalidOperationException("MapSelect requires exactly two direct-launch map choices.");
            string[] introductionButtons = { "Dinosaur Introduction", "Close Dinosaur Introduction" };
            if (introductionButtons.Any(name => names.Count(value => value == name) != 1))
                throw new InvalidOperationException("MapSelect requires an open/close dinosaur introduction flow.");
            if (buttons.Where(button => required.Contains(button.name) || introductionButtons.Contains(button.name))
                .Any(button => button.onClick.GetPersistentEventCount() != 1))
                throw new InvalidOperationException("Each MapSelect action requires exactly one persistent listener.");
            MapSelectUI controller = Find<MapSelectUI>(scene).SingleOrDefault();
            if (controller == null)
                throw new InvalidOperationException("MapSelect requires its UI controller.");
            Transform panel = Find<Canvas>(scene).Single().transform.Find("Dinosaur Introduction Panel");
            if (panel == null || panel.gameObject.activeSelf)
                throw new InvalidOperationException("MapSelect dinosaur introduction must exist and start closed.");
            EventSystem eventSystem = Find<EventSystem>(scene).SingleOrDefault();
            if (eventSystem == null || eventSystem.GetComponents<Component>().All(component => component == null ||
                    component.GetType().FullName != "UnityEngine.InputSystem.UI.InputSystemUIInputModule"))
                throw new InvalidOperationException("MapSelect requires InputSystemUIInputModule.");
            if (Find<Image>(scene).All(image => image.name != "Dino Attack Logo" || image.sprite == null))
                throw new InvalidOperationException("MapSelect requires the Dino Attack logo.");
        }

        private static T[] Find<T>(Scene scene) where T : Component =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(value => value.gameObject.scene == scene).ToArray();
    }
}
