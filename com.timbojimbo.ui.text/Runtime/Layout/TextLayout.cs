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

    /// <summary>Everything a layout needs. Lengths are layout pixels: the unit the text is laid out in, canvas units for a TextBlock.</summary>
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
        /// <summary>Room around every glyph quad for anti-aliasing, in layout pixels: one device pixel's worth.</summary>
        public float AntiAliasMarginPx;
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
            AntiAliasMarginPx = 1f,
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
        private float _heightPx;
        private TextAnchor _alignment;
        private float _paragraphSpacingPx;
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
            {
                // Nothing to lay out: the handle lets go of the last text, so neither its size nor its queries answer for it.
                _handle.Clear();
                return false;
            }

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
            // Room around every glyph for anti-aliasing, capped at what the atlas can represent.
            settings.VertexPaddingPx = _baseFont != null ? Mathf.Min(input.AntiAliasMarginPx, AtgFontAssets.MaxPaddingPx(_baseFont, input.FontSizePx)) : input.AntiAliasMarginPx;

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
            _heightPx = input.HeightPx;
            _alignment = input.Alignment;
            _paragraphSpacingPx = input.ParagraphSpacingPx;

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

        /// <summary>
        /// Rectangles covering the source characters in [start, end), one per line, in layout pixels. A text with
        /// nothing to draw (only spaces or line breaks) is laid out all the same and answers too.
        /// </summary>
        public void GetSourceRangeRects(int sourceStart, int sourceEnd, List<Rect> results)
        {
            results.Clear();
            if (!_handle.IsLaidOut)
                return;
            _handle.GetRangeRects(_layoutText.ToLayout(sourceStart), _layoutText.ToLayout(sourceEnd), results);
        }

        /// <summary>The index into <see cref="TextDocument.Links"/> of the link under a layout point, or -1.</summary>
        public int LinkAt(Vector2 pointPx)
        {
            if (!_handle.IsLaidOut)
                return -1;
            int link = _handle.LinkAt(pointPx);
            return link >= 0 && link < _document.Links.Count ? link : -1;
        }

        // ---- Carets and lines ----
        // Source indices and layout pixels (y down from the layout's top-left corner), for the last layout. Plain text
        // maps 1:1; with rich text a source index inside hidden markup is taken at the next shown character, as the
        // range rectangles take it. A text with nothing to draw (spaces, line breaks) is laid out all the same and
        // answers; an empty text answers from its font, as a single empty line placed where its alignment puts a line.
        // An index where a line wraps is on two lines at once: it ends the line before and starts the line after. Taken
        // upstream it is the end of the line before, otherwise the start of the line after, as a caret's affinity
        // decides in UIKit (UITextStorageDirection) and Flutter (TextAffinity); the caller keeps the affinity with its
        // caret, upstream after End or a tap past a wrapped line's end.

        /// <summary>
        /// The caret at a source index, in layout pixels: zero wide, at the insertion point, as tall as the line the
        /// index is on. An index where a line wraps is drawn at the end of the line before when
        /// <paramref name="upstream"/>, otherwise at the start of the line after. In an empty text it sits at the side of
        /// the box the text is aligned to, as tall as the font's line height.
        /// </summary>
        public Rect GetCaretRect(int sourceIndex, bool upstream = false)
        {
            if (!_handle.IsLaidOut)
                return EmptyTextCaret();
            int index = LayoutIndex(sourceIndex);
            if (index == _layoutText.Length && EndsWithBreak())
                return TrailingLineCaret(_handle.CaretRectPx(index - 1));
            if (upstream && WrapsAt(index))
                return WrappedLineEndCaret(index);
            return _handle.CaretRectPx(index);
        }

        /// <summary>
        /// The source index of the caret position nearest a point in layout pixels. A point above the first line or
        /// below the last finds the nearest position on that line; past either end of a line, that end.
        /// </summary>
        public int GetIndexAt(Vector2 pointPx) => GetIndexAt(pointPx, out _);

        /// <summary>
        /// The source index of the caret position nearest a point in layout pixels, as <see cref="GetIndexAt(Vector2)"/>,
        /// and whether to take it upstream: true when the point is on a wrapped line past its end, whose nearest position
        /// is the index the next line starts at.
        /// </summary>
        public int GetIndexAt(Vector2 pointPx, out bool upstream)
        {
            upstream = false;
            if (!_handle.IsLaidOut)
                return 0;
            int length = _layoutText.Length;
            bool trailingLine = EndsWithBreak();
            // The last line with characters on it ends at the text's end, or before a final line break.
            var lastCaret = _handle.CaretRectPx(trailingLine ? length - 1 : length);
            if (trailingLine && pointPx.y >= TrailingLineCaret(lastCaret).yMin)
                return _layoutText.ToSource(length);
            // Held within the lines, so a point above or below them is matched against the nearest one.
            pointPx.y = Mathf.Clamp(pointPx.y, _handle.CaretRectPx(0).yMin, lastCaret.yMax);
            int index = _handle.IndexAt(pointPx);
            if (!WrapsAt(index))
                return _layoutText.ToSource(index);
            // A wrap's index is on both lines; the point is on the one its height is nearer, split midway between them.
            upstream = pointPx.y < (WrappedLineEndCaret(index).yMax + _handle.CaretRectPx(index).yMin) * 0.5f;
            return upstream ? _layoutText.ToSourceEnd(index) : _layoutText.ToSource(index);
        }

        /// <summary>How many lines the text has: at least 1, and a text ending in a line break ends with an empty line.</summary>
        public int LineCount
        {
            get
            {
                if (!_handle.IsLaidOut)
                    return 1;
                int last = _handle.LineOf(CodePointStart(_layoutText.Length - 1));
                return EndsWithBreak() ? last + 2 : last + 1;
            }
        }

        /// <summary>
        /// The line a source index is on, counted from 0. An index where a line wraps is on the line before when
        /// <paramref name="upstream"/>, otherwise on the line after.
        /// </summary>
        public int GetLineAt(int sourceIndex, bool upstream = false)
        {
            if (!_handle.IsLaidOut)
                return 0;
            int length = _layoutText.Length;
            int index = LayoutIndex(sourceIndex);
            if (index < length)
                return _handle.LineOf(upstream && WrapsAt(index) ? CodePointStart(index - 1) : index);
            // The text's end: on the empty line after a final line break, otherwise on the last character's line.
            int last = _handle.LineOf(CodePointStart(length - 1));
            return EndsWithBreak() ? last + 1 : last;
        }

        /// <summary>The source index of a line's first character. A line past the last starts at the text's end.</summary>
        public int GetLineStart(int line) => _layoutText.ToSource(LayoutLineStart(line));

        /// <summary>
        /// The source index at the end of a line's text: before its line break, if it ends in one. A line that wraps
        /// ends at the index the next one starts at; take it upstream (<see cref="GetCaretRect"/>,
        /// <see cref="GetLineAt"/>) to keep it on this line.
        /// </summary>
        public int GetLineEnd(int line)
        {
            int start = LayoutLineStart(line);
            int end = LayoutLineStart(line + 1);
            if (end > start && _layoutText.Text[end - 1] == '\n')
                end--;
            return _layoutText.ToSourceEnd(end);
        }

        /// <summary>
        /// The line height (the distance between baselines) of a font stack's primary font at a size, in layout pixels:
        /// the font a text with that stack is laid out in. 0 when there is no font to lay out in.
        /// </summary>
        public static float LineHeightOf(FontStack font, float fontSizePx)
        {
            var primary = EffectiveStack(font).Primary;
            if (primary == null)
                primary = AtgFallbackSet.DefaultFont;
            return primary != null ? AtgFontAssets.LineHeightPx(primary, fontSizePx) : 0f;
        }

        // A source index as a layout index the engine takes: within the text and at a code point boundary.
        private int LayoutIndex(int sourceIndex) => CodePointStart(Mathf.Clamp(_layoutText.ToLayout(sourceIndex), 0, _layoutText.Length));

        // A layout index between the halves of a surrogate pair moved back to the pair's start: the engine works in code
        // points and takes indices at their boundaries (see AtgTextHandle's queries).
        private int CodePointStart(int index)
        {
            var text = _layoutText.Text;
            return index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]) ? index - 1 : index;
        }

        private bool EndsWithBreak()
        {
            var text = _layoutText.Text;
            return text.Length > 0 && text[text.Length - 1] == '\n';
        }

        // Whether a line wraps at a layout index: a line starts there that no line break began, so the index also ends
        // the line before it.
        private bool WrapsAt(int index)
        {
            var text = _layoutText.Text;
            if (index <= 0 || index >= text.Length || text[index - 1] == '\n')
                return false;
            return _handle.LineOf(index) != _handle.LineOf(CodePointStart(index - 1));
        }

        // The caret at the end of the line a wrap at a layout index ends: after the character before the wrap, on that
        // character's line and as tall as it. The caret before that character is at one side of its box, so the caret
        // after it is at the other, whichever way the text runs; a character the engine gives no box keeps the caret
        // before it.
        private Rect WrappedLineEndCaret(int index)
        {
            int previous = CodePointStart(index - 1);
            var caret = _handle.CaretRectPx(previous);
            _handle.GetRangeRects(previous, index, _rects);
            if (_rects.Count > 0)
            {
                var box = _rects[0];
                caret.x = Mathf.Abs(box.xMax - caret.x) >= Mathf.Abs(box.xMin - caret.x) ? box.xMax : box.xMin;
            }
            return caret;
        }

        // The first layout index on a line; the text's length past the last line with characters on it. Lines are runs
        // of the text in order, so it is found by bisecting the engine's line numbers. Each probe asks for the line of
        // the code point it lands in: both halves of a surrogate pair are on one line, so the bisection still finds the
        // pair's start.
        private int LayoutLineStart(int line)
        {
            if (!_handle.IsLaidOut || line <= 0)
                return 0;
            int low = 0, high = _layoutText.Length;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (_handle.LineOf(CodePointStart(mid)) < line)
                    low = mid + 1;
                else
                    high = mid;
            }
            return low;
        }

        // A text ending in a line break ends with an empty line, which holds no character for the engine to place a
        // caret at. A line break is on the line it ends (as in ICU's line breaking), so that line's caret is the break's
        // own moved a line further down (a paragraph's spacing included) to the side the text is aligned to.
        private Rect TrailingLineCaret(Rect breakCaret)
            => new(AlignedX(), breakCaret.yMin + BaseLineHeightPx + _paragraphSpacingPx, 0f, breakCaret.height);

        // An empty text is one empty line of its font, placed in its box as the alignment places a line.
        private Rect EmptyTextCaret()
        {
            float height = BaseLineHeightPx;
            float box = _heightPx >= 0f ? _heightPx : height;
            float top = ((int)_alignment / 3) switch { 0 => 0f, 1 => (box - height) * 0.5f, _ => box - height };
            return new Rect(AlignedX(), top, 0f, height);
        }

        // Where a line with nothing on it starts across: the side of the box the text is aligned to.
        private float AlignedX()
        {
            float width = _widthPx >= 0f ? _widthPx : _handle.SizePx.x;
            return ((int)_alignment % 3) switch { 0 => 0f, 1 => width * 0.5f, _ => width };
        }

        private float BaseLineHeightPx => _baseFont != null ? AtgFontAssets.LineHeightPx(_baseFont, _fontSizePx) : 0f;

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
