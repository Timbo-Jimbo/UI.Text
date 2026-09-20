using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UI.Text.Bridge
{
    public enum AtgHorizontalAlignment { Left, Center, Right, Justified }
    public enum AtgVerticalAlignment { Top, Middle, Bottom }
    public enum AtgOverflow { Clip, Ellipsis }
    public enum AtgDirection { LeftToRight, RightToLeft }

    /// <summary>OpenType weight classes. A real face is used when the font asset has one registered, otherwise bold is synthesised.</summary>
    public enum AtgWeight { Thin = 100, ExtraLight = 200, Light = 300, Regular = 400, Medium = 500, SemiBold = 600, Bold = 700, Heavy = 800, Black = 900 }

    [System.Flags]
    public enum AtgStyle
    {
        None = 0,
        Italic = 1 << 0,
        Underline = 1 << 1,
        Strikethrough = 1 << 2,
        UpperCase = 1 << 3,
        LowerCase = 1 << 4,
        SmallCaps = 1 << 5,
    }

    /// <summary>
    /// A range of the text with its own properties. Unset fields inherit from the text: a null font, a zero size,
    /// a zero weight, and <see cref="HasColor"/> false. Lengths and indices are UTF-16 offsets into the text.
    /// </summary>
    public struct AtgSpan
    {
        public int Start;
        public int Length;
        public FontAsset Font;
        public float FontSizePx;
        public bool HasColor;
        public Color32 Color;
        public AtgWeight Weight;
        public AtgStyle Style;
        /// <summary>Identifier reported by hit tests and link rectangles; -1 for none.</summary>
        public int LinkId;
        /// <summary>
        /// Draws a sprite instead of the range's character(s). The text must contain the sprite's placeholder
        /// character (U+E000 plus its index in the asset) at <see cref="Start"/>.
        /// </summary>
        public SpriteAsset Sprite;
        /// <summary>Box reserved for the sprite, in pixels: width, height, and how far its top sits above the baseline.</summary>
        public float SpriteWidthPx, SpriteHeightPx, SpriteAscentPx;
        public float CharacterSpacingPx;
        public float IndentPx;
        public float LineHeightPx;
        public float VerticalOffsetPx;
        public sbyte SubscriptLevel, SuperscriptLevel;

        public static AtgSpan Range(int start, int length) => new AtgSpan { Start = start, Length = length, LinkId = -1 };
    }

    /// <summary>Everything the generator needs for one layout. Pixel values are in output pixels (canvas units times canvas scale).</summary>
    public struct AtgTextSettings
    {
        public string Text;
        /// <summary>Primary font; null uses the fallback set's default font.</summary>
        public FontAsset Font;
        /// <summary>Fallback chain; null uses <see cref="AtgFallbackSet.Shared"/>.</summary>
        public AtgFallbackSet Fallbacks;
        public float FontSizePx;
        /// <summary>Layout box in pixels; a negative dimension is unconstrained.</summary>
        public float WidthPx, HeightPx;
        public bool WordWrap;
        public AtgOverflow Overflow;
        public AtgHorizontalAlignment HorizontalAlignment;
        public AtgVerticalAlignment VerticalAlignment;
        public Color32 Color;
        public AtgWeight Weight;
        public AtgStyle Style;
        public AtgDirection Direction;
        public float CharacterSpacingPx, WordSpacingPx, ParagraphSpacingPx;
        /// <summary>Collapse runs of whitespace and newlines like HTML; off keeps every newline as a line break.</summary>
        public bool CollapseWhitespace;
        /// <summary>Turn literal "\n"-style escape sequences in the text into their characters.</summary>
        public bool ParseEscapeSequences;
        /// <summary>Disable ligatures and other OpenType features (the editor does this for its own UI).</summary>
        public bool DisableFontFeatures;
        /// <summary>
        /// Extra room around every glyph quad, in pixels, for anti-aliasing, outlines and shadows to render into.
        /// Keep it within the font's atlas padding (see <see cref="AtgFontAssets.MaxPaddingPx"/>), beyond which the
        /// distance field has no data.
        /// </summary>
        public float VertexPaddingPx;
        /// <summary>Per-range overrides; only the first <see cref="SpanCount"/> entries are used, so the array can be pooled.</summary>
        public AtgSpan[] Spans;
        public int SpanCount;

        public static AtgTextSettings Default => new AtgTextSettings
        {
            Text = "",
            FontSizePx = 16f,
            WidthPx = -1f,
            HeightPx = -1f,
            WordWrap = true,
            Color = new Color32(255, 255, 255, 255),
            Weight = AtgWeight.Regular,
        };
    }
}
