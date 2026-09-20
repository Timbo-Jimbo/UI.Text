using UnityEngine;

namespace TimboJimbo.UI.Text
{
    /// <summary>One laid-out glyph as canvas geometry, handed to <see cref="ITextBlockGlyphModifier"/>s before it is drawn.</summary>
    public struct TextBlockGlyph
    {
        /// <summary>Index of the glyph in draw order within the text.</summary>
        public int Index;
        /// <summary>The font's glyph id, or the sprite's placeholder code point for sprites.</summary>
        public int GlyphId;
        /// <summary>True for colour bitmaps (emoji, sprites); their vertex colour only carries alpha.</summary>
        public bool IsBitmap;
        /// <summary>Corners in the text's local space, y up. uv1.x carries the synthetic bold amount.</summary>
        public UIVertex BottomLeft, TopLeft, TopRight, BottomRight;
    }

    /// <summary>
    /// Put one on the same GameObject as a <see cref="TextBlock"/> to alter glyph geometry as it is built:
    /// per-glyph animation, reveal effects, colour ramps. Called once per glyph on every rebuild.
    /// </summary>
    public interface ITextBlockGlyphModifier
    {
        void ModifyGlyph(TextBlock block, ref TextBlockGlyph glyph);
    }
}
