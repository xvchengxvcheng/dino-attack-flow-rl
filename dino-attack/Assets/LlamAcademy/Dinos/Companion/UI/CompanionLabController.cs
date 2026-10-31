using System;
using System.Collections.Generic;
using LlamAcademy.Dinos.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.Companion
{
    /// <summary>Standalone, runtime-only relationship tuning laboratory.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CompanionLabController : MonoBehaviour
    {
        private readonly Dictionary<string, Slider> Sliders = new();
        private readonly Dictionary<string, Label> ValueLabels = new();
        private CompanionController Companion;
        private CompanionProfileData WorkingProfile;
        private UIDocument Document;
        private PanelSettings LabPanelSettings;
        private Label Feedback;
        private bool Updating;

        private static readonly (string Key, string Label, float Min, float Max)[] RelationshipFields =
        {
            (nameof(CompanionProfileData.Affinity), "好感", 0, 100),
            (nameof(CompanionProfileData.Familiarity), "熟悉度", 0, 100),
            (nameof(CompanionProfileData.Trust), "信任", 0, 100),
            (nameof(CompanionProfileData.Respect), "尊重", 0, 100),
            (nameof(CompanionProfileData.Stress), "压力", 0, 100),
        };

        private static readonly (string Key, string Label)[] JungFields =
        {
            (nameof(CompanionProfileData.Extraversion), "外倾 Extraversion"),
            (nameof(CompanionProfileData.Sensation), "感觉 Sensation"),
            (nameof(CompanionProfileData.Intuition), "直觉 Intuition"),
            (nameof(CompanionProfileData.Thinking), "思维 Thinking"),
            (nameof(CompanionProfileData.Feeling), "情感 Feeling"),
            (nameof(CompanionProfileData.RiskTolerance), "风险承受"),
            (nameof(CompanionProfileData.NeedForAutonomy), "自主需要"),
        };

        private void Awake()
        {
            Document = GetComponent<UIDocument>();
            // Scale this full-screen lab independently of the shared in-game dialogue HUD.
            if (Document.panelSettings != null)
            {
                LabPanelSettings = Instantiate(Document.panelSettings);
                LabPanelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                LabPanelSettings.referenceResolution = new Vector2Int(1280, 800);
                LabPanelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                LabPanelSettings.match = 1f;
                LabPanelSettings.scale = 1f;
                Document.panelSettings = LabPanelSettings;
            }
            Companion = FindFirstObjectByType<CompanionController>(FindObjectsInactive.Include);
            WorkingProfile = Companion?.Profile?.Clone() ?? CompanionSaveService.Load(out _);
            Build(Document.rootVisualElement);
            RefreshFromProfile();
        }

        private void OnDestroy()
        {
            if (LabPanelSettings != null) Destroy(LabPanelSettings);
        }

        private void Build(VisualElement root)
        {
            root.Clear();
            root.style.flexGrow = 1;
            root.style.height = Length.Percent(100);
            root.style.paddingLeft = root.style.paddingRight = 18;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 12;
            root.style.backgroundColor = CompanionUiStyle.Background;
            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.justifyContent = Justify.SpaceBetween;
            top.style.alignItems = Align.Center;
            top.style.flexShrink = 0;
            top.Add(CompanionUiStyle.Label(CompanionCharacterInfo.DisplayName + " · 关系实验室", 36));
            CompanionIntroductionView introduction = null;
            var about = CompanionUiStyle.Button("角色介绍", () => introduction?.Show());
            about.name = "companion-show-introduction";
            top.Add(about);
            var back = CompanionUiStyle.Button("返回地图选择", ReturnToMapSelect);
            back.style.fontSize = 22;
            back.style.minHeight = 48;
            top.Add(back);
            root.Add(top);
            var description = CompanionUiStyle.Label("本实验室直接调整运行时数据；保存关系会写入独立档案，不改变正式战斗存档。", 19, true);
            description.style.marginTop = 6;
            description.style.marginBottom = 8;
            description.style.flexShrink = 0;
            root.Add(description);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "companion-lab-scroll" };
            scroll.style.flexGrow = 1;
            scroll.style.flexBasis = 0;
            scroll.style.minHeight = 0;
            scroll.style.flexShrink = 1;
            var columns = new VisualElement { name = "companion-lab-columns" };
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexWrap = Wrap.Wrap;
            columns.style.alignItems = Align.FlexStart;
            AddGroup(columns, "关系变量", "影响命令接受、反馈语气与自主权", RelationshipFields);
            AddGroup(columns, "荣格心理维度", "七项倾向用于行为图权重", JungFields);
            scroll.Add(columns);
            root.Add(scroll);
            introduction = new CompanionIntroductionView(root);

            var actions = new VisualElement { name = "companion-lab-actions" };
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            actions.style.flexShrink = 0;
            actions.style.marginTop = 8;
            AddAction(actions, "生疏预设", () => ApplyPreset(Preset.Stranger));
            AddAction(actions, "默契预设", () => ApplyPreset(Preset.Familiar));
            AddAction(actions, "紧张预设", () => ApplyPreset(Preset.Tense));
            AddAction(actions, "保存关系", SaveRelationship, true);
            AddAction(actions, "载入关系", LoadRelationship);
            AddAction(actions, "恢复默认", ResetRelationship);
            Feedback = CompanionUiStyle.Label(string.Empty, 18);
            Feedback.style.color = CompanionUiStyle.Accent;
            actions.Add(Feedback);
            root.Add(actions);
        }

        private static void AddAction(VisualElement parent, string text, Action callback, bool primary = false)
        {
            var button = CompanionUiStyle.Button(text, callback, primary);
            button.style.fontSize = 20;
            button.style.minHeight = 46;
            parent.Add(button);
        }

        private void AddGroup(VisualElement columns, string title, string subtitle,
            IReadOnlyList<(string Key, string Label, float Min, float Max)> fields)
        {
            var group = new VisualElement();
            CompanionUiStyle.Panel(group, CompanionUiStyle.Surface);
            group.style.flexGrow = 1;
            group.style.flexBasis = 330;
            group.style.minWidth = 280;
            group.style.marginTop = group.style.marginRight = 10;
            CompanionUiStyle.AddHeading(group, title, subtitle);
            foreach (var field in fields) AddSlider(group, field.Key, field.Label, field.Min, field.Max);
            columns.Add(group);
        }

        private void AddGroup(VisualElement columns, string title, string subtitle,
            IReadOnlyList<(string Key, string Label)> fields)
        {
            var group = new VisualElement();
            CompanionUiStyle.Panel(group, CompanionUiStyle.Surface);
            group.style.flexGrow = 1;
            group.style.flexBasis = 330;
            group.style.minWidth = 280;
            group.style.marginTop = group.style.marginRight = 10;
            CompanionUiStyle.AddHeading(group, title, subtitle);
            foreach (var field in fields) AddSlider(group, field.Key, field.Label, 0, 1);
            columns.Add(group);
        }

        private void AddSlider(VisualElement parent, string key, string label, float min, float max)
        {
            var row = new VisualElement();
            row.style.marginTop = 12;
            row.Add(CompanionUiStyle.Label(label, 20));
            var value = CompanionUiStyle.Label("0", 19, true);
            value.style.minWidth = 60;
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.minHeight = 34;
            var slider = new Slider(min, max) { name = "companion-slider-" + key };
            slider.style.flexGrow = 1;
            slider.style.height = 34;
            slider.style.marginRight = 8;
            slider.RegisterValueChangedCallback(evt =>
            {
                value.text = max > 1 ? evt.newValue.ToString("0") : evt.newValue.ToString("0.00");
                if (!Updating) ApplySlider(key, evt.newValue);
            });
            line.Add(slider);
            line.Add(value);
            row.Add(line);
            parent.Add(row);
            Sliders[key] = slider;
            ValueLabels[key] = value;
        }

        private void ApplySlider(string key, float value)
        {
            WorkingProfile ??= CompanionProfileData.CreateDefault();
            Set(WorkingProfile, key, value);
            Companion?.ApplyProfile(WorkingProfile);
        }

        private void RefreshFromProfile()
        {
            WorkingProfile ??= Companion?.Profile?.Clone() ?? CompanionProfileData.CreateDefault();
            Updating = true;
            foreach (var item in Sliders)
            {
                float current = Get(WorkingProfile, item.Key);
                if (item.Value != null) item.Value.SetValueWithoutNotify(current);
                if (ValueLabels.TryGetValue(item.Key, out Label label) && label != null)
                    label.text = current > 1f ? current.ToString("0") : current.ToString("0.00");
            }
            Updating = false;
        }

        private enum Preset { Stranger, Familiar, Tense }

        private void ApplyPreset(Preset preset)
        {
            WorkingProfile ??= Companion?.Profile?.Clone() ?? CompanionProfileData.CreateDefault();
            var profile = WorkingProfile.Clone();
            switch (preset)
            {
                case Preset.Stranger:
                    profile.Affinity = 18; profile.Familiarity = 8; profile.Trust = 12; profile.Respect = 25; profile.Stress = 30;
                    profile.NeedForAutonomy = 0.72f; profile.RiskTolerance = 0.32f; break;
                case Preset.Familiar:
                    profile.Affinity = 78; profile.Familiarity = 82; profile.Trust = 76; profile.Respect = 70; profile.Stress = 18;
                    profile.NeedForAutonomy = 0.48f; profile.RiskTolerance = 0.64f; break;
                case Preset.Tense:
                    profile.Affinity = 44; profile.Familiarity = 55; profile.Trust = 34; profile.Respect = 45; profile.Stress = 86;
                    profile.NeedForAutonomy = 0.88f; profile.RiskTolerance = 0.18f; break;
            }
            WorkingProfile = profile;
            Companion?.ApplyProfile(profile);
            RefreshFromProfile();
            Feedback.text = "预设已应用（尚未保存）";
        }

        private void SaveRelationship()
        {
            WorkingProfile ??= Companion?.Profile?.Clone() ?? CompanionProfileData.CreateDefault();
            Companion?.ApplyProfile(WorkingProfile);
            bool saved = Companion != null
                ? Companion.SaveRelationship()
                : CompanionSaveService.Save(WorkingProfile, out _);
            Feedback.text = saved ? "关系档案已保存" : "关系档案保存失败";
        }

        private void LoadRelationship()
        {
            string reason;
            var profile = CompanionSaveService.Load(out reason);
            WorkingProfile = profile ?? CompanionProfileData.CreateDefault();
            Companion?.ApplyProfile(WorkingProfile);
            RefreshFromProfile();
            Feedback.text = string.IsNullOrEmpty(reason) || reason == "SaveLoaded" ? "关系档案已载入" : $"已载入默认关系（{reason}）";
        }

        private void ResetRelationship()
        {
            WorkingProfile = CompanionProfileData.CreateDefault();
            Companion?.ApplyProfile(WorkingProfile);
            RefreshFromProfile();
            Feedback.text = "已恢复默认关系（尚未保存）";
        }

        private void ReturnToMapSelect() => SceneManager.LoadScene(GameMapId.MapSelect.SceneName);

        private static float Get(CompanionProfileData p, string key) => key switch
        {
            nameof(CompanionProfileData.Affinity) => p.Affinity,
            nameof(CompanionProfileData.Familiarity) => p.Familiarity,
            nameof(CompanionProfileData.Trust) => p.Trust,
            nameof(CompanionProfileData.Respect) => p.Respect,
            nameof(CompanionProfileData.Stress) => p.Stress,
            nameof(CompanionProfileData.Extraversion) => p.Extraversion,
            nameof(CompanionProfileData.Sensation) => p.Sensation,
            nameof(CompanionProfileData.Intuition) => p.Intuition,
            nameof(CompanionProfileData.Thinking) => p.Thinking,
            nameof(CompanionProfileData.Feeling) => p.Feeling,
            nameof(CompanionProfileData.RiskTolerance) => p.RiskTolerance,
            nameof(CompanionProfileData.NeedForAutonomy) => p.NeedForAutonomy,
            _ => 0f
        };

        private static void Set(CompanionProfileData p, string key, float value)
        {
            switch (key)
            {
                case nameof(CompanionProfileData.Affinity): p.Affinity = value; break;
                case nameof(CompanionProfileData.Familiarity): p.Familiarity = value; break;
                case nameof(CompanionProfileData.Trust): p.Trust = value; break;
                case nameof(CompanionProfileData.Respect): p.Respect = value; break;
                case nameof(CompanionProfileData.Stress): p.Stress = value; break;
                case nameof(CompanionProfileData.Extraversion): p.Extraversion = value; break;
                case nameof(CompanionProfileData.Sensation): p.Sensation = value; break;
                case nameof(CompanionProfileData.Intuition): p.Intuition = value; break;
                case nameof(CompanionProfileData.Thinking): p.Thinking = value; break;
                case nameof(CompanionProfileData.Feeling): p.Feeling = value; break;
                case nameof(CompanionProfileData.RiskTolerance): p.RiskTolerance = value; break;
                case nameof(CompanionProfileData.NeedForAutonomy): p.NeedForAutonomy = value; break;
            }
        }
    }
}
