using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.Map.Editor
{
    [Serializable]
    public sealed class VisualPolishAudit
    {
        public bool RequiredRootsPresent;
        public bool Z3DecorationEmpty;
        public int ColliderCount;
        public int TriggerCount;
        public int RigidbodyCount;
        public int NavMeshComponentCount;
        public int AgentCount;
        public int GameplayBehaviourCount;
        public string[] ViolatingPaths = Array.Empty<string>();

        public bool IsIsolated => RequiredRootsPresent && Z3DecorationEmpty &&
            ColliderCount == 0 && TriggerCount == 0 && RigidbodyCount == 0 &&
            NavMeshComponentCount == 0 && AgentCount == 0 && GameplayBehaviourCount == 0;
    }

    /// <summary>
    /// Owns the render-only additions for the Stage 8 battlefield. Everything authored below
    /// Art Pass must remain presentation-only so that training, navigation and combat contracts
    /// cannot be changed by a visual repair.
    /// </summary>
    public static class OpenTropicalBattlefieldVisualPolishBuilder
    {
        private const string MaterialFolder = "Assets/LlamAcademy/Dinos/Stage8/Materials";
        private const string VolumeProfilePath = "Assets/DefaultVolumeProfile.asset";
        private const string MountainPath = "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Ultimate Pack/T/- Meshes_T/Terrains_T/mountain-desert.fbx";
        private const string MiniForestPath = "Assets/ThirdParty/Kenney/MiniForest/Models/";
        private const string TribalRoot = "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/";
        private const string TribalNature = TribalRoot + "Nature_T/";
        private const string TribalPrehistoric = TribalNature + "Prehistoric_T/";
        private const string TribalGrass = TribalNature + "Grass_T/";
        private const string TribalProps = TribalRoot + "Tribal_T/";

        private enum DecorationMaterialPolicy
        {
            Preserve,
            Canyon,
            Grass
        }

        private readonly struct DecorationSpec
        {
            public readonly string Root;
            public readonly string Name;
            public readonly string AssetPath;
            public readonly Vector3 Position;
            public readonly float Yaw;
            public readonly Vector3 Scale;
            public readonly DecorationMaterialPolicy MaterialPolicy;
            public readonly bool CastShadows;

            public DecorationSpec(string root, string name, string assetPath, float x, float z,
                float yaw, float scale, DecorationMaterialPolicy materialPolicy, bool castShadows = true)
            {
                Root = root;
                Name = name;
                AssetPath = assetPath;
                Position = new Vector3(x, 0f, z);
                Yaw = yaw;
                Scale = Vector3.one * scale;
                MaterialPolicy = materialPolicy;
                CastShadows = castShadows;
            }
        }

        private static readonly DecorationSpec[] TerrainDecorations =
        {
            new("Terrain Dressing/Z1 Ruins", "Ruins Rock 01", MiniForestPath + "rocks-low.fbx", -47f, -42f, 12f, 1.05f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z1 Ruins", "Ruins Grass 02", MiniForestPath + "patch-grass.fbx", -42f, -42f, -18f, 1.25f, DecorationMaterialPolicy.Grass),
            new("Terrain Dressing/Z1 Ruins", "Ruins Stones 03", MiniForestPath + "stones.fbx", -39f, -39f, 31f, 1.0f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z1 Ruins", "Ruins Rock 04", MiniForestPath + "rocks-ramp.fbx", -48f, -33f, 76f, 0.90f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z1 Ruins", "Ruins Grass 05", TribalGrass + "Grass_Long.prefab", -42f, -30f, -24f, 0.82f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z1 Ruins", "Ruins Stones 06", MiniForestPath + "stones.fbx", -36f, -33f, 9f, 0.86f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z1 Ruins", "Ruins Grass 07", TribalGrass + "Grass.prefab", -45f, -24f, 18f, 0.90f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z1 Ruins", "Ruins Rock 08", MiniForestPath + "rocks-low.fbx", -36f, -27f, -11f, 0.92f, DecorationMaterialPolicy.Canyon),

            new("Terrain Dressing/Z2 Meadow", "Meadow Grass 01", TribalGrass + "Grass_Long.prefab", -39f, 6f, 9f, 0.92f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z2 Meadow", "Meadow Fern 02", TribalPrehistoric + "Fern_Prehistoric.prefab", -36f, 0f, -17f, 0.74f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z2 Meadow", "Meadow Patch 03", MiniForestPath + "patch-grass.fbx", -36f, 6f, 32f, 1.35f, DecorationMaterialPolicy.Grass),
            new("Terrain Dressing/Z2 Meadow", "Meadow Grass 04", TribalGrass + "Grass.prefab", -33f, -3f, -7f, 0.94f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z2 Meadow", "Meadow Fern 05", TribalPrehistoric + "Fern_Prehistoric.prefab", -33f, 3f, 25f, 0.70f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z2 Meadow", "Meadow Stones 06", MiniForestPath + "stones.fbx", -33f, 9f, -13f, 0.82f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z2 Meadow", "Meadow Grass 07", TribalGrass + "Grass_Long.prefab", -30f, -9f, 16f, 0.86f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z2 Meadow", "Meadow Patch 08", MiniForestPath + "patch-grass.fbx", -30f, -3f, 44f, 1.15f, DecorationMaterialPolicy.Grass),
            new("Terrain Dressing/Z2 Meadow", "Meadow Fern 09", TribalPrehistoric + "Fern_Prehistoric.prefab", -30f, 6f, -27f, 0.66f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z2 Meadow", "Meadow Stone 10", MiniForestPath + "rocks-low.fbx", -27f, -12f, 11f, 0.58f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z2 Meadow", "Meadow Grass 11", TribalGrass + "Grass.prefab", -27f, 0f, 36f, 0.78f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z2 Meadow", "Meadow Grass 12", TribalGrass + "Grass_Long.prefab", -24f, 12f, -34f, 0.80f, DecorationMaterialPolicy.Preserve),

            new("Terrain Dressing/Z4 Palms", "Palm Grove Grass 01", TribalGrass + "Grass_Long.prefab", 15f, -12f, 12f, 0.86f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z4 Palms", "Palm Grove Cycad 02", TribalPrehistoric + "Cycad_Triple_Prehistoric.prefab", 18f, -18f, -22f, 0.72f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z4 Palms", "Palm Grove Fern 03", TribalPrehistoric + "Fern_Prehistoric.prefab", 18f, -9f, 31f, 0.72f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z4 Palms", "Palm Grove Bush 04", TribalPrehistoric + "Bush_Cycad_Prehistoric.prefab", 18f, 0f, -9f, 0.68f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z4 Palms", "Palm Grove Grass 05", TribalGrass + "Grass.prefab", 21f, -15f, 24f, 0.92f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z4 Palms", "Palm Grove Palm 06", TribalPrehistoric + "Palm_Prehistoric_Small.prefab", 21f, -6f, -19f, 1.18f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z4 Palms", "Palm Grove Cycad 07", TribalPrehistoric + "Cycad_Triple_Prehistoric.prefab", 21f, 6f, 13f, 0.66f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z4 Palms", "Palm Grove Fern 08", TribalPrehistoric + "Fern_Prehistoric.prefab", 24f, -12f, -31f, 0.70f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z4 Palms", "Palm Grove Grass 09", TribalGrass + "Grass_Long.prefab", 24f, 0f, 17f, 0.82f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z4 Palms", "Palm Grove Bush 10", TribalPrehistoric + "Bush_Cycad_Prehistoric.prefab", 27f, 6f, -12f, 0.62f, DecorationMaterialPolicy.Preserve),

            new("Terrain Dressing/Z5 Rocks", "Rocky Shelf Rock 01", MiniForestPath + "rocks-low.fbx", 27f, -42f, 8f, 1.0f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z5 Rocks", "Rocky Shelf Grass 02", TribalGrass + "Grass_Long.prefab", 30f, -39f, -16f, 0.82f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z5 Rocks", "Rocky Shelf Stones 03", MiniForestPath + "stones.fbx", 33f, -42f, 33f, 0.94f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z5 Rocks", "Rocky Shelf Rock 04", MiniForestPath + "rocks-ramp.fbx", 36f, -36f, 71f, 0.86f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z5 Rocks", "Rocky Shelf Grass 05", TribalGrass + "Grass.prefab", 30f, -30f, -8f, 0.90f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z5 Rocks", "Rocky Shelf Stones 06", MiniForestPath + "stones.fbx", 36f, -30f, 17f, 0.82f, DecorationMaterialPolicy.Canyon),
            new("Terrain Dressing/Z5 Rocks", "Rocky Shelf Grass 07", TribalGrass + "Grass_Long.prefab", 39f, -27f, -23f, 0.78f, DecorationMaterialPolicy.Preserve),
            new("Terrain Dressing/Z5 Rocks", "Rocky Shelf Rock 08", MiniForestPath + "rocks-low.fbx", 42f, -24f, 14f, 0.78f, DecorationMaterialPolicy.Canyon)
        };

        private static readonly DecorationSpec[] VillageDecorations =
        {
            new("", "Vase Small West", TribalProps + "Vase_Tribal_Small.prefab", -7.2f, -40.4f, 8f, 0.85f, DecorationMaterialPolicy.Preserve),
            new("", "Vase Small East", TribalProps + "Vase_Tribal_Small.prefab", 2.6f, -40.1f, -12f, 0.82f, DecorationMaterialPolicy.Preserve),
            new("", "Vase Big", TribalProps + "Vase_Tribal_Big.prefab", -6.1f, -32.6f, 16f, 0.84f, DecorationMaterialPolicy.Preserve),
            new("", "Cooking Pot", TribalProps + "CookingPot_Tribal.prefab", 0.4f, -33.0f, -8f, 0.82f, DecorationMaterialPolicy.Preserve),
            new("", "Stone Table", TribalProps + "Table_Stone_Tribal.prefab", -4.0f, -42.6f, 4f, 0.78f, DecorationMaterialPolicy.Preserve),
            new("", "Dinosaur Totem", TribalProps + "Totem_Dinosaur_Tribal.prefab", -2.1f, -29.0f, 0f, 0.90f, DecorationMaterialPolicy.Preserve),
            new("", "Plaza Flag West", TribalProps + "Flag_Stand_Tribal.prefab", -8f, -38f, 0f, 0.80f, DecorationMaterialPolicy.Preserve),
            new("", "Plaza Flag East", TribalProps + "Flag_Stand_Tribal.prefab", 4f, -38f, 0f, 0.80f, DecorationMaterialPolicy.Preserve)
        };

        private static readonly DecorationSpec[] DistantDecorations = Array.Empty<DecorationSpec>();

        private static readonly string[] RequiredArtPaths =
        {
            "Lighting & Atmosphere",
            "Terrain Dressing",
            "Terrain Dressing/Z1 Ruins",
            "Terrain Dressing/Z2 Meadow",
            "Terrain Dressing/Z4 Palms",
            "Terrain Dressing/Z5 Rocks",
            "Canyon Rim/Distant Scenery"
        };

        private static readonly string[] FrozenHierarchyPaths =
        {
            "/World/Open Tropical Battlefield/Deployment Zones",
            "/World/Open Tropical Battlefield/Terrain Surfaces",
            "/World/Open Tropical Battlefield/Village",
            "/World/Open Tropical Battlefield/Defense Presets",
            "/World/Open Tropical Battlefield/Session Defense Layout",
            "/World/Open Tropical Battlefield/Session Ground Defense Layout",
            "/World/Camera Bounds",
            "/Training/FormalTrainingEnvironment"
        };

        private static readonly string[] FrozenOwnerTypeNames =
        {
            "LlamAcademy.Dinos.RoundManagement.RoundManager",
            "LlamAcademy.Dinos.Enemy.EnemyAIController",
            "LlamAcademy.Dinos.Deployment.DinoDeploymentService",
            "LlamAcademy.Dinos.Map.DinoDeploymentZonePresenter",
            "LlamAcademy.Dinos.Session.DinoSessionRestartService"
        };

        private static readonly string[] RuntimeUiBehavioralIds =
        {
            "start-button", "wave-text", "dino-button-container", "resources-container",
            "win-lose-text", "deployment-feedback", "terrain-feedback", "phase-status",
            "food-status", "threat-status", "result-panel", "session-summary", "restart-button"
        };

        public static void Build(
            Scene scene,
            Transform world,
            Transform battlefield,
            Transform terrainRoot,
            Transform village,
            Transform outerScenery)
        {
            if (!scene.IsValid() || !scene.isLoaded || world == null || battlefield == null ||
                terrainRoot == null || village == null || outerScenery == null)
            {
                throw new InvalidOperationException("The loaded battlefield hierarchy is incomplete.");
            }

            Transform art = EnsureChild(battlefield, "Art Pass");
            EnsureChild(art, "Lighting & Atmosphere");
            Transform dressing = EnsureChild(art, "Terrain Dressing");
            EnsureChild(dressing, "Z1 Ruins");
            EnsureChild(dressing, "Z2 Meadow");
            EnsureChild(dressing, "Z4 Palms");
            EnsureChild(dressing, "Z5 Rocks");
            Transform canyonRim = EnsureChild(art, "Canyon Rim");
            Transform distant = EnsureChild(canyonRim, "Distant Scenery");

            ConfigureLightingAndAtmosphere(scene);
            ConfigureVolumeProfile();
            ConfigureStage8Materials();
            BuildTerrainDecorations(battlefield, art);
            BuildVillageDecorations(art);
            BuildDistantDecorations(scene, distant);

            VisualPolishAudit audit = Audit(battlefield);
            if (!audit.IsIsolated)
            {
                throw new InvalidOperationException(
                    "Visual polish isolation failed: " + string.Join(", ", audit.ViolatingPaths));
            }
        }

        public static bool IsCanonical(Scene scene, Transform battlefield)
        {
            if (battlefield == null || !Audit(battlefield).IsIsolated || !LightingIsCanonical(scene) ||
                !VolumeIsCanonical() || !MaterialsAreCanonical()) return false;

            Transform art = battlefield.Find("Art Pass");
            bool terrain = TerrainDecorations.All(spec => DecorationMatches(art.Find(spec.Root)?.Find(spec.Name), spec)) &&
                TerrainDecorations.GroupBy(spec => spec.Root).All(group => art.Find(group.Key)?.childCount == group.Count());
            Transform curated = art.Find("Village Details/Curated Village Props");
            bool village = curated != null && curated.childCount == VillageDecorations.Length &&
                VillageDecorations.All(spec => DecorationMatches(curated.Find(spec.Name), spec));
            Transform distant = art.Find("Canyon Rim/Distant Scenery");
            bool rim = distant != null && distant.childCount == DistantDecorations.Length &&
                DistantDecorations.All(spec => DecorationMatches(distant.Find(spec.Name), spec));
            return terrain && village && rim;
        }

        public static void RemoveDistantScenery(Transform battlefield)
        {
            Transform distant = battlefield?.Find("Art Pass/Canyon Rim/Distant Scenery");
            if (distant == null)
            {
                throw new InvalidOperationException("The visual-polish Distant Scenery responsibility root is missing.");
            }

            DestroyUnlistedChildren(distant, Array.Empty<string>());
        }

        private static void ConfigureLightingAndAtmosphere(Scene scene)
        {
            Light[] directional = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                .Where(light => light.type == LightType.Directional).ToArray();
            if (directional.Length != 1 || RenderSettings.sun == null || RenderSettings.sun != directional[0])
            {
                throw new InvalidOperationException(
                    $"Expected exactly one authored RenderSettings sun, found {directional.Length} directional lights.");
            }

            Light sun = directional[0];
            sun.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
            sun.color = Html("#FFD39A");
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            sun.shadowBias = 0.08f;
            sun.shadowNormalBias = 0.45f;
            EditorUtility.SetDirty(sun);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Html("#C9B29A");
            RenderSettings.ambientEquatorColor = Html("#8F7665");
            RenderSettings.ambientGroundColor = Html("#4A352C");
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Html("#B6A18E");
            RenderSettings.fogStartDistance = 78f;
            RenderSettings.fogEndDistance = 170f;

            string skyPath = MaterialFolder + "/GoldenAfternoonSky.mat";
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null) throw new InvalidOperationException("Built-in procedural sky shader is unavailable.");
            if (sky == null)
            {
                sky = new Material(shader) { name = "GoldenAfternoonSky" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            else if (sky.shader != shader)
            {
                sky.shader = shader;
            }

            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", Html("#6FA9D2"));
            sky.SetColor("_GroundColor", Html("#6B4A36"));
            sky.SetFloat("_Exposure", 1.05f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();
        }

        private static void ConfigureVolumeProfile()
        {
            ScriptableObject profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(VolumeProfilePath);
            if (profile == null) throw new InvalidOperationException("DefaultVolumeProfile.asset is missing.");
            ConfigureVolumeComponent(profile, "Tonemapping", true, ("mode", 1));
            ConfigureVolumeComponent(profile, "WhiteBalance", true, ("temperature", 12f), ("tint", 2f));
            ConfigureVolumeComponent(profile, "ColorAdjustments", true, ("postExposure", 0.15f),
                ("contrast", 12f), ("saturation", -2f));
            ConfigureVolumeComponent(profile, "Bloom", true, ("threshold", 1.10f),
                ("intensity", 0.22f), ("scatter", 0.55f));
            ConfigureVolumeComponent(profile, "Vignette", true, ("intensity", 0.08f), ("smoothness", 0.35f));
            foreach (string disabled in new[] { "DepthOfField", "MotionBlur", "ChromaticAberration", "FilmGrain",
                         "LensDistortion", "ScreenSpaceLensFlare" })
                ConfigureVolumeComponent(profile, disabled, false);
            EditorUtility.SetDirty(profile);
        }

        private static void ConfigureVolumeComponent(ScriptableObject profile, string name, bool active,
            params (string Name, object Value)[] values)
        {
            UnityEngine.Object component = FindVolumeComponent(profile, name);
            if (component == null) throw new InvalidOperationException("Volume override is missing: " + name);
            SerializedObject serialized = new(component);
            SerializedProperty activeProperty = serialized.FindProperty("active");
            if (activeProperty == null) throw new InvalidOperationException(name + " has no active property.");
            activeProperty.boolValue = active;
            foreach ((string propertyName, object value) in values)
            {
                SerializedProperty overrideState = serialized.FindProperty(propertyName + ".m_OverrideState");
                SerializedProperty parameter = serialized.FindProperty(propertyName + ".m_Value");
                if (overrideState == null || parameter == null)
                    throw new InvalidOperationException($"Volume override {name}.{propertyName} is missing.");
                overrideState.boolValue = true;
                if (value is int integer) parameter.intValue = integer;
                else parameter.floatValue = Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
        }

        private static UnityEngine.Object FindVolumeComponent(ScriptableObject profile, string name)
        {
            SerializedProperty components = new SerializedObject(profile).FindProperty("components");
            if (components == null) return null;
            for (int index = 0; index < components.arraySize; index++)
            {
                UnityEngine.Object component = components.GetArrayElementAtIndex(index).objectReferenceValue;
                if (component != null && (component.name == name || component.GetType().Name == name)) return component;
            }
            return null;
        }

        private static void ConfigureStage8Materials()
        {
            ConfigureGround("CanyonRock", "#C97648", 2.8f, 52f, 0.34f, 0.04f, 0.30f);
            ConfigureGround("VillageSand", "#E7C58B", 3.8f, 58f, 0.20f, 0.04f, 0.28f);
            ConfigureGround("TropicalGrass", "#75975A", 3.2f, 54f, 0.30f, 0.04f, 0.25f);
            ConfigureGround("NeutralTropicalGround", "#B59165", 4.5f, 66f, 0.24f, 0.04f, 0.24f);
            ConfigureGround("RiverMud", "#8F775D", 3.4f, 44f, 0.28f, 0.06f, 0.42f);

            Material water = LoadMaterial("ShallowWaterTeal");
            RequireShader(water, "Dino Attack/Environment/Shallow Water");
            water.SetColor("_BaseColor", WithAlpha(Html("#238D8F"), 0.72f));
            water.SetColor("_DeepColor", WithAlpha(Html("#175B66"), 0.82f));
            water.SetColor("_FoamColor", WithAlpha(Html("#9BCDC1"), 0.52f));
            water.SetFloat("_NormalStrength", 0.18f);
            water.SetFloat("_TilingA", 3.8f);
            water.SetFloat("_TilingB", 5.3f);
            water.SetVector("_PanA", new Vector4(0.014f, 0.008f, 0f, 0f));
            water.SetVector("_PanB", new Vector4(-0.009f, 0.012f, 0f, 0f));
            water.SetFloat("_FoamWidth", 0.045f);
            water.SetFloat("_FresnelPower", 4.2f);
            water.SetFloat("_Smoothness", 0.74f);
            EditorUtility.SetDirty(water);
        }

        private static void ConfigureGround(string name, string color, float detail, float macro,
            float normal, float smoothnessMin, float smoothnessMax)
        {
            Material material = LoadMaterial(name);
            RequireShader(material, "Dino Attack/Environment/Stylized Ground");
            material.SetColor("_BaseColor", Html(color));
            material.SetFloat("_DetailScale", detail);
            material.SetFloat("_MacroScale", macro);
            material.SetFloat("_NormalStrength", normal);
            material.SetFloat("_SmoothnessMin", smoothnessMin);
            material.SetFloat("_SmoothnessMax", smoothnessMax);
            EditorUtility.SetDirty(material);
        }

        private static void BuildTerrainDecorations(Transform battlefield, Transform art)
        {
            ValidateTerrainDecorationSafety(battlefield);
            foreach (DecorationSpec spec in TerrainDecorations)
            {
                Transform root = art.Find(spec.Root);
                if (root == null) throw new InvalidOperationException("Missing decoration root: " + spec.Root);
                EnsureDecoration(root, spec);
            }
        }

        private static void BuildVillageDecorations(Transform art)
        {
            Transform villageDetails = art.Find("Village Details");
            if (villageDetails == null) throw new InvalidOperationException("Village Details is missing.");
            Transform obsolete = villageDetails.Find("Market Detail");
            if (obsolete != null) Undo.DestroyObjectImmediate(obsolete.gameObject);

            Transform curated = EnsureChild(villageDetails, "Curated Village Props");
            foreach (DecorationSpec spec in VillageDecorations)
            {
                Transform oldFlag = villageDetails.Find(spec.Name);
                if (oldFlag != null && oldFlag.parent != curated)
                {
                    Undo.SetTransformParent(oldFlag, curated, "Move existing village visual into curated props");
                }
                EnsureDecoration(curated, spec);
            }
            DestroyUnlistedChildren(curated, VillageDecorations.Select(spec => spec.Name));
        }

        private static void BuildDistantDecorations(Scene scene, Transform root)
        {
            foreach (DecorationSpec spec in DistantDecorations) EnsureDecoration(root, spec);
            DestroyUnlistedChildren(root, DistantDecorations.Select(spec => spec.Name));

            BoxCollider cameraBounds = scene.GetRootGameObjects()
                .SelectMany(value => value.GetComponentsInChildren<BoxCollider>(true))
                .FirstOrDefault(value => value.name == "Camera Bounds");
            DinoDeploymentZone[] zones = scene.GetRootGameObjects()
                .SelectMany(value => value.GetComponentsInChildren<DinoDeploymentZone>(true)).ToArray();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Vector3 center = renderer.bounds.center;
                if ((cameraBounds != null && ContainsXZ(cameraBounds.bounds, center)) || zones.Any(zone => zone.Contains(center)))
                {
                    throw new InvalidOperationException(
                        $"Distant silhouette renderer center is inside gameplay space: {HierarchyPath(renderer.transform)} at {center}");
                }
            }
        }

        private static GameObject EnsureDecoration(Transform parent, DecorationSpec spec)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            if (source == null) throw new InvalidOperationException("Missing approved decoration asset: " + spec.AssetPath);

            Transform found = parent.Find(spec.Name);
            GameObject instance = found == null ? null : found.gameObject;
            if (instance != null && !string.Equals(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance),
                    spec.AssetPath, StringComparison.Ordinal))
            {
                Undo.DestroyObjectImmediate(instance);
                instance = null;
            }
            if (instance == null)
            {
                instance = PrefabUtility.InstantiatePrefab(source, parent) as GameObject;
                if (instance == null) throw new InvalidOperationException("Could not instantiate " + spec.AssetPath);
                Undo.RegisterCreatedObjectUndo(instance, "Create deterministic battlefield decoration");
                instance.name = spec.Name;
            }

            if (instance.transform.parent != parent)
                Undo.SetTransformParent(instance.transform, parent, "Parent deterministic battlefield decoration");
            instance.transform.position = spec.Position;
            instance.transform.rotation = Quaternion.Euler(0f, spec.Yaw, 0f);
            instance.transform.localScale = spec.Scale;
            SetLayerRecursively(instance.transform, 0);
            SanitizeVisualHierarchy(instance.transform, spec.CastShadows);

            Material replacement = spec.MaterialPolicy == DecorationMaterialPolicy.Canyon ? LoadMaterial("CanyonRock") :
                spec.MaterialPolicy == DecorationMaterialPolicy.Grass ? LoadMaterial("TropicalGrass") : null;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (replacement != null)
                {
                    renderer.sharedMaterials = Enumerable.Repeat(replacement,
                        Math.Max(1, renderer.sharedMaterials.Length)).ToArray();
                }
                EditorUtility.SetDirty(renderer);
            }
            return instance;
        }

        public static void SanitizeVisualHierarchy(Transform root, bool castShadows)
        {
            foreach (Collider value in root.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(value);
            foreach (Rigidbody value in root.GetComponentsInChildren<Rigidbody>(true)) Undo.DestroyObjectImmediate(value);
            foreach (NavMeshAgent value in root.GetComponentsInChildren<NavMeshAgent>(true)) Undo.DestroyObjectImmediate(value);
            foreach (NavMeshObstacle value in root.GetComponentsInChildren<NavMeshObstacle>(true)) Undo.DestroyObjectImmediate(value);
            foreach (NavMeshSurface value in root.GetComponentsInChildren<NavMeshSurface>(true)) Undo.DestroyObjectImmediate(value);
            foreach (NavMeshModifier value in root.GetComponentsInChildren<NavMeshModifier>(true)) Undo.DestroyObjectImmediate(value);
            foreach (NavMeshModifierVolume value in root.GetComponentsInChildren<NavMeshModifierVolume>(true)) Undo.DestroyObjectImmediate(value);
            foreach (MonoBehaviour value in root.GetComponentsInChildren<MonoBehaviour>(true)) Undo.DestroyObjectImmediate(value);
            foreach (Animator value in root.GetComponentsInChildren<Animator>(true)) Undo.DestroyObjectImmediate(value);
            foreach (Animation value in root.GetComponentsInChildren<Animation>(true)) Undo.DestroyObjectImmediate(value);
            foreach (ParticleSystem value in root.GetComponentsInChildren<ParticleSystem>(true)) Undo.DestroyObjectImmediate(value);

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = 0;
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.BatchingStatic);
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }

            Component forbidden = root.GetComponentsInChildren<Component>(true).FirstOrDefault(component =>
                component is Collider || component is Rigidbody || component is NavMeshAgent ||
                component is NavMeshObstacle || component is NavMeshSurface || component is NavMeshModifier ||
                component is NavMeshModifierVolume || component is MonoBehaviour);
            if (forbidden != null)
            {
                throw new InvalidOperationException(
                    $"Visual sanitizer left forbidden component {forbidden.GetType().FullName} at {HierarchyPath(forbidden.transform)}");
            }
        }

        private static void ValidateTerrainDecorationSafety(Transform battlefield)
        {
            DinoDeploymentZone[] zones = battlefield.Find("Deployment Zones")
                ?.GetComponentsInChildren<DinoDeploymentZone>(true) ?? Array.Empty<DinoDeploymentZone>();
            DinoDeploymentZone z3 = zones.FirstOrDefault(zone => zone.name == "Z3");
            Transform[] frozenRoots = battlefield.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.name == "Guard Anchor" || transform.GetComponents<Component>().Any(component =>
                    component != null && (component.GetType().FullName == "LlamAcademy.Dinos.Unit.VillageHouse" ||
                                          component.GetType().FullName == "LlamAcademy.Dinos.Enemy.Defense.WallSpawnSlot")))
                .ToArray();

            foreach (DecorationSpec spec in TerrainDecorations)
            {
                string zoneName = spec.Root.Substring(spec.Root.LastIndexOf('/') + 1, 2);
                DinoDeploymentZone target = zones.FirstOrDefault(zone => zone.name == zoneName);
                if (target == null || !target.Contains(spec.Position))
                    throw new InvalidOperationException($"{spec.Name} is outside its responsibility zone {zoneName}.");
                if (z3 != null && z3.Contains(spec.Position))
                    throw new InvalidOperationException(spec.Name + " intrudes into the frozen Z3 water quadrilateral.");
                float boundaryDistance = zones.SelectMany(zone => SegmentDistances(spec.Position, zone.WorldVertices)).Min();
                if (boundaryDistance < 1.5f)
                    throw new InvalidOperationException($"{spec.Name} is only {boundaryDistance:F2}m from a deployment boundary.");
                float frozenDistance = frozenRoots.Length == 0 ? float.PositiveInfinity :
                    frozenRoots.Min(root => DistanceXZ(spec.Position, root.position));
                if (frozenDistance < 2.5f)
                    throw new InvalidOperationException($"{spec.Name} is only {frozenDistance:F2}m from a frozen gameplay root.");
            }
        }

        private static IEnumerable<float> SegmentDistances(Vector3 point, IReadOnlyList<Vector3> vertices)
        {
            for (int index = 0; index < vertices.Count; index++)
                yield return DistanceToSegmentXZ(point, vertices[index], vertices[(index + 1) % vertices.Count]);
        }

        private static float DistanceToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector2 p = new(point.x, point.z);
            Vector2 start = new(a.x, a.z);
            Vector2 segment = new(b.x - a.x, b.z - a.z);
            float denominator = segment.sqrMagnitude;
            float t = denominator <= Mathf.Epsilon ? 0f : Mathf.Clamp01(Vector2.Dot(p - start, segment) / denominator);
            return Vector2.Distance(p, start + segment * t);
        }

        private static float DistanceXZ(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        private static bool ContainsXZ(Bounds bounds, Vector3 point) => point.x >= bounds.min.x &&
            point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z;

        private static void DestroyUnlistedChildren(Transform parent, IEnumerable<string> retainedNames)
        {
            HashSet<string> retained = new(retainedNames, StringComparer.Ordinal);
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<GameObject> remove = new();
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform child = parent.GetChild(index);
                if (!retained.Contains(child.name) || !seen.Add(child.name)) remove.Add(child.gameObject);
            }

            foreach (GameObject child in remove) Undo.DestroyObjectImmediate(child);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        }

        private static bool DecorationMatches(Transform transform, DecorationSpec spec)
        {
            return transform != null && Approximately(transform.position, spec.Position) &&
                Approximately(transform.eulerAngles.y, Mathf.Repeat(spec.Yaw, 360f)) &&
                Approximately(transform.localScale, spec.Scale) &&
                string.Equals(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject), spec.AssetPath,
                    StringComparison.Ordinal) && transform.GetComponentsInChildren<Renderer>(true).Length > 0;
        }

        private static bool LightingIsCanonical(Scene scene)
        {
            Light[] directional = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true))
                .Where(light => light.type == LightType.Directional).ToArray();
            return directional.Length == 1 && RenderSettings.sun == directional[0] &&
                Approximately(directional[0].transform.eulerAngles, Quaternion.Euler(42f, -28f, 0f).eulerAngles) &&
                Approximately(directional[0].intensity, 1.25f) && directional[0].shadows == LightShadows.Soft &&
                RenderSettings.ambientMode == AmbientMode.Trilight && RenderSettings.fog &&
                Approximately(RenderSettings.ambientSkyColor, Html("#C9B29A")) &&
                Approximately(RenderSettings.ambientEquatorColor, Html("#8F7665")) &&
                Approximately(RenderSettings.ambientGroundColor, Html("#4A352C")) &&
                Approximately(RenderSettings.fogColor, Html("#B6A18E")) &&
                RenderSettings.fogMode == FogMode.Linear && Approximately(RenderSettings.fogStartDistance, 78f) &&
                Approximately(RenderSettings.fogEndDistance, 170f) &&
                AssetDatabase.GetAssetPath(RenderSettings.skybox) == MaterialFolder + "/GoldenAfternoonSky.mat";
        }

        private static bool VolumeIsCanonical()
        {
            ScriptableObject profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(VolumeProfilePath);
            return VolumeValueMatches(profile, "Tonemapping", "mode", 1) &&
                   VolumeValueMatches(profile, "WhiteBalance", "temperature", 12f) &&
                   VolumeValueMatches(profile, "ColorAdjustments", "contrast", 12f) &&
                   VolumeValueMatches(profile, "Bloom", "intensity", 0.22f) &&
                   VolumeValueMatches(profile, "Vignette", "intensity", 0.08f);
        }

        private static bool VolumeValueMatches(ScriptableObject profile, string componentName, string propertyName,
            float expected)
        {
            if (profile == null) return false;
            UnityEngine.Object component = FindVolumeComponent(profile, componentName);
            if (component == null) return false;
            SerializedObject serialized = new(component);
            SerializedProperty active = serialized.FindProperty("active");
            SerializedProperty value = serialized.FindProperty(propertyName + ".m_Value");
            return active != null && active.boolValue && value != null &&
                   (value.propertyType == SerializedPropertyType.Integer ||
                    value.propertyType == SerializedPropertyType.Enum
                       ? value.intValue == Mathf.RoundToInt(expected)
                       : Approximately(value.floatValue, expected));
        }

        private static bool MaterialsAreCanonical()
        {
            Material canyon = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/CanyonRock.mat");
            Material water = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/ShallowWaterTeal.mat");
            return canyon != null && water != null && Approximately(canyon.GetFloat("_MacroScale"), 52f) &&
                Approximately(canyon.GetFloat("_NormalStrength"), 0.34f) &&
                Approximately(water.GetFloat("_TilingA"), 3.8f) && Approximately(water.GetFloat("_FresnelPower"), 4.2f);
        }

        private static Material LoadMaterial(string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");
            if (material == null) throw new InvalidOperationException("Required Stage8 material is missing: " + name);
            return material;
        }

        private static void RequireShader(Material material, string shaderName)
        {
            if (material.shader == null || material.shader.name != shaderName)
                throw new InvalidOperationException($"{material.name} must retain shader {shaderName}.");
        }

        private static Color Html(string html)
        {
            if (!ColorUtility.TryParseHtmlString(html, out Color color))
                throw new InvalidOperationException("Invalid visual color constant: " + html);
            return color;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static bool Approximately(float a, float b) => Mathf.Abs(a - b) <= 0.0001f;
        private static bool Approximately(Vector3 a, Vector3 b) =>
            Approximately(a.x, b.x) && Approximately(a.y, b.y) && Approximately(a.z, b.z);
        private static bool Approximately(Color a, Color b) => Approximately(a.r, b.r) && Approximately(a.g, b.g) &&
            Approximately(a.b, b.b) && Approximately(a.a, b.a);

        public static VisualPolishAudit Audit(Transform battlefield)
        {
            VisualPolishAudit audit = new();
            Transform[] artRoots = battlefield == null ? Array.Empty<Transform>() : battlefield.Cast<Transform>()
                .Where(child => child.name == "Art Pass").ToArray();
            Transform art = artRoots.Length == 1 ? artRoots[0] : null;
            Transform z3 = art == null ? null : art.Find("Z3 Riverbank");
            audit.RequiredRootsPresent = art != null &&
                RequiredArtPaths.All(path => HasUniqueRelativePath(art, path));
            audit.Z3DecorationEmpty = z3 != null &&
                art.Cast<Transform>().Count(child => child.name == "Z3 Riverbank") == 1 && z3.childCount == 0;

            if (art == null)
            {
                audit.ViolatingPaths = new[] { artRoots.Length == 0 ? "Art Pass (missing)" : "Art Pass (duplicate roots)" };
                return audit;
            }

            List<string> violations = new();
            foreach (Transform transform in art.GetComponentsInChildren<Transform>(true))
            {
                string path = HierarchyPath(transform);
                foreach (Collider collider in transform.GetComponents<Collider>())
                {
                    if (collider.isTrigger)
                    {
                        audit.TriggerCount++;
                        violations.Add(path + " [Trigger]");
                    }
                    else
                    {
                        audit.ColliderCount++;
                        violations.Add(path + " [Collider]");
                    }
                }

                foreach (Rigidbody body in transform.GetComponents<Rigidbody>())
                {
                    audit.RigidbodyCount++;
                    violations.Add(path + " [Rigidbody]");
                }

                foreach (Component component in transform.GetComponents<Component>())
                {
                    if (component == null) continue;
                    Type type = component.GetType();
                    if (component is NavMeshAgent || component is NavMeshObstacle ||
                        component is NavMeshSurface || component is NavMeshModifier ||
                        component is NavMeshModifierVolume)
                    {
                        audit.NavMeshComponentCount++;
                        violations.Add(path + " [" + type.Name + "]");
                    }

                    if (IsMlAgent(type))
                    {
                        audit.AgentCount++;
                        violations.Add(path + " [Agent]");
                    }

                    if (component is MonoBehaviour && !IsAllowedPresentationBehaviour(type) && !IsMlAgent(type))
                    {
                        audit.GameplayBehaviourCount++;
                        violations.Add(path + " [" + type.Name + "]");
                    }
                }
            }

            if (!audit.RequiredRootsPresent) violations.Add("required visual responsibility roots are missing");
            if (!audit.Z3DecorationEmpty) violations.Add("Art Pass/Z3 Riverbank must remain empty");
            audit.ViolatingPaths = violations.Distinct(StringComparer.Ordinal).OrderBy(value => value,
                StringComparer.Ordinal).ToArray();
            return audit;
        }

        private static bool HasUniqueRelativePath(Transform root, string path)
        {
            Transform current = root;
            foreach (string segment in path.Split('/'))
            {
                Transform[] matches = current.Cast<Transform>().Where(child => child.name == segment).ToArray();
                if (matches.Length != 1) return false;
                current = matches[0];
            }
            return true;
        }

        public static string BuildFrozenGameplaySignature(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return string.Empty;
            }

            StringBuilder records = new();
            HashSet<Transform> selected = new();
            foreach (string path in FrozenHierarchyPaths)
            {
                Transform root = FindByPath(scene, path);
                if (root == null)
                {
                    Append(records, "MISSING", path);
                    continue;
                }
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true)) selected.Add(transform);
            }

            foreach (Component owner in scene.GetRootGameObjects()
                         .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                         .Where(component => component != null && FrozenOwnerTypeNames.Contains(
                             component.GetType().FullName, StringComparer.Ordinal)))
            {
                selected.Add(owner.transform);
            }
            foreach (NavMeshSurface surface in UnityEngine.Object.FindObjectsByType<NavMeshSurface>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (surface.gameObject.scene == scene) selected.Add(surface.transform);

            foreach (Transform transform in selected.OrderBy(HierarchyPath, StringComparer.Ordinal))
            {
                Append(records, "T", HierarchyPath(transform), transform.gameObject.activeSelf,
                    transform.gameObject.layer, transform.gameObject.tag,
                    Vector(transform.position), Vector(transform.eulerAngles), Vector(transform.lossyScale));

                foreach (Component component in transform.GetComponents<Component>()
                             .Where(component => component != null && !IsPresentationComponent(component))
                             .OrderBy(component => component.GetType().FullName, StringComparer.Ordinal))
                {
                    Append(records, "C", HierarchyPath(transform), component.GetType().FullName,
                        component is Behaviour behaviour ? behaviour.enabled : true);
                    AppendGameplayComponentState(records, component);
                }
            }

            VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml");
            VisualElement uiRoot = uxml == null ? null : uxml.CloneTree();
            foreach (string id in RuntimeUiBehavioralIds)
                Append(records, "UI", id, uiRoot != null && uiRoot.Q<VisualElement>(id) != null);
            Append(records, "PROTOCOL", "dino_attack_structured_set_v1", "continuous=4", "discrete=0",
                "streams=5", "shapes=3,6x5,8x7,8x6,10x7", "dinos=Velociraptor,Pachycephalosaurus,TRex");

            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(records.ToString()));
            }
            return BitConverter.ToString(digest).Replace("-", string.Empty);
        }

        private static void AppendGameplayComponentState(StringBuilder records, Component component)
        {
            switch (component)
            {
                case Collider collider:
                    Append(records, collider.enabled, collider.isTrigger, Vector(collider.bounds.center),
                        Vector(collider.bounds.size));
                    break;
                case NavMeshAgent agent:
                    Append(records, agent.agentTypeID, F(agent.radius), F(agent.height), F(agent.speed),
                        F(agent.angularSpeed), F(agent.acceleration));
                    break;
                case NavMeshSurface surface:
                    UnityEngine.Object navMeshData = new SerializedObject(surface)
                        .FindProperty("m_NavMeshData")?.objectReferenceValue;
                    Append(records, surface.agentTypeID, (int)surface.collectObjects,
                        surface.useGeometry.ToString(), StableObjectReference(navMeshData), ObjectJsonSha256(navMeshData));
                    break;
                case MonoBehaviour behaviour:
                    AppendSerializedPrimitives(records, behaviour);
                    break;
            }
        }

        private static void AppendSerializedPrimitives(StringBuilder records, MonoBehaviour behaviour)
        {
            SerializedObject serialized = new(behaviour);
            SerializedProperty iterator = serialized.GetIterator();
            while (iterator.NextVisible(true))
            {
                switch (iterator.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Enum:
                    case SerializedPropertyType.ArraySize:
                    case SerializedPropertyType.Character:
                        Append(records, iterator.propertyPath, iterator.longValue);
                        break;
                    case SerializedPropertyType.Boolean:
                        Append(records, iterator.propertyPath, iterator.boolValue);
                        break;
                    case SerializedPropertyType.Float:
                        Append(records, iterator.propertyPath, F(iterator.doubleValue));
                        break;
                    case SerializedPropertyType.String:
                        Append(records, iterator.propertyPath, iterator.stringValue);
                        break;
                    case SerializedPropertyType.Vector2:
                        Append(records, iterator.propertyPath, Vector(iterator.vector2Value));
                        break;
                    case SerializedPropertyType.Vector3:
                        Append(records, iterator.propertyPath, Vector(iterator.vector3Value));
                        break;
                    case SerializedPropertyType.ObjectReference:
                        Append(records, iterator.propertyPath, StableObjectReference(iterator.objectReferenceValue));
                        break;
                }
            }
        }

        private static string StableObjectReference(UnityEngine.Object value)
        {
            if (value == null) return "null";
            string assetPath = AssetDatabase.GetAssetPath(value);
            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(value);
            if (!string.IsNullOrEmpty(assetPath)) return assetPath + ":" + globalId + ":" + value.name;
            if (value is Component component) return globalId + ":" + HierarchyPath(component.transform) + ":" + component.GetType().FullName;
            if (value is GameObject gameObject) return globalId + ":" + HierarchyPath(gameObject.transform);
            return globalId + ":" + value.GetType().FullName + ":" + value.name;
        }

        private static string ObjectJsonSha256(UnityEngine.Object value)
        {
            if (value == null) return "null";
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(
                    Encoding.UTF8.GetBytes(EditorJsonUtility.ToJson(value)))).Replace("-", string.Empty);
            }
        }

        private static Transform FindByPath(Scene scene, string path)
        {
            string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            Transform current = scene.GetRootGameObjects().Select(root => root.transform)
                .FirstOrDefault(root => root.name == parts[0]);
            for (int index = 1; current != null && index < parts.Length; index++)
            {
                current = current.Find(parts[index]);
            }
            return current;
        }

        private static bool IsPresentationComponent(Component component)
        {
            string type = component.GetType().FullName;
            return component is Transform || component is Renderer || component is MeshFilter ||
                   component is Light || component is Camera || component is AudioSource ||
                   type == "UnityEngine.Rendering.Volume";
        }

        private static bool IsAllowedPresentationBehaviour(Type type) =>
            type.FullName == "UnityEngine.Rendering.Volume";

        private static bool IsMlAgent(Type type)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                if (current.FullName == "Unity.MLAgents.Agent") return true;
            }
            return false;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                GameObject gameObject = new(name);
                Undo.RegisterCreatedObjectUndo(gameObject, "Create visual polish responsibility root");
                child = gameObject.transform;
                Undo.SetTransformParent(child, parent, "Parent visual polish responsibility root");
            }

            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            child.gameObject.layer = 0;
            return child;
        }

        private static string HierarchyPath(Transform transform)
        {
            Stack<string> names = new();
            for (Transform current = transform; current != null; current = current.parent) names.Push(current.name);
            return "/" + string.Join("/", names);
        }

        private static string Vector(Vector2 value) => F(value.x) + "," + F(value.y);
        private static string Vector(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
        private static string F(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

        private static void Append(StringBuilder builder, params object[] values)
        {
            foreach (object value in values) builder.Append(value).Append('|');
            builder.AppendLine();
        }
    }
}
