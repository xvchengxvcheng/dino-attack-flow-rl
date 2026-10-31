using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.DefenseBattle;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LlamAcademy.Dinos.Companion.Editor
{
    /// <summary>
    /// Creates the standalone JEV village demo from the authored Map 4 scene.
    /// The source scene is copied through Unity's scene API so binary scene
    /// data and prefab overrides remain intact.
    /// </summary>
    public static class JevVillageDemoSceneBuilder
    {
        public const string SourcePath = "Assets/LlamAcademy/Dinos/Scenes/FirstPersonDefenseBattlefield.unity";
        public const string DemoPath = "Assets/LlamAcademy/Dinos/Scenes/JevVillageDemo.unity";

        private static readonly string[] GuardSearchNames = { "Archer", "Mage" };

        [MenuItem("Dino Attack/Companion/Build JEV Village Demo")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play Mode and wait for compilation before building the JEV demo.");

            EnsureSceneCopy();
            BuildDemoScene();
            BuildMapSelectEntry();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("JEV_VILLAGE_DEMO_BUILD_OK");
        }

        private static void EnsureSceneCopy()
        {
            Scene source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Single);
            if (File.Exists(DemoPath)) AssetDatabase.DeleteAsset(DemoPath);
            if (!EditorSceneManager.SaveScene(source, DemoPath, true))
                throw new InvalidOperationException($"Could not copy Map 4 to {DemoPath}.");
        }

        private static void BuildDemoScene()
        {
            Scene scene = EditorSceneManager.OpenScene(DemoPath, OpenSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects()
                         .Where(value => value.name == "JEV Village Demo Runtime")
                         .ToArray())
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            GameObject rootObject = new("JEV Village Demo Runtime");
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            JevVillageDemoRuntime runtime = rootObject.AddComponent<JevVillageDemoRuntime>();

            SetSerializedReference(runtime, "Player", FindSingle<FirstPersonDefender>(scene));
            SetSerializedReference(runtime, "Coordinator", FindSingle<DefenseBattleCoordinator>(scene));
            SetSerializedReference(runtime, "Companion", FindSingle<CompanionController>(scene));
            SetSerializedArray(runtime, "PassiveDinoTypes", FindPassiveDinoTypes());
            SetSerializedArray(runtime, "GuardPrefabs", FindGuardPrefabs());
            SetSerializedValue(runtime, "PassiveDinoCount", 8);
            SetSerializedValue(runtime, "GuardCount", 4);
            SetSerializedValue(runtime, "PassiveSpawnRadius", 26f);
            SetSerializedValue(runtime, "GuardSpawnRadius", 13f);
            SetSerializedValue(runtime, "ShowOverlay", true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EnsureBuildScene(DemoPath);
        }

        private static DinoSO[] FindPassiveDinoTypes()
        {
            return AssetDatabase.FindAssets("t:DinoSO")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<DinoSO>)
                .Where(value => value != null && value.Prefab != null)
                .OrderBy(value => value.name, StringComparer.Ordinal)
                .Take(3)
                .ToArray();
        }

        private static GameObject[] FindGuardPrefabs()
        {
            List<GameObject> result = new();
            foreach (string searchName in GuardSearchNames)
            {
                GameObject prefab = AssetDatabase.FindAssets($"{searchName} t:Prefab")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => path.EndsWith($"/{searchName}.prefab", StringComparison.OrdinalIgnoreCase))
                    .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                    .FirstOrDefault(value => value != null && value.GetComponentInChildren<Defender>(true) != null);
                if (prefab != null) result.Add(prefab);
            }
            return result.ToArray();
        }

        private static void BuildMapSelectEntry()
        {
            Scene scene = EditorSceneManager.OpenScene(CompanionSceneBuilder.MapSelectPath, OpenSceneMode.Single);
            MapSelectUI controller = FindSingle<MapSelectUI>(scene);
            Canvas canvas = FindSingle<Canvas>(scene);
            if (controller == null || canvas == null) throw new InvalidOperationException("MapSelect is missing MapSelectUI or Canvas.");

            Button button = canvas.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(value => value.name == "JEV Village Demo");
            if (button == null)
            {
                Button source = ReadObject<Button>(controller, "FirstPersonDefenseBattlefieldButton") ??
                                canvas.GetComponentsInChildren<Button>(true).FirstOrDefault();
                if (source == null) throw new InvalidOperationException("MapSelect has no button to clone for the JEV demo.");
                button = UnityEngine.Object.Instantiate(source, source.transform.parent);
                button.name = "JEV Village Demo";
                if (button.transform is RectTransform rect)
                    rect.anchoredPosition += new Vector2(0f, -82f);
            }

            foreach (Text text in button.GetComponentsInChildren<Text>(true)) text.text = "JEV村庄演示";
            foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true)) text.text = "JEV村庄演示";
            // Remove the cloned map button's serialized listener as well as
            // runtime listeners. RemoveAllListeners() only clears the latter,
            // so leaving the inherited Map 4 callback would launch the defense
            // scene before this demo callback gets a chance to run.
            for (int index = button.onClick.GetPersistentEventCount() - 1; index >= 0; index--)
                UnityEventTools.RemovePersistentListener(button.onClick, index);
            button.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(button.onClick, controller.LaunchJevVillageDemo);
            SetSerializedReference(controller, "JevVillageDemoButton", button);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void SetSerializedArray<T>(UnityEngine.Object target, string propertyName, IReadOnlyList<T> values)
            where T : UnityEngine.Object
        {
            SerializedObject data = new(target);
            SerializedProperty property = data.FindProperty(propertyName);
            if (property == null || !property.isArray) return;
            property.arraySize = values == null ? 0 : values.Count;
            for (int index = 0; index < property.arraySize; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedValue(UnityEngine.Object target, string propertyName, object value)
        {
            SerializedObject data = new(target);
            SerializedProperty property = data.FindProperty(propertyName);
            if (property == null) return;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer when value is int integer: property.intValue = integer; break;
                case SerializedPropertyType.Float when value is float single: property.floatValue = single; break;
                case SerializedPropertyType.Boolean when value is bool boolean: property.boolValue = boolean; break;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            if (target == null) return;
            SerializedObject data = new(target);
            SerializedProperty property = data.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) return;
            property.objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T ReadObject<T>(UnityEngine.Object target, string propertyName) where T : UnityEngine.Object
        {
            if (target == null) return null;
            return new SerializedObject(target).FindProperty(propertyName)?.objectReferenceValue as T;
        }

        private static T FindSingle<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();

        private static void EnsureBuildScene(string path)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (!scenes.Any(scene => scene.path == path))
                EditorBuildSettings.scenes = scenes.Concat(new[] { new EditorBuildSettingsScene(path, true) }).ToArray();
        }
    }
}
