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
        /// <summary>Paragraphs read right to left; left-to-right runs inside them (a Latin word, a number) still read left to right.</summary>
        RightToLeft,
        /// <summary>
        /// The direction of the text's first strong character (a letter, or a directional mark): right to left when it
        /// is Arabic, Hebrew or another right-to-left script, otherwise left to right; with none (an empty text, or
        /// only numbers, punctuation and emoji), left to right. For text people write, such as a text field's or a
        /// chat message's. Over the whole text, as HTML's dir="auto" on an input: iOS and Android text views decide
        /// each paragraph by its own first letter, and a later paragraph in the other direction here reads the
        /// first's way.
        /// </summary>
        Auto,
        /// <summary>
        /// As <see cref="Auto"/>, but right to left when the text has no strong character: for a text field while the
        /// keyboard writes right to left, whose caret then starts at the right before the first letter is typed, as on
        /// iOS (Android's FIRSTSTRONG_RTL).
        /// </summary>
        AutoRightToLeft,
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
        /// <summary>
        /// Whether <see cref="Alignment"/>'s left and right are the start and end of the text, as UIKit's natural
        /// alignment and CSS's text-align: start and end are: a text read right to left aligned left sits at the right.
        /// </summary>
        public bool NaturalAlignment;
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
        private bool _rightToLeft;
        private bool _generated;
        // What the caret and hit-test queries find of the last layout, as they first need it: each line's extent, and
        // for each index whether a character is drawn with a box of its own there (0 not yet known, 1 yes, 2 no), the
        // direction it reads in (0 not yet known, 1 left to right, 2 right to left) and the engine's caret (NaN not yet
        // known).
        private readonly List<LineSpan> _lineSpans = new();
        private byte[] _drawn = Array.Empty<byte>();
        private byte[] _direction = Array.Empty<byte>();
        private float[] _caretX = Array.Empty<float>();

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

        /// <summary>
        /// Whether the last layout read right to left: its <see cref="TextLayoutInput.Direction"/>, or for
        /// <see cref="TextBlockDirection.Auto"/>, that of its first strong character.
        /// </summary>
        public bool IsRightToLeft => _rightToLeft;

        /// <summary>
        /// Whether a plain text (no markup) laid out in a direction reads right to left, as <see cref="IsRightToLeft"/>
        /// says once it is: for <see cref="TextBlockDirection.Auto"/> and <see cref="TextBlockDirection.AutoRightToLeft"/>,
        /// by its first strong character.
        /// </summary>
        public static bool ReadsRightToLeft(string text, TextBlockDirection direction) => direction switch
        {
            TextBlockDirection.RightToLeft => true,
            TextBlockDirection.Auto => Bidi.FirstStrongIsRightToLeft(text, false),
            TextBlockDirection.AutoRightToLeft => Bidi.FirstStrongIsRightToLeft(text, true),
            _ => false,
        };

        /// <summary>
        /// The alignment the last layout was made with: its input's, turned across for
        /// <see cref="TextLayoutInput.NaturalAlignment"/> when the text reads right to left.
        /// </summary>
        public TextAnchor Alignment => _alignment;

        /// <summary>Underline and strikethrough segments, in the run's colour before tinting.</summary>
        public IReadOnlyList<DecorationQuad> Decorations => _decorations;

        /// <summary>Bounds of each atomic placeholder, in text order.</summary>
        public IReadOnlyList<Rect> PlaceholderBoxes => _placeholderBoxes;

        /// <summary>Lays the text out. False when there was nothing to draw.</summary>
        public bool Generate(in TextLayoutInput input, in InlineContext context)
        {
            _generated = false;
            ClearQueries();
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
            _paragraphSpacingPx = input.ParagraphSpacingPx;

            settings.FontSizePx = input.FontSizePx;
            settings.WidthPx = input.WidthPx;
            settings.HeightPx = -1f;
            settings.WordWrap = input.WordWrap;
            settings.VerticalAlignment = ((int)input.Alignment / 3) switch { 0 => AtgVerticalAlignment.Top, 1 => AtgVerticalAlignment.Middle, _ => AtgVerticalAlignment.Bottom };
            // Colour is applied when the mesh is built, so a tint change never re-shapes the text.
            settings.Color = new Color32(255, 255, 255, 255);
            settings.Weight = input.Bold ? AtgWeight.Bold : AtgWeight.Regular;
            settings.Style = input.Italic ? AtgStyle.Italic : AtgStyle.None;
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
            // Auto reads the text as shown, markup taken out.
            _rightToLeft = ReadsRightToLeft(settings.Text, input.Direction);
            settings.Direction = _rightToLeft ? AtgDirection.RightToLeft : AtgDirection.LeftToRight;
            _alignment = input.NaturalAlignment && _rightToLeft ? Across(input.Alignment) : input.Alignment;
            settings.HorizontalAlignment = ((int)_alignment % 3) switch { 0 => AtgHorizontalAlignment.Left, 1 => AtgHorizontalAlignment.Center, _ => AtgHorizontalAlignment.Right };
            return settings;
        }

        // An alignment turned across, left for right: TextAnchor runs upper-left to lower-right, a row of three at a time.
        internal static TextAnchor Across(TextAnchor alignment) => (TextAnchor)((int)alignment / 3 * 3 + 2 - (int)alignment % 3);

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
        /// Rectangles covering the source characters in [start, end), in layout pixels: one per stretch of a line they
        /// cover, a line whose text reads both ways having one per stretch that reads one way. A line's trailing spaces
        /// and its line break are covered after its text, where its carets stand (see <see cref="GetCaretRect"/>). A
        /// text with nothing to draw (only spaces or line breaks) is laid out all the same and answers too.
        /// </summary>
        public void GetSourceRangeRects(int sourceStart, int sourceEnd, List<Rect> results)
        {
            results.Clear();
            if (!_handle.IsLaidOut)
                return;
            int length = _layoutText.Length;
            int start = Mathf.Clamp(_layoutText.ToLayout(sourceStart), 0, length);
            int end = Mathf.Clamp(_layoutText.ToLayout(sourceEnd), 0, length);
            if (end <= start)
                return;
            int last = _handle.LineOf(CodePointStart(end - 1));
            for (int line = _handle.LineOf(CodePointStart(start)); line <= last; line++)
            {
                var span = LineSpanOf(line);
                int from = Mathf.Max(start, span.First), to = Mathf.Min(end, span.Next);
                int textTo = Mathf.Min(to, span.Content);
                int lineFrom = results.Count;
                if (textTo > from)
                {
                    _handle.GetRangeRects(from, textTo, _rects);
                    results.AddRange(_rects);
                }
                int spacesFrom = Mathf.Max(from, span.Content);
                if (to > spacesFrom)
                {
                    float a = CaretX(in span, spacesFrom);
                    float b = CaretX(in span, Mathf.Min(to, span.Stop));
                    // The line break, past the spaces, as wide as the engine draws it.
                    if (to > span.Stop)
                        b += (_rightToLeft ? -1f : 1f) * Width(span.Stop, span.Next);
                    if (Mathf.Abs(b - a) > 0.01f)
                        results.Add(Rect.MinMaxRect(Mathf.Min(a, b), span.Top, Mathf.Max(a, b), span.Bottom));
                }
                JoinTouching(results, lineFrom);
            }
        }

        // Joins the rectangles of one line from an index on that touch or overlap across into one, as one stretch.
        private static void JoinTouching(List<Rect> rects, int from)
        {
            for (int i = from; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i];
                    var b = rects[j];
                    if (b.xMin > a.xMax + 0.01f || a.xMin > b.xMax + 0.01f)
                        continue;
                    rects[i] = Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
                    rects.RemoveAt(j);
                    j = i;
                }
            }
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
        //
        // Where text reads both ways, carets follow Core Text's primary caret, as UIKit's text views draw it (and
        // Android's getPrimaryHorizontal). A caret between two characters stands at the edge of the one whose run reads
        // nearer the paragraph's way (two in one run share that edge): where a character of the paragraph's direction
        // typed there would go. A line's start and end count as the paragraph's direction, so a caret at the start of a
        // line stands at the side the text starts from (the right of a right-to-left line), and one at its end (before
        // its line break, at a wrap taken upstream, at the text's end) at the side it ends at, past the spaces there,
        // which stand at the paragraph's level after the line's text, as Unicode's bidi algorithm has trailing
        // whitespace (UAX #9, L1).
        //
        // The engine places carets inside a line so, and those are taken from it. At a line's ends it does not: in a
        // right-to-left text over several lines it puts the text's start and end on other lines, the place before a line
        // break at its line's start and a wrapped line's trailing space before its box, and finds points past a line's
        // ends on other lines. So each line's ends are found here, from where its characters reach (see LineSpan), and a
        // point is matched to the caret nearest it on the line it is level with.

        /// <summary>
        /// The caret at a source index, in layout pixels: zero wide, at the insertion point, as tall as the line the
        /// index is on. An index where a line wraps is drawn at the end of the line before when
        /// <paramref name="upstream"/>, otherwise at the start of the line after. Where the text reads both ways, at
        /// the edge of the character that reads nearer the text's own direction, and at a line's start or end, at the
        /// side the line starts from or ends at as the text reads (the end of a right-to-left line at its left). In an
        /// empty text it sits at the side of the box the text is aligned to, as tall as the font's line height.
        /// </summary>
        public Rect GetCaretRect(int sourceIndex, bool upstream = false)
        {
            if (!_handle.IsLaidOut)
                return EmptyTextCaret();
            int index = LayoutIndex(sourceIndex);
            if (index == _layoutText.Length && EndsWithBreak())
                return TrailingLineCaret();
            var span = LineSpanOf(LineOfCaret(index, upstream));
            return new Rect(CaretX(in span, index), span.Top, 0f, span.Height);
        }

        /// <summary>
        /// The source index of the caret position nearest a point in layout pixels: on the line the point is level
        /// with (above the first line, the first; below the last, the last), the position whose caret is nearest across,
        /// so past either end of a line, that end. Positions inside what the engine draws as one (a letter and its vowel
        /// marks, a ligature, an emoji sequence) are not matched.
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
            if (!TryLineAt(pointPx.y, out int line))
                return _layoutText.ToSource(_layoutText.Length);
            var span = LineSpanOf(line);
            int nearest = span.First;
            float distance = Mathf.Abs(pointPx.x - span.Start);
            for (int index = span.First + 1; index <= span.Stop; index++)
            {
                if (!IsCaretStop(in span, index))
                    continue;
                float d = Mathf.Abs(pointPx.x - CaretX(in span, index));
                if (d < distance)
                {
                    nearest = index;
                    distance = d;
                }
            }
            return LineEndToSource(in span, line, nearest, out upstream);
        }

        /// <summary>
        /// Whether the character at a source index reads right to left: a letter by its script, a number never, and a
        /// space or punctuation as the text around it has it laid out (the paragraph's way at a line's end).
        /// </summary>
        public bool IsCharacterRightToLeft(int sourceIndex)
        {
            if (!_handle.IsLaidOut)
                return _rightToLeft;
            int index = LayoutIndex(sourceIndex);
            return index < _layoutText.Length ? CharacterIsRightToLeft(index) : _rightToLeft;
        }

        /// <summary>
        /// A caret-shaped rect (zero wide, as tall as the line) at the leading or trailing edge of the character at a
        /// source index, in that character's own direction: its left, or for <paramref name="leading"/> false its right,
        /// in a left-to-right run, and the other way about in a right-to-left one. Where a selection's handles stand, as
        /// Android stands them and as iOS's selection rects place them: the start handle at the leading edge of the first
        /// character selected and the end handle at the trailing edge of the last, beside them however the text around
        /// them reads. The text's end, or a line break, at the end of its line.
        /// </summary>
        public Rect GetCharacterEdgeRect(int sourceIndex, bool leading)
        {
            if (!_handle.IsLaidOut)
                return EmptyTextCaret();
            int index = LayoutIndex(sourceIndex);
            if (index >= _layoutText.Length)
                return GetCaretRect(sourceIndex);
            var span = LineSpanOf(_handle.LineOf(index));
            return new Rect(EdgeX(in span, index, leading), span.Top, 0f, span.Height);
        }

        /// <summary>
        /// The source index whose character edge (see <see cref="GetCharacterEdgeRect"/>) is nearest a point in layout
        /// pixels, on the line the point is level with: for <paramref name="leading"/>, the index of a character whose
        /// leading edge is nearest (where a selection's start handle is dragged to); otherwise the index after a
        /// character whose trailing edge is nearest (where its end handle is dragged to), taken upstream when that is the
        /// end of a wrapped line.
        /// </summary>
        public int GetIndexAtEdge(Vector2 pointPx, bool leading, out bool upstream)
        {
            upstream = false;
            if (!_handle.IsLaidOut)
                return 0;
            if (!TryLineAt(pointPx.y, out int line))
                return _layoutText.ToSource(_layoutText.Length);
            var span = LineSpanOf(line);
            int nearest = leading ? span.First : span.Stop;
            float distance = float.PositiveInfinity;
            for (int index = span.First; index < span.Next; index++)
            {
                if (index < span.Stop && !StartsDrawnCharacter(index))
                    continue;
                float d = Mathf.Abs(pointPx.x - EdgeX(in span, index, leading));
                if (d < distance)
                {
                    nearest = leading ? index : Mathf.Min(NextCharacter(index, span.Next), span.Stop);
                    distance = d;
                }
            }
            if (leading)
                return _layoutText.ToSource(nearest);
            return LineEndToSource(in span, line, nearest, out upstream);
        }

        /// <summary>
        /// The caret position beside the caret at a source index on screen, to its right or its left, as the arrow keys
        /// move a caret in iOS, macOS and Android text views: across the line by where its carets stand, which in text
        /// that reads both ways is not the order of the indices. Past the end of a line, as the text reads, it goes on
        /// to the start of the next line, and past its start to the end of the line before (taken upstream where that
        /// line wraps, as <paramref name="besideUpstream"/> says); where a line wraps, the end of one line and the start
        /// of the next are one place in the text, so it goes on a place along the line it comes to, and each move goes
        /// through the text. At the text's first or last place, the same index.
        /// </summary>
        public int GetVisualNeighbour(int sourceIndex, bool upstream, bool toRight, out bool besideUpstream)
        {
            besideUpstream = false;
            if (!_handle.IsLaidOut)
                return sourceIndex;
            int length = _layoutText.Length;
            int index = LayoutIndex(sourceIndex);
            int last = LastLine();
            // Forward in reading order: to the right in left-to-right text, to the left in right-to-left text.
            bool forward = toRight != _rightToLeft;
            if (index == length && EndsWithBreak())
            {
                // On the empty line the text ends with: only back, to the end of the line before it.
                if (forward)
                    return sourceIndex;
                return _layoutText.ToSourceEnd(LineSpanOf(last).Stop);
            }

            int line = LineOfCaret(index, upstream);
            var span = LineSpanOf(line);
            int found = StopBeside(in span, index, CaretX(in span, index), toRight);
            if (found >= 0)
                return LineEndToSource(in span, line, found, out besideUpstream);
            // Past the line's end, on to the next line's start, and past its start back to the end of the line before.
            // Where a line wraps those are one place in the text, the end of one line and the start of the next: the
            // move goes on a place along the line it comes to, so each press moves the caret through the text, as
            // Android's getOffsetToLeftOf and macOS's moveLeft: do.
            if (forward)
            {
                if (line == last)
                    // Past the last line with characters on it: the empty line a final line break ends the text with.
                    return EndsWithBreak() ? _layoutText.ToSource(length) : sourceIndex;
                var after = LineSpanOf(line + 1);
                if (after.First == index && (found = StopBeside(in after, index, after.Start, toRight)) >= 0)
                    return LineEndToSource(in after, line + 1, found, out besideUpstream);
                return _layoutText.ToSource(after.First);
            }
            if (line == 0)
                return sourceIndex;
            var before = LineSpanOf(line - 1);
            if (before.Stop == index && (found = StopBeside(in before, index, before.End, toRight)) >= 0)
                return LineEndToSource(in before, line - 1, found, out besideUpstream);
            return LineEndToSource(in before, line - 1, before.Stop, out besideUpstream);
        }

        // The caret stop on a line nearest a place across it, to its right or its left, other than a layout index (the
        // caret's own); -1 with none that way.
        private int StopBeside(in LineSpan span, int index, float from, bool toRight)
        {
            int found = -1;
            float distance = float.PositiveInfinity;
            for (int stop = span.First; stop <= span.Stop; stop++)
            {
                if (stop == index || !IsCaretStop(in span, stop))
                    continue;
                float d = CaretX(in span, stop) - from;
                if (!toRight)
                    d = -d;
                if (d > 0.01f && d < distance)
                {
                    found = stop;
                    distance = d;
                }
            }
            return found;
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

        // A line of the last layout: the layout indices it runs over, where its text stops, and where it starts and
        // ends across and down. Start and End are its sides as the text reads (a left-to-right line starts at its
        // left, a right-to-left one at its right); TextEnd is where its text reaches before the spaces at its end.
        private struct LineSpan
        {
            public bool Known;
            // Its first layout index, and the next line's (the text's length after the last line).
            public int First, Next;
            // Where its text ends before its line break (Next, without one), and before the spaces at its end. A line
            // of nothing but spaces has no spaces at its end: they are its text.
            public int Stop, Content;
            public float Start, TextEnd, End, Top, Height;

            public float Bottom => Top + Height;
        }

        // A line's span, found the first time it is asked for after a layout. Its start is the side its text starts from
        // and its text's end the side its text reaches, from the engine's rectangles for its text, which in a line that
        // reads both ways are several. The spaces at its end (Unicode's whitespace and segment separators, not a
        // no-break space, which belongs to the text around it) then stand after its text, as wide as they are drawn: the
        // engine puts a wrapped line's trailing space before the start of the layout's box in right-to-left text. A line
        // of nothing but its line break starts and ends at the side of the box the text is aligned to.
        private LineSpan LineSpanOf(int line)
        {
            while (_lineSpans.Count <= line)
                _lineSpans.Add(default);
            var span = _lineSpans[line];
            if (span.Known)
                return span;

            var text = _layoutText.Text;
            span.Known = true;
            span.First = LayoutLineStart(line);
            span.Next = LayoutLineStart(line + 1);
            span.Stop = span.Next > span.First && text[span.Next - 1] == '\n' ? span.Next - 1 : span.Next;
            span.Content = span.Stop;
            while (span.Content > span.First && Bidi.KindOf(text[span.Content - 1]) == Bidi.Kind.Space)
                span.Content--;
            if (span.Content == span.First)
                span.Content = span.Stop;

            float top, bottom;
            if (span.Stop > span.First && Extent(span.First, span.Content, out float left, out float right, out top, out bottom))
            {
                span.Start = _rightToLeft ? right : left;
                span.TextEnd = _rightToLeft ? left : right;
                float room = Width(span.Content, span.Stop);
                span.End = _rightToLeft ? span.TextEnd - room : span.TextEnd + room;
            }
            else
            {
                // Only a line break: its box gives the line's place down, not across.
                if (!Extent(span.First, span.Next, out _, out _, out top, out bottom))
                {
                    var caret = _handle.CaretRectPx(span.First);
                    top = caret.yMin;
                    bottom = caret.yMax;
                }
                span.Start = span.TextEnd = span.End = AlignedX();
            }
            span.Top = top;
            span.Height = bottom - top;
            _lineSpans[line] = span;
            return span;
        }

        // The caret's place across at a layout index on a line: the line's start, the engine's caret inside its text, past
        // its text by the room of the spaces before the index at its end, and the line's end.
        private float CaretX(in LineSpan span, int index)
        {
            if (index <= span.First)
                return span.Start;
            if (index >= span.Stop)
                return span.End;
            if (index < span.Content)
                return InteriorCaretX(index);
            float room = Width(span.Content, index);
            return _rightToLeft ? span.TextEnd - room : span.TextEnd + room;
        }

        // The engine's caret at a layout index inside a line's text, asked once per index after a layout.
        private float InteriorCaretX(int index)
        {
            EnsureCaches(index);
            if (float.IsNaN(_caretX[index]))
                _caretX[index] = _handle.CursorPositionPx(index).x;
            return _caretX[index];
        }

        // Where the leading or the trailing edge of the character at a layout index on a line stands across, in that
        // character's own direction. The spaces at the line's end and its line break stand after its text, as the
        // paragraph reads; a character drawn as part of the one before it (a mark, the rest of a ligature) has no box of
        // its own, so its edges are the carets on either side of it.
        private float EdgeX(in LineSpan span, int index, bool leading)
        {
            if (index >= span.Stop)
                return span.End;
            if (index >= span.Content)
                return CaretX(in span, leading ? index : NextCharacter(index, span.Stop));
            int next = NextCharacter(index, span.Content);
            if (!Extent(index, next, out float left, out float right, out _, out _) || right - left < 0.01f)
                return CaretX(in span, leading ? index : next);
            return leading != CharacterIsRightToLeft(index) ? left : right;
        }

        // Whether the character at a layout index reads right to left. A letter reads its script's way and a number left
        // to right, as Unicode has them (UAX #9: strong characters and numbers keep their direction whatever surrounds
        // them, short of an explicit override). A space or punctuation takes its direction from the text around it, so it
        // reads as the run the engine laid it out in: the caret before it stands at the edge it starts from when the
        // character before it shares its run, and the caret after it at the edge it ends at when the one after does. At
        // a line's end, or alone between runs, it reads the paragraph's way, as Unicode has trailing whitespace and the
        // neutrals between runs of either direction. Found once per character after a layout.
        private bool CharacterIsRightToLeft(int index)
        {
            EnsureCaches(index);
            if (_direction[index] != 0)
                return _direction[index] == 2;
            bool rightToLeft;
            switch (Bidi.KindAt(_layoutText.Text, index))
            {
                case Bidi.Kind.RightToLeft:
                    rightToLeft = true;
                    break;
                case Bidi.Kind.LeftToRight:
                case Bidi.Kind.Number:
                    rightToLeft = false;
                    break;
                default:
                    rightToLeft = NeutralIsRightToLeft(index);
                    break;
            }
            _direction[index] = rightToLeft ? (byte)2 : (byte)1;
            return rightToLeft;
        }

        private bool NeutralIsRightToLeft(int index)
        {
            var span = LineSpanOf(_handle.LineOf(index));
            if (index >= span.Content)
                return _rightToLeft;
            int next = NextCharacter(index, span.Content);
            if (!Extent(index, next, out float left, out float right, out _, out _) || right - left < 0.01f)
                return _rightToLeft;
            if (index > span.First)
            {
                float before = InteriorCaretX(index);
                if (Mathf.Abs(before - left) < 0.01f)
                    return false;
                if (Mathf.Abs(before - right) < 0.01f)
                    return true;
            }
            if (next < span.Content)
            {
                float after = InteriorCaretX(next);
                if (Mathf.Abs(after - right) < 0.01f)
                    return false;
                if (Mathf.Abs(after - left) < 0.01f)
                    return true;
            }
            return _rightToLeft;
        }

        // The layout index after the character at one, past what is drawn as part of it (its vowel marks, the rest of a
        // ligature or an emoji sequence, the second half of a surrogate pair), at most a limit.
        private int NextCharacter(int index, int limit)
        {
            int next = index + 1;
            while (next < limit && !StartsDrawnCharacter(next))
                next++;
            return next;
        }

        // Whether a caret stops at a layout index on a line: at its start and end, and before each character drawn with a
        // box of its own between them.
        private bool IsCaretStop(in LineSpan span, int index) =>
            index == span.First || index == span.Stop || (index > span.First && index < span.Stop && StartsDrawnCharacter(index));

        // The line a caret at a layout index stands on: at a wrap taken upstream, the line before; at the text's end, the
        // last line with characters on it.
        private int LineOfCaret(int index, bool upstream)
        {
            if (upstream && WrapsAt(index))
                return _handle.LineOf(CodePointStart(index - 1));
            int length = _layoutText.Length;
            return _handle.LineOf(index < length ? index : CodePointStart(length - 1));
        }

        // The last line with characters on it: the text's last line, or the one before the empty line a final line break
        // ends it with.
        private int LastLine() => _handle.LineOf(CodePointStart(_layoutText.Length - 1));

        // The line with characters on it a point is level with, split midway between lines: above the first, the first,
        // and below the last, the last. False for a point on the empty line a final line break ends the text with.
        private bool TryLineAt(float y, out int line)
        {
            int last = LastLine();
            line = last;
            if (EndsWithBreak() && y >= TrailingLineCaret().yMin)
                return false;
            line = 0;
            while (line < last && y >= (LineSpanOf(line).Bottom + LineSpanOf(line + 1).Top) * 0.5f)
                line++;
            return true;
        }

        // A layout index on a line as a source index, and whether a caret there is taken upstream. At the line's end it is
        // the place after the line's last shown character (before any markup closing it), taken upstream where the line
        // wraps, as the line's end is also where the next line starts.
        private int LineEndToSource(in LineSpan span, int line, int index, out bool upstream)
        {
            upstream = index == span.Stop && span.Stop == span.Next && line < LastLine();
            return index == span.Stop && index < _layoutText.Length ? _layoutText.ToSourceEnd(index) : _layoutText.ToSource(index);
        }

        // Whether the character at a layout index is drawn with a box of its own, so a caret can stand before it: not the
        // second half of a surrogate pair, a vowel or accent mark drawn on the letter before it, or a letter drawn into a
        // ligature with the one before it. Asked of the engine once per character after a layout.
        private bool StartsDrawnCharacter(int index)
        {
            EnsureCaches(index);
            if (_drawn[index] == 0)
            {
                _handle.GetRangeRects(index, index + 1, _rects);
                bool drawn = false;
                for (int i = 0; i < _rects.Count && !drawn; i++)
                    drawn = _rects[i].width > 0f;
                _drawn[index] = drawn ? (byte)1 : (byte)2;
            }
            return _drawn[index] == 1;
        }

        // Room for what is found once per index after a layout, up to an index (and the text's length).
        private void EnsureCaches(int index)
        {
            if (_drawn.Length > index)
                return;
            int size = Mathf.Max(index + 1, _layoutText.Length + 1);
            int was = _caretX.Length;
            Array.Resize(ref _drawn, size);
            Array.Resize(ref _direction, size);
            Array.Resize(ref _caretX, size);
            for (int i = was; i < size; i++)
                _caretX[i] = float.NaN;
        }

        // Forgets what was found for the last layout.
        private void ClearQueries()
        {
            _lineSpans.Clear();
            Array.Clear(_drawn, 0, _drawn.Length);
            Array.Clear(_direction, 0, _direction.Length);
            for (int i = 0; i < _caretX.Length; i++)
                _caretX[i] = float.NaN;
        }

        // How far across and down the engine's rectangles for the layout indices in [start, end) reach: one per run, a
        // line whose text reads both ways having several. False when it gives none.
        private bool Extent(int start, int end, out float left, out float right, out float top, out float bottom)
        {
            _handle.GetRangeRects(start, end, _rects);
            left = top = float.PositiveInfinity;
            right = bottom = float.NegativeInfinity;
            for (int i = 0; i < _rects.Count; i++)
            {
                var r = _rects[i];
                left = Mathf.Min(left, r.xMin);
                right = Mathf.Max(right, r.xMax);
                top = Mathf.Min(top, r.yMin);
                bottom = Mathf.Max(bottom, r.yMax);
            }
            return _rects.Count > 0;
        }

        // How wide the engine draws the layout indices in [start, end), wherever it puts them.
        private float Width(int start, int end)
        {
            if (end <= start)
                return 0f;
            _handle.GetRangeRects(start, end, _rects);
            float width = 0f;
            for (int i = 0; i < _rects.Count; i++)
                width += _rects[i].width;
            return width;
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
        // caret at: it is the line below the last one with characters on it (a paragraph's spacing further down), one
        // line of the text's font tall, and starts at the side of the box the text is aligned to.
        private Rect TrailingLineCaret()
        {
            var span = LineSpanOf(LastLine());
            return new Rect(AlignedX(), span.Bottom + _paragraphSpacingPx, 0f, BaseLineHeightPx);
        }

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
