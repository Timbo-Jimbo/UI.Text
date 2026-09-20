using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using NativeSettings = UnityEngine.TextCore.NativeTextGenerationSettings;
using NativeSpan = UnityEngine.TextCore.TextSpan;
using TextCoreAsset = UnityEngine.TextCore.Text.TextAsset;

namespace TimboJimbo.UI.Text.Bridge
{
    /// <summary>
    /// One laid-out text. Owns the native generation state (shaped runs, mesh cache, hit-testing data) for the
    /// lifetime of the handle, so keep one per text component and dispose it when the component goes away.
    /// </summary>
    public sealed class AtgTextHandle : IDisposable
    {
        // The generator works in 26.6 fixed point: 64 units per pixel.
        private const float FixedPoint = 64f;
        private const int Unconstrained = TextLib.k_unconstrainedScreenSize;

        private struct Group
        {
            public int MeshIndex;
            public int AtlasIndex;
            public AtgQuadGroup Info;
        }

        private IntPtr _info;
        private NativeSettings _native = NativeSettings.Default;
        private NativeTextInfo _lastInfo;
        private bool _uvsAreGenerated;
        private readonly List<Group> _groups = new();
        private List<List<List<int>>> _indicesByMesh = new();
        private Dictionary<EntityId, HashSet<uint>> _missingGlyphs = new();
        private NativeSpan[] _nativeSpans = Array.Empty<NativeSpan>();

        private static readonly List<uint> s_GlyphsToAdd = new();

        /// <summary>True after the last <see cref="Generate"/> cut the text short with an ellipsis.</summary>
        public bool IsElided { get; private set; }

        /// <summary>Extent of the laid-out text in pixels, from the last <see cref="Generate"/>.</summary>
        public Vector2 SizePx { get; private set; }

        public int GroupCount => _groups.Count;

        public AtgQuadGroup GetGroup(int index) => _groups[index].Info;

        /// <summary>
        /// Lays the text out and prepares its quads. Returns false when there is nothing to draw (empty text, no
        /// usable font). Missing glyphs are rasterised into the font atlases on the way.
        /// </summary>
        public bool Generate(ref AtgTextSettings settings)
        {
            _groups.Clear();
            IsElided = false;
            SizePx = Vector2.zero;

            if (string.IsNullOrEmpty(settings.Text))
                return false;
            if (!Convert(ref settings, out var font, out var fallbacks))
                return false;

            AtgFontAssets.Prepare(font);
            for (int i = 0; i < settings.SpanCount; i++)
                if (settings.Spans[i].Font != null)
                    AtgFontAssets.Prepare(settings.Spans[i].Font);
            _native.textSettings = fallbacks.Native;
            EnsureInfo();

            var lib = AtgEngine.Lib;
            bool wasCached = false;
            _lastInfo = lib.GenerateText(_native, _info, ref wasCached);
            if (!wasCached)
                _uvsAreGenerated = false;
            IsElided = _lastInfo.isElided;
            SizePx = new Vector2(_lastInfo.totalWidth / FixedPoint, _lastInfo.totalHeight / FixedPoint);

            // Shaping knows glyph ids before they exist in an atlas; rasterise the missing ones, then the UV pass
            // below maps each quad onto its glyph's atlas rect.
            _missingGlyphs.Clear();
            if (lib.HasMissingGlyphs(_lastInfo, ref _missingGlyphs))
            {
                foreach (var pair in _missingGlyphs)
                {
                    if (pair.Value.Count == 0 || Resources.EntityIdToObject(pair.Key) is not FontAsset target)
                        continue;
                    s_GlyphsToAdd.Clear();
                    s_GlyphsToAdd.AddRange(pair.Value);
                    target.TryAddGlyphs(s_GlyphsToAdd);
                }
                AtgFontAssets.FlushUpdates();
            }

            foreach (var perMesh in _indicesByMesh)
                foreach (var perAtlas in perMesh)
                    perAtlas.Clear();
            List<bool> noColorTracking = null;
            lib.ProcessMeshInfos(_lastInfo, _native, ref _indicesByMesh, ref noColorTracking, _uvsAreGenerated);
            _uvsAreGenerated = true;

            int meshIndex = 0;
            var meshInfos = _lastInfo.meshInfos;
            for (int i = 0; i < meshInfos.Length; i++)
            {
                if (Resources.EntityIdToObject(meshInfos[i].textAssetId) is not TextCoreAsset asset)
                    continue;
                if (meshIndex >= _indicesByMesh.Count)
                    break;
                var atlasGroups = _indicesByMesh[meshIndex++];
                var fontAsset = asset as FontAsset;
                var spriteAsset = asset as SpriteAsset;
                bool bitmap = spriteAsset != null || fontAsset.IsColor() || !IsSdf(fontAsset.atlasRenderMode);

                for (int atlasIndex = 0; atlasIndex < atlasGroups.Count; atlasIndex++)
                {
                    var group = atlasGroups[atlasIndex];
                    if (group.Count == 0)
                        continue;
                    Texture2D atlas = spriteAsset != null
                        ? spriteAsset.spriteSheet as Texture2D
                        : atlasIndex < fontAsset.atlasTextures.Length ? fontAsset.atlasTextures[atlasIndex] : null;
                    if (atlas == null)
                        continue;
                    _groups.Add(new Group
                    {
                        MeshIndex = i,
                        AtlasIndex = atlasIndex,
                        Info = new AtgQuadGroup { Atlas = atlas, IsBitmap = bitmap, IsSprite = spriteAsset != null, Count = group.Count, AtlasPadding = fontAsset != null ? fontAsset.atlasPadding : 0 },
                    });
                }
            }

            return _groups.Count > 0;
        }

