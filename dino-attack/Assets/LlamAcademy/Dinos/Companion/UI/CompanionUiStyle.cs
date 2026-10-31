using UnityEngine;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.Companion
{
    /// <summary>Shared visual language for the companion dialogue and relationship laboratory.</summary>
    public static class CompanionUiStyle
    {
        // Keep the UI independent from the fonts installed on the development machine.
        // The OTF lives below a Resources folder so it is included in player builds and
        // can be loaded before the first panel is constructed (including after a domain
        // reload).  This is important for Chinese text: the old default UI Toolkit font
        // only contained a small Latin glyph set, which rendered Chinese as empty boxes.
        private const string CjkFontResourcePath = "CompanionFonts/NotoSansCJKsc-Regular";
        private static Font CjkFont;
        private static bool FontResolved;

        public static readonly Color Background = new(0.025f, 0.045f, 0.12f, 0.97f);
        public static readonly Color Surface = new(0.06f, 0.08f, 0.20f, 0.98f);
        public static readonly Color SurfaceRaised = new(0.10f, 0.09f, 0.26f, 0.98f);
        public static readonly Color Border = new(0.25f, 0.75f, 0.95f, 0.72f);
        public static readonly Color Accent = new(0.33f, 0.86f, 1f, 1f);
        public static readonly Color AccentPurple = new(0.72f, 0.47f, 1f, 1f);
        public static readonly Color Text = new(0.91f, 0.96f, 1f, 1f);
        public static readonly Color Muted = new(0.62f, 0.73f, 0.86f, 1f);
        public static readonly Color Warning = new(1f, 0.62f, 0.45f, 1f);

        /// <summary>
        /// Applies the bundled Simplified Chinese font to an element. Font properties are
        /// inherited by UI Toolkit children, so applying this to the panel root also covers
        /// TextField internals and buttons created by other presenters.
        /// </summary>
        public static void ApplyFont(VisualElement element)
        {
            if (element == null) return;
            Font font = GetCjkFont();
            if (font == null) return;

            element.style.unityFont = font;
            element.style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        private static Font GetCjkFont()
        {
            if (FontResolved) return CjkFont;
            FontResolved = true;
            CjkFont = Resources.Load<Font>(CjkFontResourcePath);
            if (CjkFont == null)
                Debug.LogWarning($"Companion UI could not load bundled CJK font at Resources/{CjkFontResourcePath}. Chinese glyphs may be unavailable.");
            return CjkFont;
        }

        public static void Panel(VisualElement element, Color? color = null)
        {
            ApplyFont(element);
            element.style.backgroundColor = color ?? Surface;
            element.style.borderTopWidth = element.style.borderRightWidth =
                element.style.borderBottomWidth = element.style.borderLeftWidth = 1;
            element.style.borderTopColor = element.style.borderRightColor =
                element.style.borderBottomColor = element.style.borderLeftColor = Border;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 10;
            element.style.paddingLeft = element.style.paddingRight = 14;
            element.style.paddingTop = element.style.paddingBottom = 12;
        }

        public static Label Label(string text, int size = 18, bool muted = false)
        {
            var label = new Label(text);
            ApplyFont(label);
            label.style.color = muted ? Muted : Text;
            label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        public static Button Button(string text, System.Action callback, bool primary = false)
        {
            var button = new Button(callback) { text = text };
            ApplyFont(button);
            button.style.minHeight = 38;
            button.style.paddingLeft = button.style.paddingRight = 13;
            button.style.marginTop = button.style.marginBottom = 4;
            button.style.backgroundColor = primary ? new Color(0.14f, 0.30f, 0.48f, 1f) : SurfaceRaised;
            button.style.color = Text;
            button.style.fontSize = 17;
            button.style.borderTopColor = button.style.borderRightColor =
                button.style.borderBottomColor = button.style.borderLeftColor = primary ? Accent : AccentPurple;
            button.style.borderTopWidth = button.style.borderRightWidth =
                button.style.borderBottomWidth = button.style.borderLeftWidth = 1;
            return button;
        }

        public static void AddHeading(VisualElement parent, string title, string subtitle = null)
        {
            parent.Add(Label(title, 28));
            if (!string.IsNullOrEmpty(subtitle)) parent.Add(Label(subtitle, 18, true));
        }
    }
}
