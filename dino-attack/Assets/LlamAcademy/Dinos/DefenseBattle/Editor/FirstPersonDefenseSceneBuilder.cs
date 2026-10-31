using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor.Animations;
using System.Security.Cryptography;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LlamAcademy.Dinos.DefenseBattle.Editor
{
    public static class FirstPersonDefenseSceneBuilder
    {
        public const string SourceScene = "Assets/LlamAcademy/Dinos/Scenes/DefenseBattlefield.unity";
        public const string TargetScene = "Assets/LlamAcademy/Dinos/Scenes/FirstPersonDefenseBattlefield.unity";
        private const string ModelFolder = "Assets/ThirdParty/VRoid/SendagayaShino";

        [MenuItem("Dino Attack/Maps/Build Third Person Defense Battlefield")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("A stopped, compiled Editor is required.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your open scene before building Map 4.");
            string sourceHash = Hash(SourceScene);
            if (!File.Exists(TargetScene) && !AssetDatabase.CopyAsset(SourceScene, TargetScene))
                throw new InvalidOperationException("Unable to duplicate Map 3.");
            Scene scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
            GameObject oldPlayer = scene.GetRootGameObjects().SingleOrDefault(r => r.name == "First Person Defender");
            if (oldPlayer != null) UnityEngine.Object.DestroyImmediate(oldPlayer);
            DefenseBattleCoordinator coordinator = Find<DefenseBattleCoordinator>(scene).Single();
            Camera strategyCamera = Find<Camera>(scene).Single(c => c.CompareTag("MainCamera"));
            DefensePlacementController placement = Find<DefensePlacementController>(scene).Single();
            var placementData = new SerializedObject(placement);
            Defender mage = placementData.FindProperty("MagePrefab").objectReferenceValue as Defender;
            if (mage == null || mage.UnitType == null) throw new InvalidOperationException("Map 3 mage armor configuration is missing.");

            GameObject root = new("First Person Defender");
            root.layer = LayerMask.NameToLayer("Enemies");
            var motor = root.AddComponent<CharacterController>();
            motor.height = 2.6f;
            motor.radius = 0.55f;
            motor.center = Vector3.up * 1.3f;
            motor.stepOffset = 0.35f;
            motor.skinWidth = 0.04f;
            motor.slopeLimit = 50f;
            motor.enabled = false;
            var player = root.AddComponent<FirstPersonDefender>();

            ConfigureHumanoid();
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + "/Shino.fbx");
            if (imported == null) throw new InvalidOperationException("The downloaded VRoid character has not imported.");
            GameObject model = UnityEngine.Object.Instantiate(imported, root.transform);
            model.name = "VRoid Shino Character";
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Character model has no renderers.");
            Bounds modelBounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) modelBounds.Encapsulate(renderer.bounds);
            float modelScale = 2.6f / modelBounds.size.y;
            model.transform.localScale *= modelScale;
            model.transform.localPosition = new Vector3(-modelBounds.center.x, -modelBounds.min.y, -modelBounds.center.z) * modelScale;
            foreach (Renderer renderer in renderers)
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(AnimeMaterial).ToArray();
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }

            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("The imported character requires a valid Humanoid avatar.");
            animator.runtimeAnimatorController = BuildAnimator();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);

            GameObject cameraObject = new("Third Person Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = Vector3.up * 2.25f;
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.06f;
            camera.farClipPlane = strategyCamera.farClipPlane;
            camera.clearFlags = strategyCamera.clearFlags;
            camera.backgroundColor = strategyCamera.backgroundColor;
            camera.cullingMask = strategyCamera.cullingMask;
            var follow = cameraObject.AddComponent<ThirdPersonFollowCamera>();
            SetObject(follow, "Player", player);
            GameObject staff = new("Arcane Staff");
            staff.transform.SetParent(animator.GetBoneTransform(HumanBodyBones.RightHand), false);
            // FBX stores the rig in centimetres; compensate the hand's world scale for Unity-made props.
            float handScale = staff.transform.parent.lossyScale.x;
            staff.transform.localPosition = new Vector3(0.04f, 0.05f, 0.03f) / handScale;
            staff.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            staff.transform.localScale = Vector3.one * (1.1f / handScale);
            DefenderAnimationBuilder.ConfigureStaff(animator, staff.transform);
            Material wood = MaterialAsset("StaffWood", new Color(0.2f, 0.08f, 0.035f));
            Material gold = MaterialAsset("StaffGold", new Color(0.95f, 0.6f, 0.12f));
            Material crystal = MaterialAsset("ArcaneCrystal", new Color(0.08f, 0.85f, 1f));
            crystal.EnableKeyword("_EMISSION");
            crystal.SetColor("_EmissionColor", new Color(0.05f, 1.5f, 2f));
            EditorUtility.SetDirty(crystal);
            Prop(staff.transform, PrimitiveType.Cylinder, "Wooden shaft", Vector3.zero, new Vector3(0.07f, 0.62f, 0.07f), wood);
            Prop(staff.transform, PrimitiveType.Cylinder, "Gold collar", new Vector3(0f, 0.62f, 0), new Vector3(0.15f, 0.05f, 0.15f), gold);
            var tip = Prop(staff.transform, PrimitiveType.Cube, "Crystal", new Vector3(0, 0.83f, 0), new Vector3(0.16f, 0.3f, 0.16f), crystal);
            tip.localRotation = Quaternion.Euler(0, 45, 20);

            GameObject lineObject = new("Spell Trail", typeof(LineRenderer));
            lineObject.transform.SetParent(root.transform, false);
            LineRenderer line = lineObject.GetComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = 0.06f;
            line.endWidth = 0.015f;
            line.sharedMaterial = crystal;
            line.useWorldSpace = true;
            line.enabled = false;
            SetObject(player, "Coordinator", coordinator);
            SetObject(player, "CharacterModel", model);
            SetObject(player, "StrategyCamera", strategyCamera);
            SetObject(player, "FirstPersonCamera", camera);
            SetObject(player, "DamageType", mage.UnitType);
            SetObject(player, "Staff", staff.transform);
            SetObject(player, "SpellTrail", line);
            var abilities = root.AddComponent<DefenderAbilities>();
            SetObject(abilities, "Player", player);
            Material burstMaterial = EffectMaterial("Starfall", new Color(0.5f, 0.22f, 1f, 0.24f));
            Transform burst = Prop(root.transform, PrimitiveType.Sphere, "Starfall Burst", Vector3.zero, Vector3.one, burstMaterial);
            burst.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            burst.gameObject.SetActive(false);
            SetObject(abilities, "BurstVisual", burst);
            Material shieldMaterial = EffectMaterial("ArcaneShield", new Color(0.08f, 0.7f, 1f, 0.2f));
            Transform shield = Prop(root.transform, PrimitiveType.Sphere, "Arcane Shield", Vector3.up * 1.3f, new Vector3(2.2f, 3f, 2.2f), shieldMaterial);
            shield.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shield.gameObject.SetActive(false);
            SetObject(abilities, "ShieldVisual", shield);
            CameraControl control = Find<CameraControl>(scene).FirstOrDefault();
            if (control != null)
            {
                var boundsCollider = new SerializedObject(control).FindProperty("WorldBounds").objectReferenceValue as BoxCollider;
                if (boundsCollider != null)
                {
                    var data = new SerializedObject(player);
                    data.FindProperty("WalkBounds").boundsValue = boundsCollider.bounds;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            var ui = Find<DefenseBattleUI>(scene).Single();
            var hud = ui.GetComponent<FirstPersonDefenseHud>() ?? ui.gameObject.AddComponent<FirstPersonDefenseHud>();
            SetObject(hud, "Player", player);
            model.SetActive(false);
            cameraObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (!scenes.Any(s => s.path == TargetScene))
                EditorBuildSettings.scenes = scenes.Concat(new[] { new EditorBuildSettingsScene(TargetScene, true) }).ToArray();
            AddMapButton();
            AssetDatabase.SaveAssets();
            if (Hash(SourceScene) != sourceHash) throw new InvalidOperationException("Map 3 changed unexpectedly.");
            Debug.Log("MAP4_BUILD_OK: copied Map 3, added one player, preserved Map 3 SHA256=" + sourceHash);
        }

        private static void AddMapButton()
        {
            var scene = EditorSceneManager.OpenScene("Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity", OpenSceneMode.Single);
            var controller = Find<MapSelectUI>(scene).Single();
            Button[] buttons = Find<Button>(scene);
            var original = buttons.Single(b => b.name == "Defense Battlefield");
            var button = buttons.SingleOrDefault(b => b.name == "First Person Defense Battlefield");
            if (button == null) button = UnityEngine.Object.Instantiate(original, original.transform.parent);
            button.name = "First Person Defense Battlefield";
            ((RectTransform)button.transform).anchoredPosition = new Vector2(520f, -370f);
            foreach (Text text in button.GetComponentsInChildren<Text>(true)) text.text = "地图4 · 星光守卫";
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(button.onClick, i);
            UnityEventTools.AddPersistentListener(button.onClick, controller.LaunchFirstPersonDefenseBattlefield);
            SetObject(controller, "FirstPersonDefenseBattlefieldButton", button);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureHumanoid()
        {
            string path = ModelFolder + "/Shino.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing character FBX importer.");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.SaveAndReimport();
            var bones = new List<HumanBone>();
            foreach (Match match in Regex.Matches(File.ReadAllText(ModelFolder + "/humanoid-bones.json"), "\"([^\"]+)\": \"([^\"]+)\""))
            {
                string name = HumanTrait.BoneName.FirstOrDefault(n => string.Equals(n.Replace(" ", ""), match.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                if (name != null) bones.Add(new HumanBone { humanName = name, boneName = match.Groups[2].Value, limit = new HumanLimit { useDefaultValues = true } });
            }
            HumanDescription description = importer.humanDescription;
            description.human = bones.ToArray();
            // Let the FBX importer use the file's rest skeleton. Scene transforms have already
            // undergone FBX unit conversion and must not be fed back as source skeleton overrides.
            description.skeleton = Array.Empty<SkeletonBone>();
            description.upperArmTwist = description.lowerArmTwist = description.upperLegTwist = description.lowerLegTwist = 0.5f;
            description.armStretch = description.legStretch = 0.05f;
            description.hasTranslationDoF = false;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = description;
            importer.importAnimation = false;
            importer.SaveAndReimport();
        }

        private static Material AnimeMaterial(Material original)
        {
            string name = original.name;
            string path = ModelFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Universal Render Pipeline/Unlit");
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.4f);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder + "/Textures/" + name + ".png"));
            material.SetColor("_BaseColor", Color.white);
            material.EnableKeyword("_ALPHATEST_ON");
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static RuntimeAnimatorController BuildAnimator() => DefenderAnimationBuilder.BuildController();

        private static Material EffectMaterial(string name, Color color)
        {
            Material material = MaterialAsset(name, color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * 0.6f);
            material.renderQueue = 3000;
            return material;
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string path = ModelFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform Prop(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject prop = GameObject.CreatePrimitive(type);
            prop.name = name;
            prop.transform.SetParent(parent, false);
            prop.transform.localPosition = position;
            prop.transform.localScale = scale;
            prop.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(prop.GetComponent<Collider>());
            return prop.transform;
        }

        private static void SetObject(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        private static string Hash(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }

    }
}
