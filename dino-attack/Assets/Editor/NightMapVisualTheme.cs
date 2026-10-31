using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Authors presentation assets only; terrain, navigation and game rules are retained.</summary>
public static class NightMapVisualTheme
{
    public const string Folder = "Assets/LlamAcademy/Dinos/Environment/StarNight";
    public static readonly string[] Maps = { "Dinos", "LayeredBattlefield", "DefenseBattlefield", "FirstPersonDefenseBattlefield", "JevVillageDemo" };

    [MenuItem("Tools/Dino Attack/Star Night/Apply To Active Map")]
    public static void ApplyActive()
    {
        Scene scene = SceneManager.GetActiveScene();
        int index = Array.IndexOf(Maps, scene.name);
        if (EditorApplication.isPlaying || index < 0)
            throw new InvalidOperationException("Open a supported battlefield in Edit Mode.");
        EnsureFolder(Folder);
        Material sky = Sky(index);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.53f, 0.61f, 0.78f);
        RenderSettings.ambientEquatorColor = new Color(0.39f, 0.46f, 0.60f);
        RenderSettings.ambientGroundColor = new Color(0.24f, 0.29f, 0.40f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 0.45f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.045f, 0.065f, 0.12f);
        // Keep the entire playable area visible from the overhead strategy camera.
        RenderSettings.fogStartDistance = 100f;
        RenderSettings.fogEndDistance = index == 4 ? 290f : 260f;

        foreach (var light in InScene<Light>(scene))
        {
            if (light.type != LightType.Directional) continue;
            Undo.RecordObjects(new UnityEngine.Object[] { light, light.transform }, "Apply moonlight");
            light.color = new Color(0.78f, 0.86f, 1f);
            light.intensity = index == 1 ? 1.35f : 1.25f;
            light.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.42f;
            RenderSettings.sun = light;
        }
        foreach (var camera in InScene<Camera>(scene))
        {
            Undo.RecordObject(camera, "Apply night camera background");
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = RenderSettings.fogColor;
        }
        VolumeProfile profile = NightProfile();
        foreach (var volume in InScene<Volume>(scene))
        {
            Undo.RecordObject(volume, "Assign night grading");
            volume.sharedProfile = profile;
        }
        AddVillageLighting(scene, index);
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Star Night presentation saved: " + scene.name);
    }

    private static T[] InScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

    private static Material Sky(int index)
    {
        Shader shader = Shader.Find("DinoAttack/Skybox/StarNight");
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("Star Night sky shader is unavailable.");
        string path = Folder + "/" + Maps[index] + " Sky.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = Maps[index] + " Star Night Sky" };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_ZenithColor", new Color(0.012f, 0.018f, 0.055f));
        material.SetColor("_HorizonColor", index == 4 ? new Color(0.10f, 0.10f, 0.18f) : new Color(0.065f, 0.085f, 0.15f));
        material.SetColor("_NebulaColor", index == 3 ? new Color(0.06f, 0.035f, 0.10f) : new Color(0.035f, 0.038f, 0.075f));
        material.SetFloat("_StarIntensity", index == 3 ? 1.1f : 0.85f);
        material.SetFloat("_Rotation", index * 23f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static VolumeProfile NightProfile()
    {
        string path = Folder + "/Star Night Grading.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        var tone = Override<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.Neutral);
        var balance = Override<WhiteBalance>(profile);
        balance.temperature.Override(-8f);
        balance.tint.Override(0f);
        var color = Override<ColorAdjustments>(profile);
        color.postExposure.Override(0.70f);
        color.contrast.Override(5f);
        color.saturation.Override(-18f);
        var bloom = Override<Bloom>(profile);
        bloom.threshold.Override(1.2f);
        bloom.intensity.Override(0.12f);
        bloom.tint.Override(new Color(0.75f, 0.84f, 1f));
        var vignette = Override<Vignette>(profile);
        vignette.intensity.Override(0.10f);
        vignette.smoothness.Override(0.40f);
        EditorUtility.SetDirty(profile);
        foreach (var component in profile.components) EditorUtility.SetDirty(component);
        return profile;
    }

    private static T Override<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var component)) return component;
        component = profile.Add<T>(false);
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    private static void AddVillageLighting(Scene scene, int map)
    {
        Transform parent = InScene<Transform>(scene).FirstOrDefault(t => t.name == "Lighting & Atmosphere");
        Transform existing = parent != null ? parent.Find("Star Night Village Lights") : null;
        var root = existing != null ? existing.gameObject : new GameObject("Star Night Village Lights");
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(root, "Create night village lighting");
            if (parent != null) root.transform.SetParent(parent, false);
        }
        // Lighting is decorative and has no colliders or gameplay components.
        var houses = InScene<MonoBehaviour>(scene).Where(c => c != null && c.GetType().Name == "VillageHouse" && c.gameObject.activeInHierarchy)
            .OrderBy(c => c.transform.position.x).ToArray();
        var positions = new System.Collections.Generic.List<Vector3>();
        foreach (var house in houses)
        {
            var renderer = house.GetComponentInChildren<Renderer>();
            Vector3 center = renderer != null ? renderer.bounds.center : house.transform.position;
            center.y += 2.5f;
            if (positions.Any(p => Vector3.Distance(p, center) < 14f)) continue;
            positions.Add(center);
            if (positions.Count == 4) break;
        }
        for (int i = 0; i < positions.Count; i++)
        {
            string name = "Village Warm Pool " + (i + 1);
            Transform child = root.transform.Find(name);
            var go = child != null ? child.gameObject : new GameObject(name, typeof(Light));
            if (child == null) Undo.RegisterCreatedObjectUndo(go, "Create village warm light");
            go.transform.SetParent(root.transform, false);
            go.transform.position = positions[i];
            var light = go.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.65f, 0.35f);
            light.intensity = map == 4 ? 3.5f : 2.8f;
            light.range = 15f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
