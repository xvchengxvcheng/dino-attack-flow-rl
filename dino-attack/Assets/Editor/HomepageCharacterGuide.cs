using System;
using LlamAcademy.Dinos.Companion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Extends the existing homepage guide while retaining serialized button bindings.</summary>
public static class HomepageCharacterGuide
{
    [MenuItem("Tools/Dino Attack/Apply Homepage Character Guide")]
    public static void Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity")
            throw new InvalidOperationException("Open MapSelect in Edit Mode.");
        var root = GameObject.Find("Map Select").transform;
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Update character guide");
        ApplyTo(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void ApplyTo(Transform root)
    {
        root.Find("Dinosaur Introduction").GetComponentInChildren<Text>(true).text = "角色介绍";
        Transform panel = root.Find("Dinosaur Introduction Panel");
        panel.Find("Introduction Title").GetComponent<Text>().text = "角色介绍";
        panel.Find("Introduction Hint").GetComponent<Text>().text = "认识恐龙伙伴与来自星空的溯光";

        string[] dinosaurs = { "Pachycephalosaurus Card", "Velociraptor Card", "TRex Card" };
        for (int i = 0; i < dinosaurs.Length; i++)
        {
            Transform card = panel.Find(dinosaurs[i]);
            Place(card.GetComponent<RectTransform>(), new Vector2(-568f + i * 568f, -190f), new Vector2(544f, 200f));
            Place(card.Find("Portrait").GetComponent<RectTransform>(), new Vector2(-165f, 0f), new Vector2(155f, 155f));
            var name = card.Find("Name").GetComponent<Text>();
            Place(name.rectTransform, new Vector2(95f, 53f), new Vector2(300f, 50f));
            name.fontSize = 28;
            name.alignment = TextAnchor.MiddleLeft;
            var description = card.Find("Description").GetComponent<Text>();
            Place(description.rectTransform, new Vector2(95f, -32f), new Vector2(300f, 110f));
            description.fontSize = 20;
            description.alignment = TextAnchor.UpperLeft;
        }

        Transform existing = panel.Find("Suguang Card");
        GameObject profile = existing != null ? existing.gameObject
            : new GameObject("Suguang Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (existing == null) Undo.RegisterCreatedObjectUndo(profile, "Create Suguang profile");
        profile.layer = panel.gameObject.layer;
        profile.transform.SetParent(panel, false);
        Place(profile.GetComponent<RectTransform>(), new Vector2(0f, 135f), new Vector2(1680f, 340f));
        var background = profile.GetComponent<Image>();
        background.color = new Color(0.055f, 0.075f, 0.15f, 1f);
        background.raycastTarget = false;
        Sprite portrait = AssetDatabase.LoadAssetAtPath<Sprite>(SuguangPortraitCapture.PortraitPath);
        if (portrait == null)
        {
            SuguangPortraitCapture.Generate();
            portrait = AssetDatabase.LoadAssetAtPath<Sprite>(SuguangPortraitCapture.PortraitPath);
        }
        Transform portraitTransform = profile.transform.Find("Portrait");
        var portraitObject = portraitTransform != null ? portraitTransform.gameObject
            : new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (portraitTransform == null) Undo.RegisterCreatedObjectUndo(portraitObject, "Add Suguang portrait");
        portraitObject.transform.SetParent(profile.transform, false);
        portraitObject.layer = profile.layer;
        var portraitImage = portraitObject.GetComponent<Image>();
        portraitImage.sprite = portrait;
        portraitImage.color = Color.white;
        portraitImage.preserveAspect = true;
        portraitImage.raycastTarget = false;
        Place(portraitImage.rectTransform, new Vector2(-615f, 0f), new Vector2(240f, 320f));
        Font font = Resources.Load<Font>("CompanionFonts/NotoSansCJKsc-Regular");
        Text title = TextElement(profile.transform, "Name", CompanionCharacterInfo.DisplayName, font, 38);
        Place(title.rectTransform, new Vector2(200f, 105f), new Vector2(1100f, 64f));
        title.alignment = TextAnchor.MiddleLeft;
        foreach (string removedText in new[] { "Subtitle", "Description" })
        {
            Transform old = profile.transform.Find(removedText);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
        Text skillHeading = TextElement(profile.transform, "Skills Heading", "技能介绍", font, 18);
        Place(skillHeading.rectTransform, new Vector2(200f, 45f), new Vector2(1100f, 28f));
        skillHeading.alignment = TextAnchor.MiddleLeft;
        skillHeading.color = new Color(0.61f, 0.73f, 0.89f);
        string[] skillNames = { CompanionCharacterInfo.AttackSkill, CompanionCharacterInfo.GuardSkill, CompanionCharacterInfo.DefenseSkill };
        string[] skillDetails = { CompanionCharacterInfo.AttackDetails, CompanionCharacterInfo.GuardDetails, CompanionCharacterInfo.DefenseDetails };
        for (int i = 0; i < skillNames.Length; i++)
        {
            float x = -170f + i * 370f;
            Text skillTitle = TextElement(profile.transform, "Skill " + i + " Title", skillNames[i], font, 20);
            Place(skillTitle.rectTransform, new Vector2(x, -15f), new Vector2(350f, 30f));
            skillTitle.alignment = TextAnchor.MiddleLeft;
            Text skillBody = TextElement(profile.transform, "Skill " + i + " Details", skillDetails[i], font, 17);
            Place(skillBody.rectTransform, new Vector2(x, -70f), new Vector2(350f, 60f));
            skillBody.alignment = TextAnchor.UpperLeft;
            skillBody.color = new Color(0.72f, 0.80f, 0.90f);
        }
        Place(panel.Find("Close Dinosaur Introduction").GetComponent<RectTransform>(), new Vector2(0f, -365f), new Vector2(340f, 70f));
    }

    private static Text TextElement(Transform parent, string name, string value, Font font, int size)
    {
        Transform existing = parent.Find(name);
        var go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        if (existing == null) Undo.RegisterCreatedObjectUndo(go, "Create character text");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = new Color(0.89f, 0.93f, 1f);
        text.raycastTarget = false;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }
}