        /// <summary>Reads one quad of a group produced by the last <see cref="Generate"/>.</summary>
        public void GetQuad(int groupIndex, int quadIndex, out AtgQuad quad)
        {
            var group = _groups[groupIndex];
            var meshInfos = _lastInfo.meshInfos;
            var elements = meshInfos[group.MeshIndex].textElementInfos;
            int meshSlot = MeshSlot(group.MeshIndex);
            int element = _indicesByMesh[meshSlot][group.AtlasIndex][quadIndex];
            ref var info = ref elements[element];
            quad.GlyphId = info.glyphID;
            quad.BottomLeft = Convert(in info.bottomLeft);
            quad.TopLeft = Convert(in info.topLeft);
            quad.TopRight = Convert(in info.topRight);
            quad.BottomRight = Convert(in info.bottomRight);
        }

        /// <summary>The size the text would take with the given settings, in pixels. Unconstrained dimensions grow to fit.</summary>
        public Vector2 MeasurePx(ref AtgTextSettings settings)
        {
            if (string.IsNullOrEmpty(settings.Text) || !Convert(ref settings, out var font, out var fallbacks))
                return Vector2.zero;
            AtgFontAssets.Prepare(font);
            _native.textSettings = fallbacks.Native;
            EnsureInfo();
            return AtgEngine.Lib.MeasureText(_native, _info);
        }

        // ---- Queries on the last layout ----

        public int CharacterCount => _info != IntPtr.Zero ? TextLib.GetCharacterCount(_info) : 0;

        public bool IsRightToLeft => _info != IntPtr.Zero && TextLib.IsMainDirectionRTL(_info);

        /// <summary>The link id under a point in layout pixels, or -1.</summary>
        public int LinkAt(Vector2 pointPx) => _info != IntPtr.Zero ? TextLib.FindIntersectingLink(pointPx, _info) : -1;

        /// <summary>Rectangles covering the characters in [start, end), one per line, in layout pixels.</summary>
        public void GetRangeRects(int start, int end, List<Rect> results)
        {
            results.Clear();
            if (_info == IntPtr.Zero || end <= start)
                return;
            var rects = TextSelectionService.GetHighlightRectangles(_info, start, end);
            if (rects != null)
                results.AddRange(rects);
        }

        /// <summary>Caret position for a character index, in layout pixels.</summary>
        public Vector2 CursorPositionPx(int index) => TextSelectionService.GetCursorPositionFromLogicalIndex(_info, index);

        /// <summary>The character index nearest a point in layout pixels.</summary>
        public int IndexAt(Vector2 pointPx) => TextSelectionService.GetCursorLogicalIndexFromPosition(_info, pointPx);

        public int LineOf(int index) => TextSelectionService.GetLineNumber(_info, index);

        public float LineHeightPx(int line) => TextSelectionService.GetLineHeight(_info, line);

        public int FirstIndexOnLine(int index) => TextSelectionService.GetFirstCharacterIndexOnLine(_info, index);

        public int LastIndexOnLine(int index) => TextSelectionService.GetLastCharacterIndexOnLine(_info, index);

