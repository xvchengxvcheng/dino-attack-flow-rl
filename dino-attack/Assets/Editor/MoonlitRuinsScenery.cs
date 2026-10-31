using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Authors decorative scenery behind the north edge; never changes navigation or gameplay colliders.</summary>
public static class MoonlitRuinsScenery
{
    private const string Folder = "Assets/LlamAcademy/Dinos/Environment/MoonlitRuins";
    private const string Textures = "Assets/ThirdParty/PolyHaven/MoonlitRuins";
    private const string RootName = "Moonlit Observatory Ruins";

    [MenuItem("Tools/Dino Attack/Star Night/Replace North Mountains With Ruins")]
    public static void ApplyActive()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || Array.IndexOf(NightMapVisualTheme.Maps, scene.name) < 0)
            throw new InvalidOperationException("Open a supported battlefield in Edit Mode.");
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        ImportTextures();
        Material stone = StoneMaterial("Moonlit Limestone", "Rock", new Color(0.68f, 0.83f, 1f));
        Material masonry = StoneMaterial("Weathered Masonry", "Stone", new Color(0.64f, 0.77f, 0.94f));
        Material engraving = MaterialAsset("Faint Silver Inlay");
        engraving.color = new Color(0.24f, 0.38f, 0.46f);
        engraving.SetFloat("_Metallic", 0.35f);
        engraving.SetFloat("_Smoothness", 0.28f);
        engraving.EnableKeyword("_EMISSION");
        engraving.SetColor("_EmissionColor", new Color(0.10f, 0.22f, 0.30f) * 1.1f);
        EditorUtility.SetDirty(engraving);

        var root = new GameObject(RootName);
        var rng = new System.Random(73019);
        var blocks = new Geometry();
        var pillars = new Geometry();
        var rubble = new Geometry();
        var ring = new Geometry();
        var inlay = new Geometry();

        // A broken terrace anchors the ruins outside the playable north boundary (z = -53).
        for (int x = 0; x < 27; x++)
            for (int z = 0; z < 4; z++)
            {
                if (z == 3 && (x < 3 || x > 23) && rng.NextDouble() < 0.65f) continue;
                blocks.Block(new Vector3(-55 + x * 3.9f, -0.55f, -55.1f - z * 4f),
                    new Vector3(3.84f, 0.8f, 3.95f), Quaternion.identity, rng, 0.08f);
            }

        BuildWall(blocks, new Vector3(-35f, 0f, -62f), 24f, 6, rng);
        BuildWall(blocks, new Vector3(31f, 0f, -63f), 25f, 7, rng);
        BuildWall(blocks, new Vector3(-50f, 0f, -64f), 9f, 3, rng);
        BuildWall(blocks, new Vector3(43.5f, 0f, -64f), 9f, 3, rng);

        float[] xs = { -49f, -39f, -28f, -19f, 13f, 24f, 35f, 46f };
        float[] heights = { 5.2f, 10.4f, 8.2f, 5.4f, 7.5f, 11.8f, 8.5f, 5.2f };
        for (int i = 0; i < xs.Length; i++)
        {
            Vector3 p = new Vector3(xs[i], 0f, -57f - (i % 3) * 1.4f);
            BuildColumn(pillars, blocks, p, heights[i], rng);
            if (i == 1 || i == 5)
                blocks.Block(p + new Vector3(3f, heights[i] - 0.7f, 0f),
                    new Vector3(8f, 1.3f, 2.8f), Quaternion.Euler(0, 0, i == 1 ? -2f : 3f), rng, 0.14f);
        }

        // Unequal gaps and individually weathered voussoirs, rather than a smooth neon portal.
        Vector3 ringCenter = new Vector3(-3f, 6.6f, -58.5f);
        for (int i = 0; i < 40; i++)
        {
            if (i >= 5 && i <= 10 || i >= 24 && i <= 26) continue;
            float a = i * 9f + 0.35f;
            float b = (i + 1) * 9f - 0.35f;
            ring.ArcStone(ringCenter, 5.6f, 6.6f, a, b, 1.7f, rng);
            if (i % 3 == 0)
                inlay.ArcStone(ringCenter + Vector3.forward * 0.88f, 6.03f, 6.09f,
                    a + 1.5f, b - 1.5f, 0.035f, rng, 0f);
        }
        blocks.Block(new Vector3(-3f, 0.2f, -58.5f), new Vector3(16f, 1.4f, 5.5f), Quaternion.identity, rng, 0.10f);
        blocks.Block(new Vector3(-8.2f, 1.3f, -58.5f), new Vector3(2.8f, 2.6f, 2.8f), Quaternion.identity, rng, 0.08f);

        // Fallen column drums and chipped blocks remain on the decorative terrace.
        for (int i = 0; i < 90; i++)
        {
            float x = Range(rng, -54f, 47f);
            float z = Range(rng, -66f, -55.5f);
            if (z < -64.5f && (x < -45f || x > 37f)) z = -63f;
            Vector3 size = new Vector3(Range(rng, 0.45f, 2f), Range(rng, 0.35f, 1.2f), Range(rng, 0.5f, 1.8f));
            rubble.Block(new Vector3(x, size.y * 0.4f, z), size,
                Quaternion.Euler(Range(rng, -18, 18), Range(rng, 0, 360), Range(rng, -18, 18)), rng, 0.18f);
        }
        for (int i = 0; i < 7; i++)
        {
            Vector3 p = new Vector3(-44f + i * 14f, 1.1f, -57.4f - i % 2 * 3f);
            pillars.Drum(p, 0.95f, 2.8f, Quaternion.Euler(0f, i * 37f, 82f), rng, false);
        }

        MakeMesh(root.transform, "Terrace And Broken Walls", blocks, masonry);
        MakeMesh(root.transform, "Eroded Column Drums", pillars, stone);
        MakeMesh(root.transform, "Fallen Masonry", rubble, stone);
        MakeMesh(root.transform, "Broken Astronomical Ring", ring, stone);
        MakeMesh(root.transform, "Weathered Star Inlay", inlay, engraving);
        AddGroundTransition(root.transform, scene, stone);
        AddMoonlight(root.transform, new Vector3(-34f, 17f, -44f), new Vector3(-30f, 5f, -62f));
        AddMoonlight(root.transform, new Vector3(30f, 18f, -44f), new Vector3(25f, 5f, -62f));
        string prefabPath = Folder + "/Moonlit Observatory Ruins.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        // Validate all targets before changing the scene. These authored masses have no gameplay colliders.
        var mountains = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true))
            .Where(r => r.name == "Rim North West Mass" || r.name == "Rim North East Mass" || r.name == "Rim North Gate")
            .ToArray();
        if (mountains.Length == 0) throw new InvalidOperationException("North scenery was not found.");
        if (mountains.Any(r => r.GetComponentsInChildren<Collider>(true).Length != 0))
            throw new InvalidOperationException("A replacement target has colliders; keep gameplay geometry intact.");
        foreach (var mountain in mountains)
        {
            Undo.RecordObject(mountain, "Hide north mountain scenery");
            mountain.enabled = false;
        }
        var previous = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
        if (previous != null) Undo.DestroyObjectImmediate(previous);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
        Undo.RegisterCreatedObjectUndo(instance, "Create moonlit observatory ruins");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = instance;
    }

    private static void ImportTextures()
    {
        foreach (string path in Directory.GetFiles(Textures, "*.jpg"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            bool normal = path.Contains("Normal");
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool srgb = !normal && !path.Contains("Roughness");
            if (importer.textureType == type && importer.sRGBTexture == srgb && importer.maxTextureSize == 1024 &&
                importer.wrapMode == TextureWrapMode.Repeat && importer.anisoLevel == 4) continue;
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = 1024;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
    }

    private static void AddGroundTransition(Transform parent, Scene scene, Material stone)
    {
        var ground = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true))
            .Single(r => r.name == "Neutral Ground");
        var material = MaterialAsset("Weathered Ground Transition");
        material.shader = Shader.Find("Dino Attack/Environment/Ruins Ground Transition");
        if (material.shader == null || !material.shader.isSupported)
            throw new InvalidOperationException("Ground transition shader is unavailable.");
        // Preserve the exact original soil shading where the blend reaches zero.
        Material soil = ground.sharedMaterial;
        material.SetColor("_BaseColor", soil.GetColor("_BaseColor"));
        foreach (string property in new[] { "_BaseMap", "_NormalMap", "_SurfaceMap" })
            material.SetTexture(property, soil.GetTexture(property));
        foreach (string property in new[] { "_DetailScale", "_MacroScale", "_NormalStrength", "_SurfaceMapIsSpecular",
                     "_SmoothnessMin", "_SmoothnessMax" })
            material.SetFloat(property, soil.GetFloat(property));
        material.renderQueue = soil.renderQueue;
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "/Stone Albedo.jpg");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "/Stone Normal.jpg");
        if (albedo == null || normal == null) throw new InvalidOperationException("Stone textures have not finished importing.");
        material.SetTexture("_StoneMap", albedo);
        material.SetTexture("_StoneNormal", normal);
        material.SetColor("_StoneTint", new Color(0.59f, 0.61f, 0.63f));
        material.SetFloat("_BlendNear", -35f);
        material.SetFloat("_BlendFar", -64f);
        material.SetFloat("_StoneScale", 4f);
        EditorUtility.SetDirty(material);

        var surface = new Geometry();
        Bounds bounds = ground.bounds;
        // A thin visual surface connects the existing ground to the sunken terrace slabs.
        // It has no collider and sits beneath existing gameplay props and terrain patches.
        surface.Ground(new Vector3(bounds.min.x, bounds.max.y + 0.008f, -69.1f),
            new Vector3(bounds.max.x, bounds.max.y + 0.008f, bounds.max.z));
        MakeMesh(parent, "Eroded Soil And Stone Transition", surface, material);
        var renderer = parent.Find("Eroded Soil And Stone Transition").GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        var fragments = new Geometry();
        var rng = new System.Random(80411);
        for (int i = 0; i < 70; i++)
        {
            float x = Range(rng, bounds.min.x + 2f, bounds.max.x - 2f);
            float z = Range(rng, -55f, -44f);
            Vector3 size = new Vector3(Range(rng, 0.25f, 1.1f), Range(rng, 0.035f, 0.12f), Range(rng, 0.3f, 1.3f));
            fragments.Block(new Vector3(x, 0.01f, z), size,
                Quaternion.Euler(0, Range(rng, 0, 360), 0), rng, 0.04f);
        }
        MakeMesh(parent, "Half Buried Boundary Fragments", fragments, stone);
    }

    private static Material MaterialAsset(string name)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Material StoneMaterial(string name, string texture, Color color)
    {
        Material material = MaterialAsset(name);
        material.color = color;
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "/" + texture + " Albedo.jpg"));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "/" + texture + " Normal.jpg"));
        material.SetFloat("_BumpScale", 0.8f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.12f);
        material.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void BuildWall(Geometry mesh, Vector3 center, float width, int rows, System.Random rng)
    {
        const float step = 2.35f;
        int count = Mathf.RoundToInt(width / step);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < count; col++)
            {
                float edge = Mathf.Abs((col + 0.5f) / count * 2f - 1f);
                int top = Mathf.RoundToInt(rows * (1f - 0.6f * edge));
                if (row > top || row == top && rng.NextDouble() < 0.3) continue;
                mesh.Block(center + new Vector3((col - count / 2f) * step + row % 2 * 0.5f,
                    row * 1.05f + 0.5f, Range(rng, -0.13f, 0.13f)),
                    new Vector3(step - 0.055f, 1f, 1.8f), Quaternion.Euler(0, Range(rng, -1.5f, 1.5f), 0), rng, 0.12f);
            }
    }

    private static void BuildColumn(Geometry stone, Geometry bases, Vector3 p, float height, System.Random rng)
    {
        bases.Block(p + Vector3.up * 0.35f, new Vector3(3.8f, 0.7f, 3.8f), Quaternion.identity, rng, 0.09f);
        bases.Block(p + Vector3.up * 0.9f, new Vector3(3.1f, 0.4f, 3.1f), Quaternion.identity, rng, 0.07f);
        float y = 1.1f;
        while (y < height)
        {
            float h = Mathf.Min(1.75f, height - y);
            bool broken = y + h >= height - 0.01f;
            stone.Drum(p + Vector3.up * y, Mathf.Lerp(1.18f, 0.95f, y / height), h - 0.045f,
                Quaternion.identity, rng, broken);
            y += h;
        }
    }

    private static void MakeMesh(Transform parent, string name, Geometry geometry, Material material)
    {
        Mesh mesh = geometry.Mesh(name);
        string path = Folder + "/" + name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) AssetDatabase.CreateAsset(mesh, path);
        else
        {
            // Use mesh setters so a repeated authoring pass also refreshes native vertex buffers.
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.vertices = mesh.vertices;
            existing.uv = mesh.uv;
            existing.triangles = mesh.triangles;
            existing.normals = mesh.normals;
            existing.tangents = mesh.tangents;
            existing.bounds = mesh.bounds;
            UnityEngine.Object.DestroyImmediate(mesh);
            mesh = existing;
        }
        mesh.UploadMeshData(false);
        EditorUtility.SetDirty(mesh);
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        go.GetComponent<MeshRenderer>().receiveShadows = true;
        go.isStatic = true;
    }

    private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    private static void AddMoonlight(Transform parent, Vector3 position, Vector3 target)
    {
        var go = new GameObject("Ruins Moonlight Fill", typeof(Light));
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.LookAt(target);
        var light = go.GetComponent<Light>();
        light.type = LightType.Spot;
        light.color = new Color(0.72f, 0.84f, 1f);
        light.intensity = 65f;
        light.range = 42f;
        light.spotAngle = 95f;
        light.innerSpotAngle = 55f;
        light.shadows = LightShadows.None;
    }

    private sealed class Geometry
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector2> uv = new();
        private readonly List<int> triangles = new();

        public void Ground(Vector3 min, Vector3 max) => Quad(min,
            new Vector3(min.x, min.y, max.z), max, new Vector3(max.x, max.y, min.z));

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            Vector3 normal = Vector3.Cross(b - a, d - a).normalized;
            Vector2 Project(Vector3 p) => Mathf.Abs(normal.y) > Mathf.Max(Mathf.Abs(normal.x), Mathf.Abs(normal.z))
                ? new Vector2(p.x, p.z) / 3f : Mathf.Abs(normal.z) > Mathf.Abs(normal.x)
                    ? new Vector2(p.x, p.y) / 3f : new Vector2(p.z, p.y) / 3f;
            uv.AddRange(new[] { Project(a), Project(b), Project(c), Project(d) });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        public void Block(Vector3 center, Vector3 size, Quaternion rotation, System.Random rng, float chip)
        {
            // Four octagonal rings create eroded bevels rather than perfect cube edges.
            float halfX = size.x * 0.5f, halfZ = size.z * 0.5f;
            float bevel = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.13f;
            Vector3[,] v = new Vector3[4, 8];
            for (int row = 0; row < 4; row++)
            {
                float inset = row == 0 || row == 3 ? bevel : 0f;
                float y = row == 0 ? -size.y * 0.5f : row == 1 ? -size.y * 0.5f + bevel :
                    row == 2 ? size.y * 0.5f - bevel : size.y * 0.5f;
                float x = halfX - inset, z = halfZ - inset;
                Vector2[] corners = { new(x - bevel, -z), new(x, -z + bevel), new(x, z - bevel), new(x - bevel, z),
                    new(-x + bevel, z), new(-x, z - bevel), new(-x, -z + bevel), new(-x + bevel, -z) };
                for (int i = 0; i < 8; i++)
                    v[row, i] = center + rotation * (new Vector3(corners[i].x, y, corners[i].y) +
                        new Vector3(Range(rng, -chip, chip), Range(rng, -chip, chip) * 0.3f, Range(rng, -chip, chip)));
            }
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                for (int row = 0; row < 3; row++) Quad(v[row, i], v[row + 1, i], v[row + 1, next], v[row, next]);
                Quad(v[3, i], center + rotation * Vector3.up * (size.y * 0.5f),
                    center + rotation * Vector3.up * (size.y * 0.5f), v[3, next]);
                Quad(v[0, next], center - rotation * Vector3.up * (size.y * 0.5f),
                    center - rotation * Vector3.up * (size.y * 0.5f), v[0, i]);
            }
        }

        public void Drum(Vector3 bottom, float radius, float height, Quaternion rotation, System.Random rng, bool broken)
        {
            const int n = 48;
            var lower = new Vector3[n]; var upper = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                float r = radius * (i % 3 == 1 ? 0.92f : 1f) + Range(rng, -0.035f, 0.035f);
                lower[i] = bottom + rotation * new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                upper[i] = bottom + rotation * new Vector3(Mathf.Cos(a) * r * 0.985f,
                    height + (broken ? Range(rng, -0.65f, 0.12f) : Range(rng, -0.025f, 0.025f)), Mathf.Sin(a) * r * 0.985f);
            }
            Vector3 top = bottom + rotation * Vector3.up * (height - (broken ? 0.25f : 0f));
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                Quad(lower[i], upper[i], upper[next], lower[next]);
                Quad(upper[i], top, top, upper[next]);
                Quad(lower[next], bottom, bottom, lower[i]);
            }
        }

        public void ArcStone(Vector3 center, float inner, float outer, float from, float to,
            float depth, System.Random rng, float chip = 0.045f)
        {
            Vector3 Point(float radius, float angle, float z) => center + new Vector3(
                Mathf.Cos(angle * Mathf.Deg2Rad) * radius + Range(rng, -chip, chip),
                Mathf.Sin(angle * Mathf.Deg2Rad) * radius + Range(rng, -chip, chip), z);
            Vector3 a = Point(inner, from, -depth / 2), b = Point(outer, from, -depth / 2);
            Vector3 c = Point(outer, to, -depth / 2), d = Point(inner, to, -depth / 2);
            Vector3 e = Point(inner, from, depth / 2), f = Point(outer, from, depth / 2);
            Vector3 g = Point(outer, to, depth / 2), h = Point(inner, to, depth / 2);
            Quad(a, d, c, b); Quad(e, f, g, h); Quad(a, b, f, e);
            Quad(d, h, g, c); Quad(b, c, g, f); Quad(a, e, h, d);
        }

        public Mesh Mesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
