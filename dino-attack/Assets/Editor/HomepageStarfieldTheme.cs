using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Scene-only starfield styling; no runtime generation or UI event changes.</summary>
public static class HomepageStarfieldTheme
{
    private const string ScenePath = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";
    private const string TexturePath = "Assets/LlamAcademy/Dinos/UI/Textures/Homepage Starfield.png";
    private const string ButtonStarsPath = "Assets/LlamAcademy/Dinos/UI/Textures/Homepage Button Stars.png";

    [MenuItem("Tools/Dino Attack/Apply Homepage Starfield Theme")]
    public static void Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != ScenePath)
            throw new InvalidOperationException("Open MapSelect in Edit Mode before applying the theme.");

        Transform root = GameObject.Find("Map Select").transform;
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Apply homepage starfield theme");
        Sprite backdrop = GenerateBackdrop();
        var background = root.Find("Homepage Backdrop").GetComponent<Image>();
        background.sprite = backdrop;
        background.color = Color.white;
        background.raycastTarget = false;
        Tint(root, "Homepage Atmosphere", new Color(0.015f, 0.02f, 0.09f, 0.08f));
        Tint(root, "Homepage Hero Panel", new Color(0.035f, 0.04f, 0.12f, 0.70f));
        Tint(root, "Homepage Horizon", new Color(0.45f, 0.55f, 1f, 0.45f));
        Tint(root, "Homepage Cyan Rail", new Color(0.36f, 0.77f, 1f, 0.55f));
        Tint(root, "Homepage Amber Rail", new Color(0.67f, 0.48f, 1f, 0.55f));
        foreach (var outline in root.GetComponentsInChildren<Outline>(true))
            outline.effectColor = new Color(0.075f, 0.09f, 0.21f, 0.55f);

        foreach (var text in root.GetComponentsInChildren<Text>(true))
            text.color = new Color(0.89f, 0.93f, 1f, 1f);
        TextColor(root, "Homepage Status", new Color(0.55f, 0.80f, 1f));
        TextColor(root, "Homepage Eyebrow", new Color(0.72f, 0.65f, 1f));
        TextColor(root, "Homepage Footer", new Color(0.53f, 0.61f, 0.80f));
        TextColor(root, "Selection Status", new Color(0.66f, 0.78f, 1f));

        Sprite buttonStars = GenerateButtonStars();
        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            bool accent = button.name == "Dinosaur Introduction";
            if (button.targetGraphic != null) button.targetGraphic.color = Color.white;
            var colors = button.colors;
            colors.normalColor = accent ? new Color(0.27f, 0.24f, 0.36f, 1f) : new Color(0.16f, 0.26f, 0.39f, 1f);
            colors.highlightedColor = new Color(0.24f, 0.36f, 0.49f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.12f, 0.23f, 0.40f, 1f);
            colors.disabledColor = new Color(0.12f, 0.14f, 0.21f, 0.65f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.16f;
            button.colors = colors;
            var outline = button.GetComponent<Outline>();
            if (outline != null) outline.effectColor = accent
                ? new Color(0.71f, 0.53f, 1f, 0.85f)
                : new Color(0.40f, 0.64f, 1f, 0.65f);
            AddButtonBorder(button, accent ? new Color(0.63f, 0.59f, 0.78f, 0.85f) : new Color(0.45f, 0.62f, 0.78f, 0.85f));
            AddButtonStars(button, buttonStars, accent ? new Color(0.90f, 0.80f, 1f) : new Color(0.72f, 0.89f, 1f));
            foreach (var label in button.GetComponentsInChildren<Text>(true)) label.color = Color.white;
        }

        Tint(root, "Dinosaur Introduction Panel", new Color(0.018f, 0.025f, 0.075f, 0.98f));
        foreach (string card in new[] { "Pachycephalosaurus Card", "Velociraptor Card", "TRex Card" })
            Tint(root, "Dinosaur Introduction Panel/" + card, new Color(0.065f, 0.075f, 0.19f, 0.96f));
        HomepageCharacterGuide.ApplyTo(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Homepage starfield theme saved to MapSelect.");
    }

    private static void AddButtonBorder(Button button, Color color)
    {
        // Separate graphics keep the border bright when Selectable tints its target image.
        for (int edge = 0; edge < 4; edge++)
        {
            string name = "Starfield Border " + edge;
            Transform existing = button.transform.Find(name);
            var go = existing != null ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (existing == null) Undo.RegisterCreatedObjectUndo(go, "Create starfield button border");
            go.layer = button.gameObject.layer;
            go.transform.SetParent(button.transform, false);
            var image = go.GetComponent<Image>();
            Color edgeColor = color;
            edgeColor.a *= edge == 1 ? 1f : 0.50f;
            image.color = edgeColor;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            bool horizontal = edge < 2;
            float side = edge % 2;
            rect.anchorMin = horizontal ? new Vector2(0f, side) : new Vector2(side, 0f);
            rect.anchorMax = horizontal ? new Vector2(1f, side) : new Vector2(side, 1f);
            rect.pivot = horizontal ? new Vector2(0.5f, side) : new Vector2(side, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = horizontal ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
        }
    }

    private static void AddButtonStars(Button button, Sprite sprite, Color color)
    {
        Transform existing = button.transform.Find("Starfield Sparkles");
        var go = existing != null ? existing.gameObject
            : new GameObject("Starfield Sparkles", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (existing == null) Undo.RegisterCreatedObjectUndo(go, "Create button stars");
        go.layer = button.gameObject.layer;
        go.transform.SetParent(button.transform, false);
        go.transform.SetAsFirstSibling();
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(5f, 5f);
        rect.offsetMax = new Vector2(-5f, -5f);
    }

    private static Sprite GenerateButtonStars()
    {
        const int width = 600, height = 180;
        var pixels = new Color[width * height];
        var random = new System.Random(871);
        // A quiet surface reflection and a few distant pinpoints replace decorative star icons.
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float u = (float)x / width, v = (float)y / height;
            float sheen = Mathf.Pow(v, 3f) * 0.075f;
            float haze = Mathf.Exp(-Mathf.Pow((u - 0.84f) * 4f, 2f) - Mathf.Pow((v - 0.70f) * 3f, 2f)) * 0.045f;
            pixels[y * width + x] = new Color(0.75f, 0.85f, 1f, sheen + haze);
        }
        for (int i = 0; i < 12; i++)
        {
            int x = random.Next(14, width - 14), y = random.Next(10, height - 10);
            // Keep the label area clear, with most small stars close to the edges.
            if (x > 110 && x < 490 && y > 38 && y < 142) continue;
            PaintSparkle(pixels, width, height, x, y, 0.8f + (float)random.NextDouble() * 0.5f, 0.24f);
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(ButtonStarsPath, texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        AssetDatabase.ImportAsset(ButtonStarsPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(ButtonStarsPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(ButtonStarsPath);
    }

    private static void PaintSparkle(Color[] pixels, int width, int height, int cx, int cy, float radius, float opacity)
    {
        int extent = Mathf.CeilToInt(radius * 2f);
        for (int y = -extent; y <= extent; y++)
        for (int x = -extent; x <= extent; x++)
        {
            int px = cx + x, py = cy + y;
            if (px < 0 || px >= width || py < 0 || py >= height) continue;
            float distance = (x * x + y * y) / (radius * radius);
            float alpha = Mathf.Exp(-distance * 2f) * opacity;
            int index = py * width + px;
            pixels[index] = new Color(1f, 1f, 1f, Mathf.Max(pixels[index].a, alpha));
        }
    }

    private static void Tint(Transform root, string path, Color color)
    {
        var target = root.Find(path);
        if (target != null && target.TryGetComponent<Image>(out var image)) image.color = color;
    }

    private static void TextColor(Transform root, string path, Color color)
    {
        var target = root.Find(path);
        if (target != null && target.TryGetComponent<Text>(out var text)) text.color = color;
    }

    private static Sprite GenerateBackdrop()
    {
        const int width = 1920, height = 1080;
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float u = (float)x / width, v = (float)y / height;
            float wave = v - (0.18f + u * 0.65f + 0.045f * Mathf.Sin(u * 9f));
            float mist = Mathf.Exp(-wave * wave * 34f);
            float noise = Mathf.PerlinNoise(u * 5f + 12.7f, v * 7f + 4.3f);
            float detail = Mathf.PerlinNoise(u * 19f + 3f, v * 24f + 8f);
            float violet = Mathf.Exp(-((u - 0.73f) * (u - 0.73f) * 7f + (v - 0.65f) * (v - 0.65f) * 12f));
            float blue = Mathf.Exp(-((u - 0.15f) * (u - 0.15f) * 10f + (v - 0.33f) * (v - 0.33f) * 8f));
            float cloud = mist * (0.25f + noise * 0.75f) * (0.6f + detail * 0.4f);
            float vignette = Mathf.Clamp01(1f - 0.5f * (Mathf.Pow((u - 0.5f) * 1.4f, 2) + Mathf.Pow((v - 0.5f) * 1.4f, 2)));
            pixels[y * width + x] = new Color(
                (0.018f + cloud * 0.065f + violet * 0.045f) * vignette,
                (0.026f + cloud * 0.07f + blue * 0.035f) * vignette,
                (0.075f + cloud * 0.18f + violet * 0.075f + blue * 0.08f) * vignette, 1f);
        }

        var random = new System.Random(73129);
        for (int i = 0; i < 1150; i++)
        {
            int x = random.Next(8, width - 8), y = random.Next(8, height - 8);
            float brightness = 0.20f + (float)random.NextDouble() * 0.65f;
            Color color = Color.Lerp(new Color(0.49f, 0.69f, 1f), new Color(0.91f, 0.85f, 1f), (float)random.NextDouble());
            Star(pixels, width, height, x, y, i % 23 == 0 ? 2.2f : 0.65f, color * brightness);
        }
        // Quiet constellation motifs frame the existing menu, away from labels.
        Vector2[] left = { new Vector2(135, 620), new Vector2(210, 745), new Vector2(310, 715), new Vector2(365, 865), new Vector2(245, 935) };
        Vector2[] right = { new Vector2(1500, 840), new Vector2(1620, 900), new Vector2(1740, 810), new Vector2(1675, 685), new Vector2(1790, 600) };
        foreach (var points in new[] { left, right })
        {
            for (int i = 1; i < points.Length; i++)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(points[i - 1], points[i]));
                for (int j = 0; j <= steps; j++)
                {
                    Vector2 p = Vector2.Lerp(points[i - 1], points[i], (float)j / steps);
                    int index = Mathf.RoundToInt(p.y) * width + Mathf.RoundToInt(p.x);
                    pixels[index] = Color.Lerp(pixels[index], new Color(0.4f, 0.58f, 0.95f), 0.24f);
                }
            }
            foreach (var p in points) Star(pixels, width, height, (int)p.x, (int)p.y, 2.5f, new Color(0.64f, 0.79f, 1f));
        }

        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
    }

    private static void Star(Color[] pixels, int width, int height, int cx, int cy, float radius, Color color)
    {
        int extent = Mathf.CeilToInt(radius * 4f);
        for (int y = -extent; y <= extent; y++)
        for (int x = -extent; x <= extent; x++)
        {
            int px = cx + x, py = cy + y;
            if (px < 0 || py < 0 || px >= width || py >= height) continue;
            float distance = x * x + y * y;
            float glow = Mathf.Exp(-distance / (radius * radius * 0.65f)) + 0.10f * Mathf.Exp(-distance / (radius * radius * 5f));
            if (radius > 2f && (x == 0 || y == 0)) glow += 0.2f * Mathf.Exp(-Mathf.Sqrt(distance) / radius / 1.8f);
            int index = py * width + px;
            pixels[index] += color * glow;
            pixels[index].a = 1f;
        }
    }
}