        /// <summary>How many characters of the last layout fit within a width, for "read more" style truncation.</summary>
        public int CharactersThatFit(float widthPx) => TextLib.GetNumCharactersThatFitWithinWidth(_info, Mathf.RoundToInt(widthPx * FixedPoint));

        public void Dispose()
        {
            if (_info == IntPtr.Zero)
                return;
            TextGenerationInfo.Destroy(_info);
            _info = IntPtr.Zero;
            _uvsAreGenerated = false;
            _groups.Clear();
        }

        // ---- Internals ----

        private void EnsureInfo()
        {
            if (_info == IntPtr.Zero)
                _info = TextGenerationInfo.Create(isPermanent: true);
        }

        private int MeshSlot(int meshIndex)
        {
            // ProcessMeshInfos skips meshes whose asset is gone, so slots and mesh indices can drift apart.
            int slot = 0;
            var meshInfos = _lastInfo.meshInfos;
            for (int i = 0; i < meshIndex; i++)
                if (Resources.EntityIdToObject(meshInfos[i].textAssetId) is TextCoreAsset)
                    slot++;
            return slot;
        }

        private bool Convert(ref AtgTextSettings s, out FontAsset font, out AtgFallbackSet fallbacks)
        {
            fallbacks = s.Fallbacks ?? AtgFallbackSet.Shared;
            font = s.Font != null ? s.Font : AtgFallbackSet.DefaultFont;
            if (font == null)
            {
                Debug.LogWarning("[UI.Text] No font asset available.");
                return false;
            }
            if (!AtgFontAssets.IsDynamic(font))
            {
                Debug.LogWarning($"[UI.Text] The advanced text generator cannot render the static font asset '{font.name}'. Use a dynamic font asset.");
                return false;
            }

            ref var n = ref _native;
            n.text = s.Text;
            n.fontAsset = font.nativeFontAsset;
            n.fontSize = Px(s.FontSizePx);
            n.screenWidth = s.WidthPx < 0f ? Unconstrained : Px(s.WidthPx);
            n.screenHeight = s.HeightPx < 0f ? Unconstrained : Px(s.HeightPx);
            n.wordWrapEnabled = s.WordWrap;
            n.overflow = s.Overflow == AtgOverflow.Ellipsis ? TextOverflow.Ellipsis : TextOverflow.Clip;
            n.horizontalAlignment = s.HorizontalAlignment switch
            {
                AtgHorizontalAlignment.Center => UnityEngine.TextCore.HorizontalAlignment.Center,
                AtgHorizontalAlignment.Right => UnityEngine.TextCore.HorizontalAlignment.Right,
                AtgHorizontalAlignment.Justified => UnityEngine.TextCore.HorizontalAlignment.Justified,
                _ => UnityEngine.TextCore.HorizontalAlignment.Left,
            };
            n.verticalAlignment = s.VerticalAlignment switch
            {
                AtgVerticalAlignment.Middle => UnityEngine.TextCore.VerticalAlignment.Middle,
                AtgVerticalAlignment.Bottom => UnityEngine.TextCore.VerticalAlignment.Bottom,
                _ => UnityEngine.TextCore.VerticalAlignment.Top,
            };
            n.color = s.Color;
            n.fontStyle = ToFontStyles(s.Style);
            n.fontWeight = ToFontWeight(s.Weight);
            n.languageDirection = s.Direction == AtgDirection.RightToLeft ? LanguageDirection.RTL : LanguageDirection.LTR;
            n.characterSpacing = Px(s.CharacterSpacingPx);
            n.wordSpacing = Px(s.WordSpacingPx);
            n.paragraphSpacing = Px(s.ParagraphSpacingPx);
            n.preProcessFlags = PreProcessFlags.None;
            if (s.CollapseWhitespace)
                n.preProcessFlags |= PreProcessFlags.CollapseWhiteSpaces;
            if (s.ParseEscapeSequences)
                n.preProcessFlags |= PreProcessFlags.ParseEscapeSequences;
            n.disableAdvancedFontFeatures = s.DisableFontFeatures;
            n.richTextEnabled = false;
            // The engine takes vertex padding in atlas texels (units of the sampling size), not layout pixels.
            float texelsPerPx = s.FontSizePx > 0f && font.faceInfo.pointSize > 0 ? font.faceInfo.pointSize / s.FontSizePx : 1f;
            n.vertexPadding = Px(Mathf.Max(0f, s.VertexPaddingPx) * texelsPerPx);
            n.bestFit = false;

            int spanCount = s.Spans != null ? Mathf.Min(s.SpanCount, s.Spans.Length) : 0;
            if (spanCount == 0)
            {
                n.textSpans = null;
                return true;
            }
            if (_nativeSpans.Length != spanCount)
                _nativeSpans = new NativeSpan[spanCount];
            for (int i = 0; i < spanCount; i++)
                _nativeSpans[i] = ConvertSpan(in s.Spans[i], in n, font);
            n.textSpans = _nativeSpans;
            return true;
        }

