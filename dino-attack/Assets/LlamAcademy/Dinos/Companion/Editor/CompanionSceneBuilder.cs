using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Behavior;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using LlamAcademy.Dinos.DefenseBattle;
using LlamAcademy.Dinos.DefenseBattle.Editor;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Unit;

namespace LlamAcademy.Dinos.Companion.Editor
{
    /// <summary>
    /// Builds the companion prefab, wires one instance into Map 4, creates the
    /// standalone relationship lab, and adds its launch button to MapSelect.
    /// The builder edits scenes through UnityEditor.SceneManagement APIs so it
    /// remains safe for binary scenes and never relies on text/YAML surgery.
    /// </summary>
    public static class CompanionSceneBuilder
    {
        public const string Map4Path = "Assets/LlamAcademy/Dinos/Scenes/FirstPersonDefenseBattlefield.unity";
        public const string MapSelectPath = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";
        public const string LabPath = "Assets/LlamAcademy/Dinos/Scenes/CompanionLab.unity";
        public const string PrefabPath = "Assets/LlamAcademy/Dinos/Companion/Companion.prefab";
        private const string PanelSettingsPath = "Assets/LlamAcademy/Dinos/Companion/CompanionPanelSettings.asset";
        private const string ModelFolder = "Assets/ThirdParty/VRoid/SendagayaShino";
        private const string ModelPath = ModelFolder + "/Shino.fbx";

        [MenuItem("Dino Attack/Companion/Build Companion Map 4 + Lab")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play Mode and wait for compilation before building the companion.");
            CompanionGraphBuilder.BuildOrUpdate();
            GameObject prefab = BuildPrefab();
            BuildMap4(prefab);
            BuildLab();
            BuildMapSelectEntry();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("COMPANION_BUILD_OK");
        }

        [MenuItem("Dino Attack/Companion/Build Companion Prefab")]
        public static void BuildPrefabMenu() => BuildPrefab();

        [MenuItem("Dino Attack/Companion/Rebuild Companion Map 4")]
        public static void RebuildMap4Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play Mode and wait for compilation before rebuilding Map 4.");

            CompanionGraphBuilder.BuildOrUpdate();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                prefab = BuildPrefab();
            }

            BuildMap4(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("COMPANION_MAP4_REBUILD_OK");
        }

        public static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/LlamAcademy/Dinos/Companion");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                AssetDatabase.DeleteAsset(PrefabPath);

            GameObject root = new("Companion Guardian");
            root.layer = LayerMask.NameToLayer("Enemies");
            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.42f;
            agent.height = 2.2f;
            agent.speed = 5.4f;
            agent.acceleration = 16f;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = 1.15f;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.height = 2.2f;
            collider.radius = 0.42f;
            collider.center = Vector3.up * 1.1f;

            GameObject model = CreateModel(root.transform);
            GameObject attackRadiusObject = new("Companion Attack Radius");
            attackRadiusObject.layer = LayerMask.NameToLayer("Enemy Attack Radius");
            attackRadiusObject.transform.SetParent(root.transform, false);
            attackRadiusObject.transform.localPosition = Vector3.up;
            SphereCollider attackSphere = attackRadiusObject.AddComponent<SphereCollider>();
            attackSphere.isTrigger = true;
            attackSphere.radius = 12f;
            AttackRadius attackRadius = attackRadiusObject.AddComponent<AttackRadius>();

            // Runtime components are added by type name so this editor builder
            // remains usable while the companion runtime assembly is compiling.
            AddComponentIfAvailable(root, "LlamAcademy.Dinos.Companion.CompanionController");
            Component jevClient = AddComponentIfAvailable(root, "LlamAcademy.Dinos.Companion.CompanionJevClient");
            SetSerializedBool(jevClient, "UseGateway", true);
            root.AddComponent<BehaviorGraphAgent>();
            AddComponentIfAvailable(root, "LlamAcademy.Dinos.Companion.CompanionFixedGraphDriver");
            DinoTargetMetadata metadata = root.AddComponent<DinoTargetMetadata>();
            metadata.Configure(DinoTargetCategory.Defender, 9001);

