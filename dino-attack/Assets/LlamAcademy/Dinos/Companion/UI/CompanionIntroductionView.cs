using UnityEngine;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.Companion
{
    /// <summary>A shared, scrollable biography window for the battle HUD and relationship lab.</summary>
    public sealed class CompanionIntroductionView
    {
        private readonly VisualElement Overlay;
        private VisualElement PreviousFocus;
        public bool IsVisible { get; private set; }

        public CompanionIntroductionView(VisualElement root)
        {
            Overlay = new VisualElement { name = "companion-introduction", focusable = true };
            Overlay.style.position = Position.Absolute;
            Overlay.style.left = Overlay.style.right = Overlay.style.top = Overlay.style.bottom = 0;
            Overlay.style.backgroundColor = new Color(0.01f, 0.02f, 0.045f, 0.90f);
            Overlay.style.alignItems = Align.Center;
            Overlay.style.justifyContent = Justify.Center;
            Overlay.style.display = DisplayStyle.None;

            var card = new VisualElement { name = "companion-introduction-card" };
            CompanionUiStyle.Panel(card, new Color(0.045f, 0.075f, 0.13f, 1f));
            card.style.width = Length.Percent(88);
            card.style.maxWidth = 740;
            card.style.maxHeight = Length.Percent(85);
            card.style.paddingLeft = card.style.paddingRight = 28;
            card.style.paddingTop = card.style.paddingBottom = 24;
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0;
            var title = CompanionUiStyle.Label(CompanionCharacterInfo.DisplayName, 32);
            title.name = "companion-introduction-title";
            title.style.flexGrow = 1;
            header.Add(title);
            var close = CompanionUiStyle.Button("关闭", Hide);
            close.name = "companion-introduction-close";
            header.Add(close);
            card.Add(header);
            var subtitle = CompanionUiStyle.Label("角色介绍", 16, true);
            subtitle.style.marginTop = 8;
            subtitle.style.marginBottom = 18;
            card.Add(subtitle);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;
            var biography = CompanionUiStyle.Label(CompanionCharacterInfo.Introduction.Replace("；", "；\n\n"), 20);
            biography.name = "companion-introduction-text";
            biography.enableRichText = false;
            biography.style.whiteSpace = WhiteSpace.Normal;
            biography.style.flexShrink = 0;
            scroll.Add(biography);
            var skillHeading = CompanionUiStyle.Label("技能介绍", 20);
            skillHeading.style.marginTop = 24;
            skillHeading.style.marginBottom = 12;
            skillHeading.style.flexShrink = 0;
            scroll.Add(skillHeading);
            var skills = CompanionUiStyle.Label(CompanionCharacterInfo.SkillIntroduction, 18, true);
            skills.name = "companion-introduction-skills";
            skills.enableRichText = false;
            skills.style.whiteSpace = WhiteSpace.Normal;
            skills.style.flexShrink = 0;
            scroll.Add(skills);
            card.Add(scroll);
            Overlay.Add(card);
            root.Add(Overlay);
            Overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                Hide();
                evt.StopPropagation();
            });
        }

        public void Show()
        {
            PreviousFocus = Overlay.panel?.focusController?.focusedElement as VisualElement;
            IsVisible = true;
            Overlay.BringToFront();
            Overlay.style.display = DisplayStyle.Flex;
            Overlay.Focus();
        }

        public void Hide()
        {
            IsVisible = false;
            Overlay.style.display = DisplayStyle.None;
            PreviousFocus?.Focus();
        }
    }
}