        private static NativeSpan ConvertSpan(in AtgSpan span, in NativeSettings n, FontAsset baseFont)
        {
            var native = n.CreateTextSpan();
            native.startIndex = span.Start;
            native.length = span.Length;
            if (span.Font != null)
                native.fontAsset = span.Font.nativeFontAsset;
            if (span.FontSizePx > 0f)
                native.fontSize = Px(span.FontSizePx);
            if (span.HasColor)
                native.color = span.Color;
            if (span.Weight != 0)
                native.fontWeight = ToFontWeight(span.Weight);
            native.fontStyle = n.fontStyle | ToFontStyles(span.Style);
            native.linkID = span.LinkId;
            if (span.Sprite != null)
            {
                // The engine sizes a sprite to the font's ascent times spriteScale and keeps the aspect ratio of
                // the metrics; the metrics' absolute values and bearing are not honoured.
                var spanFont = span.Font != null ? span.Font : baseFont;
                float sizePx = (span.FontSizePx > 0f ? span.FontSizePx : n.fontSize / FixedPoint);
                float ascent = AtgFontAssets.MetricsPx(spanFont, sizePx).Ascent;
                float height = span.SpriteHeightPx > 0f ? span.SpriteHeightPx : ascent;
                float width = span.SpriteWidthPx > 0f ? span.SpriteWidthPx : height;
                native.spriteID = span.Sprite.GetEntityId();
                native.spriteMetrics = new GlyphMetrics(width, height, 0f, height, width);
                native.spriteScale = ascent > 0f ? height / ascent : 1f;
                native.spriteTint = false;
            }
            if (span.CharacterSpacingPx != 0f)
                native.cspace = Px(span.CharacterSpacingPx);
            if (span.IndentPx != 0f)
                native.indent = Px(span.IndentPx);
            if (span.LineHeightPx != 0f)
                native.lineHeight = Px(span.LineHeightPx);
            if (span.VerticalOffsetPx != 0f)
                native.vOffset = Px(span.VerticalOffsetPx);
            native.subscriptNestingLevel = span.SubscriptLevel;
            native.superscriptNestingLevel = span.SuperscriptLevel;
            return native;
        }

        private static AtgVertex Convert(in TextCoreVertex v)
        {
            // uv2.y carries the synthetic bold factor the engine's own shader dilates the SDF by.
            float x = v.uv2.y;
            return new AtgVertex
            {
                Position = new Vector2(v.position.x, v.position.y),
                Color = v.color,
                Uv = v.uv0,
                Dilate = x < 0f ? x / 3f : x,
            };
        }

        private static int Px(float px) => Mathf.RoundToInt(px * FixedPoint);

        private static TextFontWeight ToFontWeight(AtgWeight weight) => (TextFontWeight)(int)(weight == 0 ? AtgWeight.Regular : weight);

        private static FontStyles ToFontStyles(AtgStyle style)
        {
            var result = FontStyles.Normal;
            if ((style & AtgStyle.Italic) != 0) result |= FontStyles.Italic;
            if ((style & AtgStyle.Underline) != 0) result |= FontStyles.Underline;
            if ((style & AtgStyle.Strikethrough) != 0) result |= FontStyles.Strikethrough;
            if ((style & AtgStyle.UpperCase) != 0) result |= FontStyles.UpperCase;
            if ((style & AtgStyle.LowerCase) != 0) result |= FontStyles.LowerCase;
            if ((style & AtgStyle.SmallCaps) != 0) result |= FontStyles.SmallCaps;
            return result;
        }

        private static bool IsSdf(GlyphRenderMode mode) => mode switch
        {
            GlyphRenderMode.SDF or GlyphRenderMode.SDF8 or GlyphRenderMode.SDF16 or GlyphRenderMode.SDF32
                or GlyphRenderMode.SDFAA or GlyphRenderMode.SDFAA_HINTED => true,
            _ => false,
        };
    }
}
