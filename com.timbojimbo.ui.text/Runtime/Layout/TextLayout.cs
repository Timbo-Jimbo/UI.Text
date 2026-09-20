using System;
using System.Collections.Generic;
using TimboJimbo.UI.Text.Bridge;
using TimboJimbo.UI.Text.Markup;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UI.Text
{
    public enum TextBlockOverflow
    {
        /// <summary>Wrap to the width and let extra lines spill below the rect, like TextMeshPro's Overflow.</summary>
        Overflow,
        /// <summary>Cut the text at the rect (or Max Lines) and end it with an ellipsis.</summary>
        Ellipsis,
        /// <summary>Cut the text at the rect (or Max Lines) without an ellipsis.</summary>
        Truncate,
    }

    public enum TextBlockDirection
    {
        /// <summary>Paragraphs read left to right; right-to-left runs inside them still shape and order correctly.</summary>
        LeftToRight,
        RightToLeft,
    }

    /// <summary>Everything a layout needs. Lengths are layout pixels: canvas units times the canvas scale factor.</summary>
    public struct TextLayoutInput
    {
        public string Text;
        public bool RichText;
        /// <summary>The stack to draw with; null uses the project default, then the OS default font.</summary>
        public FontStack Font;
        /// <summary>How markup renders; null uses the built-in theme.</summary>
        public TextTheme Theme;
        public float FontSizePx;
        /// <summary>The box to lay out in. A negative width or height is unconstrained.</summary>
        public float WidthPx;
        public float HeightPx;
        /// <summary>Layout pixels per canvas unit, for the one-unit anti-aliasing margin around glyphs.</summary>
        public float PixelsPerUnit;
        public bool WordWrap;
        public TextAnchor Alignment;
        public TextBlockOverflow Overflow;
        public int MaxLines;
        public TextBlockDirection Direction;
        public float CharacterSpacingPx;
        public float WordSpacingPx;
        public float ParagraphSpacingPx;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikethrough;
        /// <summary>The engine's ICU data asset a component carries into builds; null uses the editor's copy.</summary>
        public UnityEngine.TextAsset IcuData;

        public static TextLayoutInput Default => new()
        {
            Text = "",
            RichText = true,
            FontSizePx = 24f,
            WidthPx = -1f,
            HeightPx = -1f,
            PixelsPerUnit = 1f,
            WordWrap = true,
            Alignment = TextAnchor.UpperLeft,
        };
    }

    /// <summary>
    /// A prefab to place over part of the laid-out text. The rectangle and metrics are layout pixels, y down
    /// from the layout's top-left corner.
    /// </summary>
    public struct InlinePlacementRequest
    {
        public int TokenIndex;
        public GameObject Prefab;
        /// <summary>True for backgrounds, drawn under the glyphs; false for objects, drawn over them.</summary>
        public bool BelowGlyphs;
        public Rect Px;
        /// <summary>This fragment's index and the total for its token.</summary>
        public int Index;
        public int Count;
        /// <summary>Baseline distance from the rectangle's top, then the text's ascent and descent.</summary>
        public float BaselinePx;
        public float AscentPx;
        public float DescentPx;
    }

    /// <summary>A solid rectangle drawn under the glyphs: an underline or strikethrough segment. Layout pixels, y down.</summary>
    public struct DecorationQuad
    {
        public Rect Px;
        public Color32 Color;
    }

    /// <summary>
    /// Lays out one text with the generator and answers questions about the result. Owns the parsed document,
    /// the layout text, the generator handle and the plans for decorations and inline prefabs, and knows nothing
    /// about GameObjects or meshes. <see cref="TextBlock"/> feeds it a rect and turns the result into UGUI.
    /// </summary>
    public sealed class TextLayout : IDisposable
    {
        private readonly AtgTextHandle _handle = new();
        private readonly TextDocument _document = new();
        private readonly LayoutText _layoutText = new();
        private readonly SpanBuffer _spans = new();
        private readonly List<DecorationRun> _decorationRuns = new();
        private readonly List<InlineObjectRequest> _atomicRequests = new();
        private readonly List<Rect> _placeholderBoxes = new();
        private readonly List<DecorationQuad> _decorations = new();
        private readonly List<Rect> _rects = new();
        private FontAsset _baseFont;
        private float _fontSizePx;
        private float _widthPx;
        private bool _generated;

        private static readonly List<TextLayout> s_MeasureScopes = new();
        private static int s_MeasureDepth;
        private static AtgTextHandle s_OverflowHandle;

        /// <summary>The parsed source: lines, runs, links and tokens in source indices.</summary>
        public TextDocument Document => _document;

        /// <summary>The text the generator laid out, with its mapping to the source.</summary>
        public LayoutText LayoutText => _layoutText;

        /// <summary>The generator's result as quad groups per atlas, for building geometry.</summary>
        internal AtgTextHandle Handle => _handle;

        /// <summary>True after a generation that produced glyphs.</summary>
        public bool HasContent => _generated;

        public Vector2 SizePx => _handle.SizePx;

        /// <summary>True when the last layout cut the text short.</summary>
        public bool IsElided => _handle.IsElided;

        /// <summary>Underline and strikethrough segments, in the run's colour before tinting.</summary>
        public IReadOnlyList<DecorationQuad> Decorations => _decorations;

        /// <summary>Bounds of each atomic placeholder, in text order.</summary>
        public IReadOnlyList<Rect> PlaceholderBoxes => _placeholderBoxes;

        /// <summary>Lays the text out. False when there was nothing to draw.</summary>
        public bool Generate(in TextLayoutInput input, in InlineContext context)
        {
            _generated = false;
            _decorations.Clear();
            _placeholderBoxes.Clear();
            _decorationRuns.Clear();
            _atomicRequests.Clear();

            AtgEngine.Initialize(input.IcuData);
            var theme = input.Theme != null ? input.Theme : TextTheme.Default;
            var settings = Settings(in input, theme, in context, _decorationRuns, _atomicRequests);
            if (string.IsNullOrEmpty(settings.Text))
                return false;

            if (input.Underline || input.Strikethrough)
            {
                _decorationRuns.Add(new DecorationRun
                {
                    Start = 0,
                    End = settings.Text.Length,
                    Underline = input.Underline,
                    Strikethrough = input.Strikethrough,
                    Color = new Color32(255, 255, 255, 255),
                    Font = _baseFont,
                    FontSizePx = input.FontSizePx,
                });
            }

            ApplyOverflow(ref settings, in input);
            // One canvas unit of room around every glyph for anti-aliasing, capped at what the atlas can represent.
            settings.VertexPaddingPx = _baseFont != null ? Mathf.Min(input.PixelsPerUnit, AtgFontAssets.MaxPaddingPx(_baseFont, input.FontSizePx)) : input.PixelsPerUnit;

            _generated = _handle.Generate(ref settings);
            if (!_generated)
                return false;

            CollectPlaceholderBoxes();
            CollectDecorations(theme.LineThicknessEm);
            return true;
        }

        /// <summary>Builds the generator settings and, for rich text, the document, layout text and spans.</summary>
        private AtgTextSettings Settings(in TextLayoutInput input, TextTheme theme, in InlineContext context, List<DecorationRun> decorations, List<InlineObjectRequest> atomicRequests)
        {
            var settings = AtgTextSettings.Default;
            var stack = EffectiveStack(input.Font);
            settings.Font = stack.Primary;
            settings.Fallbacks = stack.Fallbacks;
            _baseFont = settings.Font != null ? settings.Font : AtgFallbackSet.DefaultFont;
            _fontSizePx = input.FontSizePx;
            _widthPx = input.WidthPx;

            settings.FontSizePx = input.FontSizePx;
            settings.WidthPx = input.WidthPx;
            settings.HeightPx = -1f;
            settings.WordWrap = input.WordWrap;
            settings.HorizontalAlignment = ((int)input.Alignment % 3) switch { 0 => AtgHorizontalAlignment.Left, 1 => AtgHorizontalAlignment.Center, _ => AtgHorizontalAlignment.Right };
            settings.VerticalAlignment = ((int)input.Alignment / 3) switch { 0 => AtgVerticalAlignment.Top, 1 => AtgVerticalAlignment.Middle, _ => AtgVerticalAlignment.Bottom };
            // Colour is applied when the mesh is built, so a tint change never re-shapes the text.
            settings.Color = new Color32(255, 255, 255, 255);
            settings.Weight = input.Bold ? AtgWeight.Bold : AtgWeight.Regular;
            settings.Style = input.Italic ? AtgStyle.Italic : AtgStyle.None;
            settings.Direction = input.Direction == TextBlockDirection.RightToLeft ? AtgDirection.RightToLeft : AtgDirection.LeftToRight;
            settings.CharacterSpacingPx = input.CharacterSpacingPx;
            settings.WordSpacingPx = input.WordSpacingPx;
            settings.ParagraphSpacingPx = input.ParagraphSpacingPx;

            if (input.RichText)
            {
                MarkdownParser.Parse(input.Text, _document, theme.AllHandlers);
                _layoutText.Build(_document, theme);
                SpanBuilder.Build(_document, _layoutText, theme, _baseFont, input.FontSizePx, input.Bold, decorations, in context, atomicRequests, _spans);
                settings.Spans = _spans.Items;
                settings.SpanCount = _spans.Count;
            }
            else
            {
                _document.Reset(input.Text);
                _layoutText.BuildPlain(input.Text);
            }
            settings.Text = _layoutText.Text;
            return settings;
        }

        private void ApplyOverflow(ref AtgTextSettings settings, in TextLayoutInput input)
        {
            float height = -1f;
            switch (input.Overflow)
            {
                case TextBlockOverflow.Ellipsis:
                    height = input.HeightPx;
                    settings.Overflow = AtgOverflow.Ellipsis;
                    break;
                case TextBlockOverflow.Truncate:
                    height = input.HeightPx;
                    settings.Overflow = AtgOverflow.Clip;
                    break;
                default:
                    // Middle and bottom alignment need a box to align in; use the taller of the rect and the text.
                    if (settings.VerticalAlignment != AtgVerticalAlignment.Top)
                    {
                        var measure = settings;
                        measure.HeightPx = -1f;
                        s_OverflowHandle ??= new AtgTextHandle();
                        height = Mathf.Max(input.HeightPx, s_OverflowHandle.MeasurePx(ref measure).y);
                    }
                    break;
            }

            if (input.MaxLines > 0 && _baseFont != null)
            {
                // Half a pixel under the exact height, so a line whose top lands on the boundary is not counted.
                float cap = AtgFontAssets.LineHeightPx(_baseFont, input.FontSizePx) * input.MaxLines - 0.5f;
                height = height < 0f ? cap : Mathf.Min(height, cap);
                if (input.Overflow == TextBlockOverflow.Overflow)
                    settings.Overflow = AtgOverflow.Clip;
            }

            settings.HeightPx = height;
        }

        private void CollectPlaceholderBoxes()
        {
            for (int g = 0; g < _handle.GroupCount; g++)
            {
                var group = _handle.GetGroup(g);
                if (!AtgSpriteAssets.IsPlaceholder(in group))
                    continue;
                for (int q = 0; q < group.Count; q++)
                {
                    _handle.GetQuad(g, q, out var quad);
                    _placeholderBoxes.Add(quad.Bounds);
                }
            }
        }

        private void CollectDecorations(float thicknessEm)
        {
            for (int d = 0; d < _decorationRuns.Count; d++)
            {
                var run = _decorationRuns[d];
                if (run.Font == null)
                    continue;
                var metrics = AtgFontAssets.MetricsPx(run.Font, run.FontSizePx);
                _handle.GetRangeRects(run.Start, run.End, _rects);
                for (int i = 0; i < _rects.Count; i++)
                {
                    var lineRect = _rects[i];
                    // The line box bottom sits at the descent line; descent is negative below the baseline.
                    float baseline = lineRect.yMax + metrics.Descent;
                    if (run.Underline)
                    {
                        float thickness = thicknessEm > 0f ? thicknessEm * run.FontSizePx : metrics.UnderlineThickness;
                        float center = baseline - metrics.UnderlineOffset;
                        _decorations.Add(new DecorationQuad { Px = new Rect(lineRect.xMin, center - thickness * 0.5f, lineRect.width, thickness), Color = run.Color });
                    }
                    if (run.Strikethrough)
                    {
                        float thickness = thicknessEm > 0f ? thicknessEm * run.FontSizePx : metrics.StrikethroughThickness;
                        float center = baseline - metrics.StrikethroughOffset;
                        _decorations.Add(new DecorationQuad { Px = new Rect(lineRect.xMin, center - thickness * 0.5f, lineRect.width, thickness), Color = run.Color });
                    }
                }
            }
        }

        /// <summary>The prefabs the current layout wants placed, from the theme's handlers.</summary>
        public void CollectPlacements(in InlineContext context, List<InlinePlacementRequest> results)
        {
            results.Clear();
            if (!_generated || _document.Tokens.Count == 0)
                return;

            var metrics = _baseFont != null ? AtgFontAssets.MetricsPx(_baseFont, _fontSizePx) : default;
            float layoutWidth = _widthPx >= 0f ? _widthPx : _handle.SizePx.x;
            int ordinal = 0;

            for (int i = 0; i < _document.Tokens.Count; i++)
            {
                var token = _document.Tokens[i];
                var placed = _layoutText.Tokens[i];
                var handler = placed.Handler;
                if (handler == null || !placed.IsPlaced)
                    continue;

                if (placed.Placement == InlinePlacement.Atomic)
                {
                    var request = i < _atomicRequests.Count ? _atomicRequests[i] : default;
                    int k = ordinal++;
                    if (request.Prefab == null || k >= _placeholderBoxes.Count)
                        continue;
                    // The layout reserved the part of the box above the baseline; the prefab gets the whole box.
                    var box = _placeholderBoxes[k];
                    float above = box.height;
                    var size = request.SizeEm.x > 0f && request.SizeEm.y > 0f ? request.SizeEm : Vector2.one;
                    box.height = Mathf.Max(above, size.y * _fontSizePx);
                    results.Add(new InlinePlacementRequest
                    {
                        TokenIndex = i, Prefab = request.Prefab, BelowGlyphs = false, Px = box, Index = 0, Count = 1,
                        BaselinePx = above, AscentPx = above, DescentPx = box.height - above,
                    });
                    continue;
                }

                var req = handler.Request(in token, in context);
                if (req.Prefab == null)
                    continue;
                _handle.GetRangeRects(placed.Start, placed.End, _rects);
                int count = _rects.Count;
                if (count == 0)
                    continue;

                if (placed.Placement == InlinePlacement.Block)
                {
                    // One box across the full width, from the first line's top to the last line's bottom.
                    float yMin = float.MaxValue, yMax = float.MinValue;
                    for (int f = 0; f < count; f++)
                    {
                        yMin = Mathf.Min(yMin, _rects[f].yMin);
                        yMax = Mathf.Max(yMax, _rects[f].yMax);
                    }
                    results.Add(new InlinePlacementRequest
                    {
                        TokenIndex = i, Prefab = req.Prefab, BelowGlyphs = true, Px = new Rect(0f, yMin, layoutWidth, yMax - yMin), Index = 0, Count = 1,
                        BaselinePx = _rects[0].height + metrics.Descent, AscentPx = metrics.Ascent, DescentPx = -metrics.Descent,
                    });
                    continue;
                }

                // A token longer than a whole line leaves its pads alone on a line of their own; those fragments
                // hold no text and get no prefab.
                int first = 0, last = count - 1;
                if (placed.PaddingEm > 0f && placed.Length > 2 * LayoutText.PadCount)
                {
                    if (_handle.LineOf(placed.Start) != _handle.LineOf(placed.Start + LayoutText.PadCount))
                        first++;
                    if (_handle.LineOf(placed.End - 1) != _handle.LineOf(placed.End - LayoutText.PadCount - 1))
                        last--;
                }
                int fragmentCount = last - first + 1;
                for (int f = first; f <= last; f++)
                {
                    var lineRect = _rects[f];
                    results.Add(new InlinePlacementRequest
                    {
                        TokenIndex = i, Prefab = req.Prefab, BelowGlyphs = true, Px = lineRect, Index = f - first, Count = fragmentCount,
                        BaselinePx = lineRect.height + metrics.Descent, AscentPx = metrics.Ascent, DescentPx = -metrics.Descent,
                    });
                }
            }
        }

        /// <summary>Rectangles covering the source characters in [start, end), one per line, in layout pixels.</summary>
        public void GetSourceRangeRects(int sourceStart, int sourceEnd, List<Rect> results)
        {
            results.Clear();
            if (!_generated)
                return;
            _handle.GetRangeRects(_layoutText.ToLayout(sourceStart), _layoutText.ToLayout(sourceEnd), results);
        }

        /// <summary>The index into <see cref="TextDocument.Links"/> of the link under a layout point, or -1.</summary>
        public int LinkAt(Vector2 pointPx)
        {
            if (!_generated)
                return -1;
            int link = _handle.LinkAt(pointPx);
            return link >= 0 && link < _document.Links.Count ? link : -1;
        }

        /// <summary>
        /// The size a text would take, in layout pixels, without a component: for virtualised lists that need row
        /// heights ahead of time. A negative width is unconstrained. Re-entrant, since a handler may measure its own label.
        /// </summary>
        public static Vector2 Measure(string text, FontStack font, float fontSizePx, float widthPx, bool wordWrap = true, bool bold = false, bool italic = false, bool richText = false, TextTheme theme = null)
        {
            if (string.IsNullOrEmpty(text))
                return Vector2.zero;
            if (s_MeasureDepth == s_MeasureScopes.Count)
                s_MeasureScopes.Add(new TextLayout());
            var scope = s_MeasureScopes[s_MeasureDepth++];
            try
            {
                AtgEngine.Initialize(null);
                var input = TextLayoutInput.Default;
                input.Text = text;
                input.RichText = richText;
                input.Font = font;
                input.Theme = theme;
                input.FontSizePx = fontSizePx;
                input.WidthPx = widthPx;
                input.WordWrap = wordWrap;
                input.Bold = bold;
                input.Italic = italic;
                var settings = scope.Settings(in input, theme != null ? theme : TextTheme.Default, default, null, null);
                return scope._handle.MeasurePx(ref settings);
            }
            finally
            {
                s_MeasureDepth--;
            }
        }

        /// <summary>The stack a text draws with: its own, else the project default, else the OS default.</summary>
        public static FontStack EffectiveStack(FontStack font)
        {
            if (font != null)
                return font;
            var settings = TextBlockSettings.Instance;
            return settings != null && settings.DefaultFont != null ? settings.DefaultFont : FontStack.Default;
        }

        public void Dispose()
        {
            _handle.Dispose();
            _generated = false;
        }
    }
}
