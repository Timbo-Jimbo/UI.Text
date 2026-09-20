using System.Collections.Generic;
using TimboJimbo.UI.Text.Bridge;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Text
{
    /// <summary>Turns a <see cref="TextLayout"/>'s quads and decorations into UGUI geometry and materials.</summary>
    public sealed partial class TextBlock
    {
        private static readonly List<Vector3> s_Positions = new();
        private static readonly List<Color32> s_Colors = new();
        private static readonly List<Vector2> s_Uv0 = new();
        private static readonly List<Vector4> s_Uv1 = new();
        private static readonly List<List<int>> s_GroupTriangles = new();
        private static readonly List<List<int>> s_TrianglePool = new();
        private static readonly List<Material> s_Materials = new();
        private static readonly List<ITextBlockGlyphModifier> s_GlyphModifiers = new();
        private static readonly List<Rect> s_Rects = new();
        private static readonly Dictionary<Texture2D, Material> s_SdfMaterials = new();
        private static readonly Dictionary<Texture2D, Material> s_BitmapMaterials = new();

        /// <summary>Atlas texels one unit of the engine's bold weight dilates a glyph by at the sampling size: 1.7 texels for the default weight of 0.75, Unity's own look.</summary>
        private const float BoldTexelsPerUnit = 2.25f;

        private static void ClearGeometry()
        {
            s_Positions.Clear();
            s_Colors.Clear();
            s_Uv0.Clear();
            s_Uv1.Clear();
            s_GroupTriangles.Clear();
            s_Materials.Clear();
        }

        private void BuildGeometry(float scale)
        {
            // Vertices live in the glyph child's local space, which shares our rect but not necessarily our pivot.
            var rect = _glyphs.GetPixelAdjustedRect();
            // Layout space is pixels, origin top-left, y down. Canvas space is rect units, y up.
            float inverseScale = 1f / scale;
            float left = rect.xMin;
            float top = rect.yMax;
            Color32 tint = color;
            GetComponents(s_GlyphModifiers);

            BuildDecorations(left, top, inverseScale, tint);

            var handle = _layout.Handle;
            var glyph = new TextBlockGlyph();
            int glyphIndex = 0;
            for (int g = 0; g < handle.GroupCount; g++)
            {
                var group = handle.GetGroup(g);
                if (AtgSpriteAssets.IsPlaceholder(in group))
                    continue;

                var triangles = GetTriangleList(s_GroupTriangles.Count);
                // Synthetic bold: the engine's weight (boldStyleWeight, 0.75 by default) is a dilation the shader
                // applies in field units. Half the field spans the atlas padding, so the weight goes through this
                // atlas's padding to stay the same in texels whatever the padding is.
                float fieldPerTexel = group.AtlasPadding > 0 ? 0.5f / group.AtlasPadding : 0f;
                float dilateScale = fieldPerTexel * BoldTexelsPerUnit;
                for (int q = 0; q < group.Count; q++)
                {
                    handle.GetQuad(g, q, out var quad);
                    glyph.Index = glyphIndex++;
                    glyph.GlyphId = quad.GlyphId;
                    glyph.IsBitmap = group.IsBitmap;
                    glyph.BottomLeft = ToVertex(in quad.BottomLeft, left, top, inverseScale, tint, group.IsBitmap, dilateScale);
                    glyph.TopLeft = ToVertex(in quad.TopLeft, left, top, inverseScale, tint, group.IsBitmap, dilateScale);
                    glyph.TopRight = ToVertex(in quad.TopRight, left, top, inverseScale, tint, group.IsBitmap, dilateScale);
                    glyph.BottomRight = ToVertex(in quad.BottomRight, left, top, inverseScale, tint, group.IsBitmap, dilateScale);
                    for (int m = 0; m < s_GlyphModifiers.Count; m++)
                        s_GlyphModifiers[m].ModifyGlyph(this, ref glyph);

                    AddQuad(triangles, in glyph.BottomLeft, in glyph.TopLeft, in glyph.TopRight, in glyph.BottomRight);
                }
                s_GroupTriangles.Add(triangles);
                s_Materials.Add(GetMaterial(group.Atlas, !group.IsBitmap));
            }
        }

        /// <summary>Underlines and strikethroughs as solid quads in their own group, drawn before the glyphs.</summary>
        private void BuildDecorations(float left, float top, float inverseScale, Color32 tint)
        {
            var decorations = _layout.Decorations;
            if (decorations.Count == 0)
                return;

            var triangles = GetTriangleList(s_GroupTriangles.Count);
            for (int i = 0; i < decorations.Count; i++)
            {
                var quad = decorations[i];
                AddSolidQuad(triangles, quad.Px, left, top, inverseScale, Multiply(quad.Color, tint, false));
            }
            s_GroupTriangles.Add(triangles);
            s_Materials.Add(GetMaterial(AtgSpriteAssets.SolidTexture, false));
        }

        private static void AddSolidQuad(List<int> triangles, Rect px, float left, float top, float inverseScale, Color32 color)
        {
            var bl = Solid(left + px.xMin * inverseScale, top - px.yMax * inverseScale, color);
            var tl = Solid(left + px.xMin * inverseScale, top - px.yMin * inverseScale, color);
            var tr = Solid(left + px.xMax * inverseScale, top - px.yMin * inverseScale, color);
            var br = Solid(left + px.xMax * inverseScale, top - px.yMax * inverseScale, color);
            AddQuad(triangles, in bl, in tl, in tr, in br);
        }

        private static UIVertex Solid(float x, float y, Color32 color)
        {
            var v = UIVertex.simpleVert;
            v.position = new Vector3(x, y, 0f);
            v.color = color;
            v.uv0 = new Vector2(0.5f, 0.5f);
            v.uv1 = Vector4.zero;
            return v;
        }

        private static void AddQuad(List<int> triangles, in UIVertex bl, in UIVertex tl, in UIVertex tr, in UIVertex br)
        {
            int v = s_Positions.Count;
            Add(in bl);
            Add(in tl);
            Add(in tr);
            Add(in br);
            triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
            triangles.Add(v + 2); triangles.Add(v + 3); triangles.Add(v);
        }

        private static UIVertex ToVertex(in AtgVertex vertex, float left, float top, float inverseScale, Color32 tint, bool colorGlyph, float dilateScale)
        {
            // Colour bitmaps carry their own colour; the vertex only contributes alpha.
            Color32 c = colorGlyph ? new Color32(255, 255, 255, vertex.Color.a) : vertex.Color;
            var result = UIVertex.simpleVert;
            result.position = new Vector3(left + vertex.Position.x * inverseScale, top - vertex.Position.y * inverseScale, 0f);
            result.color = Multiply(c, tint, colorGlyph);
            result.uv0 = vertex.Uv;
            // uv1.x carries the synthetic bold dilation in field units; the SDF shader reads it.
            result.uv1 = new Vector4(vertex.Dilate * dilateScale, 0f, 0f, 0f);
            return result;
        }

        private static Color32 Multiply(Color32 a, Color32 b, bool alphaOnly)
        {
            if (alphaOnly)
                return new Color32(255, 255, 255, (byte)(a.a * b.a / 255));
            return new Color32((byte)(a.r * b.r / 255), (byte)(a.g * b.g / 255), (byte)(a.b * b.b / 255), (byte)(a.a * b.a / 255));
        }

        private static void Add(in UIVertex v)
        {
            s_Positions.Add(v.position);
            s_Colors.Add(v.color);
            s_Uv0.Add(v.uv0);
            s_Uv1.Add(v.uv1);
        }

        private static List<int> GetTriangleList(int index)
        {
            while (s_TrianglePool.Count <= index)
                s_TrianglePool.Add(new List<int>());
            var list = s_TrianglePool[index];
            list.Clear();
            return list;
        }

        private static Material GetMaterial(Texture2D atlas, bool sdf)
        {
            var cache = sdf ? s_SdfMaterials : s_BitmapMaterials;
            if (cache.TryGetValue(atlas, out var material) && material != null)
                return material;

            var shader = Shader.Find(sdf ? SdfShaderName : BitmapShaderName);
            material = new Material(shader)
            {
                name = $"TextBlock {(sdf ? "SDF" : "Bitmap")} {atlas.name}",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = atlas,
            };
            cache[atlas] = material;
            return material;
        }
    }
}