            BehaviorGraph runtime = AssetDatabase.LoadAssetAtPath<BehaviorGraph>(CompanionGraphBuilder.GraphPath);
            if (runtime != null)
            {
                BehaviorGraphAgent graphAgent = root.GetComponent<BehaviorGraphAgent>();
                SerializedObject graphData = new(graphAgent);
                SerializedProperty graphProperty = graphData.FindProperty("m_Graph");
                if (graphProperty != null) graphProperty.objectReferenceValue = runtime;
                graphData.ApplyModifiedPropertiesWithoutUndo();
            }

            SetSerializedReferenceByTypeName(root, "LlamAcademy.Dinos.Companion.CompanionController", "CharacterModel", model);
            SetSerializedReferenceByTypeName(root, "LlamAcademy.Dinos.Companion.CompanionController", "Graph", runtime);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
            return prefab;
        }

        private static GameObject CreateModel(Transform parent)
        {
            GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (imported == null)
            {
                GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                placeholder.name = "Companion Model Placeholder";
                placeholder.transform.SetParent(parent, false);
                placeholder.transform.localPosition = Vector3.up;
                placeholder.transform.localScale = new Vector3(0.8f, 1.3f, 0.8f);
                return placeholder;
            }

            GameObject model = UnityEngine.Object.Instantiate(imported, parent);
            model.name = "Companion Shino Character";
            model.transform.localPosition = Vector3.zero;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float scale = 2.3f / Mathf.Max(0.01f, bounds.size.y);
                model.transform.localScale *= scale;
                model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                foreach (Renderer renderer in renderers)
                {
                    // Renderer material instances are not serialized by SaveAsPrefabAsset.
                    // Resolve every slot to a real project asset so the companion cannot
                    // become magenta after the prefab is reloaded in a player build.
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(CompanionMaterial).ToArray();
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
            }
            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                try
                {
                    RuntimeAnimatorController controller = CompanionMotifectAnimationBuilder.BuildController();
                    if (controller != null) animator.runtimeAnimatorController = controller;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Motifect companion animation controller unavailable; retaining the existing controller: " +
                                     (exception == null ? "unknown error" : exception.Message));
                }
            }
            foreach (Collider childCollider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(childCollider);
            AssetDatabase.SaveAssets();
            return model;
        }

        private static Material CompanionMaterial(Material original)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Standard");
            string name = original == null || string.IsNullOrWhiteSpace(original.name)
                ? "CompanionFallbackMaterial"
                : original.name;
            string path = ModelFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                if (shader == null) throw new InvalidOperationException("No compatible Unity material shader is available.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }

            Texture texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder + "/Textures/" + name + ".png");
            if (texture == null && original != null)
            {
                if (original.HasProperty("_BaseMap")) texture = original.GetTexture("_BaseMap");
                if (texture == null && original.HasProperty("_MainTex")) texture = original.GetTexture("_MainTex");
            }
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            }
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1f);
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.4f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.color = Color.white;
            material.EnableKeyword("_ALPHATEST_ON");
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildMap4(GameObject prefab)
        {
            if (prefab == null) throw new InvalidOperationException("Companion prefab was not created.");
            Scene scene = EditorSceneManager.OpenScene(Map4Path, OpenSceneMode.Single);
            // Older builder runs renamed prefab instances to "Companion" and
            // left them behind because cleanup only matched the prefab name.
            // Remove every previous companion root before creating the single
            // configured instance for this scene.
            GameObject[] existing = scene.GetRootGameObjects()
                .Where(root => root.name == "Companion Guardian" ||
                               FindComponentByTypeName(root, "LlamAcademy.Dinos.Companion.CompanionController") != null)
                .ToArray();
            foreach (GameObject stale in existing) UnityEngine.Object.DestroyImmediate(stale);
            GameObject companion = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (companion == null) throw new InvalidOperationException("Could not instantiate the companion prefab in Map 4.");

            FirstPersonDefender player = FindSingle<FirstPersonDefender>(scene);
            DefenseBattleCoordinator coordinator = FindSingle<DefenseBattleCoordinator>(scene);
            UnitSO damageType = ReadObject<UnitSO>(player, "DamageType");
            Transform playerTransform = player == null ? null : player.transform;
            companion.transform.position = playerTransform == null ? new Vector3(-5f, 0.1f, -40f) : playerTransform.position + new Vector3(-2.2f, 0f, -1.2f);
            companion.transform.rotation = playerTransform == null ? Quaternion.identity : playerTransform.rotation;
            BindController(companion, player, coordinator, damageType);
            BuildDialogueOverlay(scene, companion, player);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void BuildDialogueOverlay(Scene scene, GameObject companion, FirstPersonDefender player)
        {
            foreach (GameObject stale in scene.GetRootGameObjects().Where(root => root.name == "Companion Dialogue UI").ToArray())
                UnityEngine.Object.DestroyImmediate(stale);

            GameObject overlay = new("Companion Dialogue UI");
            SceneManager.MoveGameObjectToScene(overlay, scene);
            UIDocument document = overlay.AddComponent<UIDocument>();
            UIDocument existing = FindFirst<UIDocument>(scene);
            document.panelSettings = existing != null && existing.panelSettings != null
                ? existing.panelSettings
                : GetOrCreatePanelSettings();
            Component panel = overlay.AddComponent<CompanionDialoguePanel>();
            SetSerializedReference(panel, "Companion", companion == null ? null : companion.GetComponent<CompanionController>());
            SetSerializedReference(panel, "Player", player);
        }

        private static void BindController(GameObject companion, FirstPersonDefender player, DefenseBattleCoordinator coordinator, UnitSO damageType)
        {
            Component controller = FindComponentByTypeName(companion, "LlamAcademy.Dinos.Companion.CompanionController");
            if (controller == null) return;
            MethodInfo configure = controller.GetType().GetMethod("Configure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (configure != null)
            {
                try { configure.Invoke(controller, new object[] { player, coordinator, companion.transform.Find("Companion Shino Character")?.gameObject, damageType }); }
                catch (Exception exception) { Debug.LogWarning($"Companion Configure could not be called in editor: {exception.Message}"); }
            }
            SetSerializedReference(controller, "Player", player);
            SetSerializedReference(controller, "Coordinator", coordinator);
            SetSerializedReference(controller, "DamageType", damageType);
            SetSerializedReference(controller, "CharacterModel", companion.transform.Find("Companion Shino Character")?.gameObject);
        }

        private static void BuildLab()
        {
            if (File.Exists(LabPath)) AssetDatabase.DeleteAsset(LabPath);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject cameraObject = new("Companion Lab Camera", typeof(Camera));
            cameraObject.transform.position = new Vector3(0f, 2f, -6f);
            cameraObject.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.055f, 0.08f, 1f);
            camera.tag = "MainCamera";
            GameObject root = new("Companion Relationship Lab");
            AddComponentIfAvailable(root, "LlamAcademy.Dinos.Companion.CompanionLabController");
            AddComponentIfAvailable(root, "LlamAcademy.Dinos.Companion.CompanionLabLauncher");
            UIDocument document = root.GetComponent<UIDocument>() ?? root.AddComponent<UIDocument>();
            document.panelSettings = GetOrCreatePanelSettings();
            GameObject eventSystem = new("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            EditorSceneManager.SaveScene(scene, LabPath);
            EnsureBuildScene(LabPath);
        }

        private static void BuildMapSelectEntry()
        {
            Scene scene = EditorSceneManager.OpenScene(MapSelectPath, OpenSceneMode.Single);
            Canvas canvas = FindFirst<Canvas>(scene);
            if (canvas == null) return;
            Component launcher = FindComponentByTypeName(canvas.gameObject, "LlamAcademy.Dinos.Companion.CompanionLabLauncher");
            if (launcher == null) launcher = AddComponentIfAvailable(canvas.gameObject, "LlamAcademy.Dinos.Companion.CompanionLabLauncher");
            UnityEngine.UI.Button button = canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(value => value.name == "Companion Relationship Lab");
            if (button == null)
            {
                UnityEngine.UI.Button source = canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault();
                if (source == null) return;
                button = UnityEngine.Object.Instantiate(source, source.transform.parent);
                button.name = "Companion Relationship Lab";
                RectTransform rect = button.transform as RectTransform;
                if (rect != null) rect.anchoredPosition += new Vector2(0f, -80f);
            }
            foreach (Text text in button.GetComponentsInChildren<Text>(true)) text.text = "好感度实验室";
            // RemoveAllListeners only removes runtime callbacks. Cloned buttons retain
            // their serialized map launch, and repeated builds would append more lab launches.
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            if (launcher != null)
            {
                MethodInfo method = launcher.GetType().GetMethod("Launch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) ??
                                    launcher.GetType().GetMethod("OpenLab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (method != null && method.GetParameters().Length == 0)
                    UnityEventTools.AddPersistentListener(button.onClick, (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), launcher, method));
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static Component AddComponentIfAvailable(GameObject gameObject, string typeName)
        {
            if (gameObject == null) return null;
            Type type = FindType(typeName);
            return type == null || !typeof(Component).IsAssignableFrom(type) ? null : gameObject.GetComponent(type) ?? gameObject.AddComponent(type);
        }

        private static Component FindComponentByTypeName(GameObject gameObject, string typeName)
        {
            Type type = FindType(typeName);
            return type == null || gameObject == null ? null : gameObject.GetComponent(type);
        }

        private static Type FindType(string typeName)
        {
            Type type = Type.GetType(typeName + ", Assembly-CSharp") ?? Type.GetType(typeName);
            if (type != null) return type;
            return AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(typeName)).FirstOrDefault(value => value != null);
        }

        private static void SetSerializedReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            if (target == null) return;
            SerializedObject data = new(target);
            SerializedProperty property = data.FindProperty(propertyName);
            if (property == null)
            {
                property = data.FindProperty("<" + propertyName + ">k__BackingField");
            }
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) return;
            property.objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
            if (PrefabUtility.IsPartOfPrefabInstance(target))
            {
                // Scene references on a prefab instance are overrides. Without
                // recording them explicitly, SaveScene can drop the bindings
                // on the next reload, leaving the companion permanently idle.
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }

        private static void SetSerializedReferenceByTypeName(GameObject gameObject, string typeName, string propertyName, UnityEngine.Object value) =>
            SetSerializedReference(FindComponentByTypeName(gameObject, typeName), propertyName, value);

        private static void SetSerializedBool(UnityEngine.Object target, string propertyName, bool value)
        {
            if (target == null) return;
            SerializedObject data = new(target);
            SerializedProperty property = data.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean) return;
            property.boolValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        private static T ReadObject<T>(UnityEngine.Object target, string propertyName) where T : UnityEngine.Object
        {
            if (target == null) return null;
            SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
            if (property == null) property = new SerializedObject(target).FindProperty("<" + propertyName + ">k__BackingField");
            return property?.objectReferenceValue as T;
        }

        private static T FindSingle<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).SingleOrDefault();
        private static T FindFirst<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();

        private static void EnsureBuildScene(string path)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (!scenes.Any(scene => scene.path == path))
                EditorBuildSettings.scenes = scenes.Concat(new[] { new EditorBuildSettingsScene(path, true) }).ToArray();
        }

        private static PanelSettings GetOrCreatePanelSettings()
        {
            PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings != null) return settings;
            EnsureFolder("Assets/LlamAcademy/Dinos/Companion");
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Companion Panel Settings";
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
