using UnityEngine;

namespace TimboJimbo.UI.Text.Bridge
{
    /// <summary>One corner of a glyph quad. Positions are pixels from the top-left of the layout box, y down.</summary>
    public struct AtgVertex
    {
        public Vector2 Position;
        public Color32 Color;
        public Vector2 Uv;
        /// <summary>Synthetic bold amount for the SDF shader: 0 for none, up to roughly 1 for the heaviest weight.</summary>
        public float Dilate;
    }

    /// <summary>A laid-out glyph or sprite as four corners plus the glyph index it was drawn from.</summary>
    public struct AtgQuad
    {
        public int GlyphId;
        public AtgVertex BottomLeft, TopLeft, TopRight, BottomRight;

        /// <summary>Axis-aligned bounds of the quad in layout pixels, y down.</summary>
        public Rect Bounds
        {
            get
            {
                float xMin = Mathf.Min(BottomLeft.Position.x, TopLeft.Position.x);
                float xMax = Mathf.Max(TopRight.Position.x, BottomRight.Position.x);
                float yMin = Mathf.Min(TopLeft.Position.y, TopRight.Position.y);
                float yMax = Mathf.Max(BottomLeft.Position.y, BottomRight.Position.y);
                return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            }
        }
    }

    /// <summary>Quads that share one atlas texture and therefore one material.</summary>
    public struct AtgQuadGroup
    {
        public Texture2D Atlas;
        /// <summary>True when the atlas holds colour bitmaps (emoji fonts, sprite sheets) rather than a signed distance field.</summary>
        public bool IsBitmap;
        /// <summary>True when the group comes from a sprite asset rather than a font.</summary>
        public bool IsSprite;
        /// <summary>Number of quads in the group; read them with <see cref="AtgTextHandle.GetQuad"/>.</summary>
        public int Count;
        /// <summary>Texels of signed-distance padding around each glyph in the atlas (the field saturates there); 0 for bitmaps.</summary>
        public int AtlasPadding;
    }
}
