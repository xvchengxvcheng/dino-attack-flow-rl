using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Map.Adapters;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.UI;
using LlamAcademy.Dinos.Unit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.DefenseBattle.Editor
{
    public static class DefenseBattlefieldSceneBuilder
    {
        public const string SourceScenePath = "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity";
        public const string TargetScenePath = "Assets/LlamAcademy/Dinos/Scenes/DefenseBattlefield.unity";
        public const string MapSelectScenePath = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";

        [MenuItem("Dino Attack/Maps/Build Defense Battlefield")]
        public static void BuildFromMenu() => Build();

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Defense battlefield builder requires a stopped, compiled Editor.");
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty && active.path != TargetScenePath)
                throw new InvalidOperationException($"Refusing to close dirty scene '{active.path}'.");

            string sourceHash = Sha256(SourceScenePath);
            File.Copy(Path.GetFullPath(SourceScenePath), Path.GetFullPath(TargetScenePath), true);
            AssetDatabase.ImportAsset(TargetScenePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            ConfigureDefenseScene(scene);
            EditorSceneManager.SaveScene(scene);
            AddSceneToBuildSettings(TargetScenePath);
            RepairMapSelect();
            AssetDatabase.SaveAssets();

            if (!string.Equals(sourceHash, Sha256(SourceScenePath), StringComparison.Ordinal))
                throw new InvalidOperationException("LayeredBattlefield.unity changed while deriving Map 3.");
            ValidateSceneAsset();
            Debug.Log("DefenseBattlefield.unity created and wired without changing LayeredBattlefield.unity.");
        }

        private static void ConfigureDefenseScene(Scene scene)
        {
            GameObject previous = scene.GetRootGameObjects().SingleOrDefault(value => value.name == "Defense Battlefield Runtime");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);

            EnemyAIController enemy = ExactlyOne<EnemyAIController>(scene);
            enemy.ConfigurePlayerDefensePlacement(true);

            SessionDefenseLayoutController sessionDefense = ExactlyOne<SessionDefenseLayoutController>(scene);
            SessionGroundDefenseController groundDefense = ExactlyOne<SessionGroundDefenseController>(scene);
            Wall wallPrefab = ReadObject<Wall>(sessionDefense, "WallPrefab");
            Defender archerPrefab = ReadObject<Defender>(sessionDefense, "ArcherPrefab");
            Defender magePrefab = ReadObject<Defender>(groundDefense, "MagePrefab");
            Transform spawnRoot = ReadObject<Transform>(sessionDefense, "SpawnRoot");
            float wallGuardRadius = ReadFloat(sessionDefense, "WallGuardAttackRadius");
            Camera camera = Find<Camera>(scene).Single(value => value.CompareTag("MainCamera"));
            LayeredBattlefieldLayoutController layeredLayout = ExactlyOne<LayeredBattlefieldLayoutController>(scene);
            DinoDeploymentZone[] zones = ReadBoundZones(layeredLayout);
            int grassLayer = LayerMask.NameToLayer("Grass");
            if (wallPrefab == null || archerPrefab == null || magePrefab == null || spawnRoot == null)
                throw new InvalidOperationException("Map 2 does not expose the required reusable defense prefabs and spawn root.");
            if (zones.Length != 15)
                throw new InvalidOperationException($"Map 2 must expose 15 formal zone bindings, found {zones.Length}.");
            if (grassLayer < 0)
            {
                throw new InvalidOperationException("The shared Grass ground layer is unavailable.");
            }

            GameObject root = new("Defense Battlefield Runtime");
            SceneManager.MoveGameObjectToScene(root, scene);
            DefensePlacementController placement = root.AddComponent<DefensePlacementController>();
            placement.Configure(
                camera,
                wallPrefab,
                archerPrefab,
                magePrefab,
                spawnRoot,
                zones,
                1 << grassLayer,
                new Vector2(-24f, -49f),
                new Vector2(20f, -11f),
                wallGuardRadius);
            root.AddComponent<DefenseBattleCoordinator>();

            RuntimeUI runtimeUi = Find<RuntimeUI>(scene).SingleOrDefault();
            if (runtimeUi == null)
                throw new InvalidOperationException("Map 2 runtime UI document is missing.");
            GameObject uiHost = runtimeUi.gameObject;
            UnityEngine.Object.DestroyImmediate(runtimeUi);
            foreach (GameResultActionController result in uiHost.GetComponents<GameResultActionController>())
                UnityEngine.Object.DestroyImmediate(result);
            if (uiHost.GetComponent<UIDocument>() == null)
                throw new InvalidOperationException("Map 2 runtime UI host has no UIDocument.");
            if (uiHost.GetComponent<DefenseBattleUI>() == null)
                uiHost.AddComponent<DefenseBattleUI>();

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void RepairMapSelect()
        {
            Scene mapSelect = EditorSceneManager.OpenScene(MapSelectScenePath, OpenSceneMode.Single);
            Type builder = AppDomain.CurrentDomain.GetAssemblies()
                .Select(value => value.GetType("LlamAcademy.Dinos.LayeredBattlefield.Editor.LayeredBattlefieldSceneBuilder", false))
                .FirstOrDefault(value => value != null)
                ?? throw new TypeLoadException("LayeredBattlefieldSceneBuilder");
            builder.GetMethod("RepairMapSelectBasicInteraction")?.Invoke(null, null);
            mapSelect = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(mapSelect);
        }

        public static void ValidateSceneAsset()
        {
            Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            ExactlyOne<DefensePlacementController>(scene);
            ExactlyOne<DefenseBattleCoordinator>(scene);
            ExactlyOne<DefenseBattleUI>(scene);
            if (ReadBoundZones(ExactlyOne<LayeredBattlefieldLayoutController>(scene)).Length != 15)
                throw new InvalidOperationException("Map 3 must preserve all three Z1-Z5 geometry presets from Map 2.");
            if (Find<RuntimeUI>(scene).Length != 0)
                throw new InvalidOperationException("Map 3 must not run the attacker-side RuntimeUI.");
            if (!EditorBuildSettings.scenes.Any(value => value.enabled && value.path == TargetScenePath))
                throw new InvalidOperationException("Map 3 is missing from enabled build settings.");
            if (scene.isDirty)
                throw new InvalidOperationException("Map 3 validation dirtied the saved scene.");
        }

        private static void AddSceneToBuildSettings(string path)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Any(value => string.Equals(value.path, path, StringComparison.Ordinal))) return;
            EditorBuildSettings.scenes = scenes.Concat(new[] { new EditorBuildSettingsScene(path, true) }).ToArray();
        }

        private static T ReadObject<T>(Component component, string field) where T : UnityEngine.Object
        {
            SerializedProperty property = new SerializedObject(component).FindProperty(field)
                ?? throw new MissingFieldException(component.GetType().Name, field);
            return property.objectReferenceValue as T;
        }

        private static float ReadFloat(Component component, string field)
        {
            SerializedProperty property = new SerializedObject(component).FindProperty(field)
                ?? throw new MissingFieldException(component.GetType().Name, field);
            return property.floatValue;
        }

        private static DinoDeploymentZone[] ReadBoundZones(LayeredBattlefieldLayoutController layout)
        {
            SerializedProperty bindings = new SerializedObject(layout).FindProperty("ZoneBindings")
                ?? throw new MissingFieldException(layout.GetType().Name, "ZoneBindings");
            DinoDeploymentZone[] result = new DinoDeploymentZone[bindings.arraySize];
            for (int index = 0; index < bindings.arraySize; index++)
            {
                result[index] = bindings.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("Zone").objectReferenceValue as DinoDeploymentZone;
            }
            return result.Where(value => value != null).Distinct().ToArray();
        }

        private static T[] Find<T>(Scene scene) where T : Component =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(value => value.gameObject.scene == scene).ToArray();

        private static T ExactlyOne<T>(Scene scene) where T : Component
        {
            T[] found = Find<T>(scene);
            if (found.Length != 1) throw new InvalidOperationException($"Expected one {typeof(T).Name}, found {found.Length}.");
            return found[0];
        }

        private static string Sha256(string assetPath)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(Path.GetFullPath(assetPath)))).Replace("-", string.Empty);
        }
    }
}
