using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class HomepageVisualPolish
{
    private const string ScenePath = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";
    private const string BackdropPath = "Assets/LlamAcademy/Dinos/UI/Textures/Homepage Backdrop.png";

    [MenuItem("Tools/Dino Attack/Apply Homepage Visual Polish")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject canvasObject = GameObject.Find("Map Select");
        if (canvasObject == null)
            throw new System.InvalidOperationException("Map Select canvas was not found.");

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        Transform root = canvas.transform;
        Sprite backdrop = AssetDatabase.LoadAssetAtPath<Sprite>(BackdropPath);
        if (backdrop == null)
            throw new System.InvalidOperationException("Homepage Backdrop sprite was not imported.");

        Image background = EnsureImage(root, "Homepage Backdrop", 0);
        background.sprite = backdrop;
        background.color = Color.white;
        background.raycastTarget = false;
        Stretch(background.rectTransform);
        background.rectTransform.SetAsFirstSibling();

        Image wash = EnsureImage(root, "Homepage Atmosphere", 1);
        wash.sprite = null;
        wash.color = new Color(0.01f, 0.02f, 0.04f, 0.12f);
        wash.raycastTarget = false;
        Stretch(wash.rectTransform);
        wash.rectTransform.SetSiblingIndex(1);

        Image heroPanel = EnsureImage(root, "Homepage Hero Panel", 2);
        heroPanel.sprite = null;
        heroPanel.color = new Color(0.012f, 0.032f, 0.055f, 0.90f);
        heroPanel.raycastTarget = false;
        SetCentered(heroPanel.rectTransform, new Vector2(0f, 170f), new Vector2(610f, 570f));
        AddOutline(heroPanel.gameObject, new Color(0.22f, 0.80f, 0.82f, 0.48f), 2f);

        CreateBar(root, "Homepage Cyan Rail", new Vector2(32f, 0f), new Vector2(3f, 760f), new Color(0.26f, 0.88f, 0.89f, 0.56f));
        CreateBar(root, "Homepage Amber Rail", new Vector2(-32f, 0f), new Vector2(3f, 760f), new Color(0.95f, 0.65f, 0.24f, 0.64f));
        CreateBar(root, "Homepage Horizon", new Vector2(0f, -365f), new Vector2(1020f, 2f), new Color(0.36f, 0.82f, 0.82f, 0.48f));

        Text brand = EnsureText(root, "Homepage Brand", "DINO ATTACK  //  FRONTIER PROTOCOL");
        SetTopLeft(brand.rectTransform, new Vector2(72f, -52f), new Vector2(720f, 42f));
        brand.fontSize = 22;
        brand.fontStyle = FontStyle.Bold;
        brand.color = new Color(0.88f, 0.96f, 0.93f, 0.94f);
        brand.alignment = TextAnchor.MiddleLeft;
        brand.raycastTarget = false;

        Text status = EnsureText(root, "Homepage Status", "SECTOR 07  //  FIELD READY");
        SetTopRight(status.rectTransform, new Vector2(-72f, -52f), new Vector2(500f, 42f));
        status.fontSize = 18;
        status.fontStyle = FontStyle.Bold;
        status.color = new Color(0.33f, 0.88f, 0.89f, 0.95f);
        status.alignment = TextAnchor.MiddleRight;
        status.raycastTarget = false;

        Text eyebrow = EnsureText(root, "Homepage Eyebrow", "TACTICAL OPERATIONS  /  SELECT DEPLOYMENT");
        SetCentered(eyebrow.rectTransform, new Vector2(0f, 420f), new Vector2(760f, 32f));
        eyebrow.fontSize = 17;
        eyebrow.fontStyle = FontStyle.Bold;
        eyebrow.color = new Color(0.95f, 0.70f, 0.30f, 0.95f);
        eyebrow.alignment = TextAnchor.MiddleCenter;
        eyebrow.raycastTarget = false;

        Text footer = EnsureText(root, "Homepage Footer", "BUILD 0.8  •  DINO ATTACK CONTROL NETWORK");
        SetBottomRight(footer.rectTransform, new Vector2(-72f, 28f), new Vector2(620f, 32f));
        footer.fontSize = 15;
        footer.color = new Color(0.65f, 0.80f, 0.81f, 0.82f);
        footer.alignment = TextAnchor.MiddleRight;
        footer.raycastTarget = false;

        Transform logo = root.Find("Dino Attack Logo");
        if (logo != null)
        {
            logo.SetSiblingIndex(3);
            RectTransform rt = logo.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0f, 170f);
            rt.sizeDelta = new Vector2(390f, 390f);
        }

        root.Find("Homepage Cyan Rail")?.SetSiblingIndex(4);
        root.Find("Homepage Amber Rail")?.SetSiblingIndex(5);
        root.Find("Homepage Horizon")?.SetSiblingIndex(6);

        foreach (string name in new[] { "Open Tropical Battlefield", "Layered Battlefield", "Defense Battlefield", "First Person Defense Battlefield", "JEV Village Demo", "Companion Relationship Lab" })
        {
            Button button = GameObject.Find(name)?.GetComponent<Button>();
            if (button == null) continue;
            StyleButton(button, false);
        }

        Button intro = GameObject.Find("Dinosaur Introduction")?.GetComponent<Button>();
        if (intro != null) StyleButton(intro, true);

        foreach (string name in new[] { "Velociraptor Card", "TRex Card", "Pachycephalosaurus Card" })
        {
            Transform cardTransform = root.Find($"Dinosaur Introduction Panel/{name}");
            GameObject card = cardTransform != null ? cardTransform.gameObject : null;
            if (card == null) continue;
            Image image = card.GetComponent<Image>();
            if (image != null) image.color = new Color(0.025f, 0.11f, 0.16f, 0.94f);
            AddOutline(card, new Color(0.30f, 0.78f, 0.79f, 0.38f), 2f);
            foreach (Text text in card.GetComponentsInChildren<Text>(true))
            {
                text.color = new Color(0.90f, 0.95f, 0.92f, 0.95f);
                if (text.name == "Name") text.fontSize = 30;
            }
        }

        Text selection = GameObject.Find("Selection Status")?.GetComponent<Text>();
        if (selection != null)
        {
            selection.text = "SELECT DEPLOYMENT  //  AWAITING COMMAND";
            selection.color = new Color(0.97f, 0.70f, 0.28f, 0.96f);
            selection.fontSize = 18;
            selection.fontStyle = FontStyle.Bold;
        }

        Camera camera = GameObject.Find("Main Camera")?.GetComponent<Camera>();
        if (camera != null) camera.backgroundColor = new Color(0.012f, 0.028f, 0.055f, 1f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Homepage visual polish applied.");
    }

    private static Image EnsureImage(Transform parent, string name, int sibling)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(sibling);
        return go.GetComponent<Image>();
    }

    private static Text EnsureText(Transform parent, string name, string value)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void StyleButton(Button button, bool primary)
    {
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = primary
                ? new Color(0.06f, 0.34f, 0.39f, 0.96f)
                : new Color(0.025f, 0.12f, 0.17f, 0.94f);
        }
        ColorBlock colors = button.colors;
        colors.normalColor = primary ? new Color(0.10f, 0.42f, 0.45f, 0.98f) : new Color(0.045f, 0.18f, 0.23f, 0.98f);
        colors.highlightedColor = new Color(0.18f, 0.64f, 0.65f, 1f);
        colors.pressedColor = new Color(0.88f, 0.61f, 0.22f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.2f, 0.27f, 0.30f, 0.55f);
        colors.fadeDuration = 0.12f;
        button.colors = colors;
        AddOutline(button.gameObject, primary ? new Color(0.95f, 0.68f, 0.27f, 0.90f) : new Color(0.22f, 0.75f, 0.78f, 0.72f), 2f);
        Text label = button.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.color = new Color(0.92f, 0.97f, 0.94f, 0.96f);
            label.fontSize = primary ? 22 : 20;
            label.fontStyle = FontStyle.Bold;
        }
    }

    private static void AddOutline(GameObject go, Color color, float distance)
    {
        Outline outline = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, distance);
        outline.useGraphicAlpha = true;
    }

    private static void CreateBar(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        Image bar = EnsureImage(parent, name, 3);
        bar.color = color;
        bar.raycastTarget = false;
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    private static void SetCentered(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
    }

    private static void SetTopLeft(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void SetTopRight(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void SetBottomRight(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }
}





