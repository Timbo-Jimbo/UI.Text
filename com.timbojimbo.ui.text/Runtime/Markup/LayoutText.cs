using System.Collections.Generic;
using System.Text;

namespace TimboJimbo.UI.Text.Markup
{
    /// <summary>What a run of the layout text is: copied source, or something the layout inserted.</summary>
    public enum LayoutRunKind
    {
        /// <summary>Source text shown as written.</summary>
        Text,
        /// <summary>The em space that indents quotes and list items.</summary>
        Indent,
        /// <summary>A list bullet or number, followed by a space.</summary>
        Marker,
        /// <summary>No-break spaces that reserve layout padding around a token's text.</summary>
        Pad,
        /// <summary>The placeholder character an atomic token becomes.</summary>
        Object,
    }

    /// <summary>A range of the layout text with one uniform style. Indices are into <see cref="LayoutText.Text"/>.</summary>
    public struct LayoutRun
    {
        public int Start;
        public int Length;
        public LayoutRunKind Kind;
        public RunStyle Style;
        public BlockKind Block;
        /// <summary>Index into <see cref="TextDocument.Links"/>, or -1.</summary>
        public int LinkIndex;
        /// <summary>Index into <see cref="TextDocument.Tokens"/>, or -1.</summary>
        public int TokenIndex;

        public int End => Start + Length;
    }

    /// <summary>Where a document token landed in the layout text. Index-aligned with <see cref="TextDocument.Tokens"/>.</summary>
    public struct LayoutToken
    {
        /// <summary>Range in the layout text: the text plus its pads, or the one placeholder character. Start is -1 when the token produced nothing.</summary>
        public int Start;
        public int Length;
        /// <summary>The handler that renders the token: the token's own, or the theme's for a built-in kind. May be null.</summary>
        public InlineHandler Handler;
        public InlinePlacement Placement;
        /// <summary>Layout padding either side of the text, in em of the token's font size; 0 for none.</summary>
        public float PaddingEm;

        public int End => Start + Length;
        public bool IsPlaced => Start >= 0;
    }

    /// <summary>
    /// The text the generator lays out, built from a <see cref="TextDocument"/>: its shown runs copied, with
    /// everything the layout adds inserted here and nowhere else. Newlines between lines, an em space to indent
    /// quotes and list items, list markers, no-break spaces as layout padding around tokens that ask for it, and
    /// one placeholder character per atomic token. Keeps the mapping between source and layout indices, so a
    /// caret, a selection or a hit test can move between the two.
    /// </summary>
    public sealed class LayoutText
    {
        /// <summary>
        /// Layout padding is a run of no-break spaces either side of the text, sized by the span builder. The
        /// no-break space breaks exactly as padding should: a line may break after an ordinary space before it,
        /// never between it and the text it pads.
        /// </summary>
        public const char Pad = ' ';
        /// <summary>No-break spaces per pad run; two let a run be sized up to twice a space of the token's font.</summary>
        public const int PadCount = 2;
        /// <summary>Quotes and list items indent with an em space; the generator applies a span indent per span rather than per line.</summary>
        public const char Indent = ' ';
        public const char Bullet = '•';
        /// <summary>The character an atomic token becomes; a sprite span over it reserves the object's box.</summary>
        public const char ObjectChar = Bridge.AtgSpriteAssets.PlaceholderCodePoint;

        private const string BulletMarker = "• ";
        private static readonly string s_Pads = new(Pad, PadCount);

        private struct Segment
        {
            public int LayoutStart;
            public int Length;
            /// <summary>Source index of the first character, or -1 for inserted text.</summary>
            public int SourceStart;
        }

        private readonly StringBuilder _text = new();
        private readonly List<Segment> _segments = new();
        private readonly List<int> _tokenFirstRun = new();
        private readonly List<int> _tokenLastRun = new();
        private string _string = "";
        private bool _stringDirty;
        private int _sourceLength;

        public readonly List<LayoutRun> Runs = new();
        public readonly List<LayoutToken> Tokens = new();

        /// <summary>The layout text, cached until the next build.</summary>
        public string Text
        {
            get
            {
                if (_stringDirty)
                {
                    _string = _text.ToString();
                    _stringDirty = false;
                }
                return _string;
            }
        }

        public int Length => _text.Length;

        /// <summary>The source shown as is: one copied segment and no runs. For texts with rich text off.</summary>
        public void BuildPlain(string source)
        {
            Clear();
            _sourceLength = source?.Length ?? 0;
            if (_sourceLength > 0)
            {
                _text.Append(source);
                _segments.Add(new Segment { LayoutStart = 0, Length = _sourceLength, SourceStart = 0 });
            }
            _stringDirty = true;
        }

        /// <summary>Builds the layout text for a parsed document. The theme resolves handlers for the built-in token kinds.</summary>
        public void Build(TextDocument document, TextTheme theme)
        {
            Clear();
            _sourceLength = document.Text.Length;

            for (int t = 0; t < document.Tokens.Count; t++)
            {
                var token = document.Tokens[t];
                var handler = token.Handler != null ? token.Handler : theme != null ? theme.FindHandler(token.Kind) : null;
                var placement = handler != null ? handler.Placement : InlinePlacement.Fragments;
                float padding = handler != null && placement != InlinePlacement.Atomic && handler.PaddingEm > 0f ? handler.PaddingEm : 0f;
                Tokens.Add(new LayoutToken { Start = -1, Handler = handler, Placement = placement, PaddingEm = padding });
                _tokenFirstRun.Add(-1);
                _tokenLastRun.Add(-1);
            }
            for (int r = 0; r < document.Runs.Count; r++)
            {
                int t = document.Runs[r].TokenIndex;
                if (t < 0) continue;
                if (_tokenFirstRun[t] < 0) _tokenFirstRun[t] = r;
                _tokenLastRun[t] = r;
            }

            for (int li = 0; li < document.Lines.Count; li++)
            {
                if (li > 0)
                    Insert("\n");
                var line = document.Lines[li];

                switch (line.Kind)
                {
                    case BlockKind.Quote:
                        InsertRun(Indent.ToString(), LayoutRunKind.Indent, line.Kind, RunStyle.None, -1, -1);
                        break;
                    case BlockKind.ListItem:
                        InsertRun(Indent.ToString(), LayoutRunKind.Indent, line.Kind, RunStyle.None, -1, -1);
                        InsertRun(line.ListLabel != null ? line.ListLabel + " " : BulletMarker, LayoutRunKind.Marker, line.Kind, RunStyle.None, -1, -1);
                        break;
                    case BlockKind.CodeBlock:
                        // Every line of a block gets the padding, so a full-width background insets its text.
                        if (line.RunCount > 0)
                        {
                            var first = document.Runs[line.FirstRun];
                            if (first.TokenIndex >= 0 && Tokens[first.TokenIndex].PaddingEm > 0f)
                                InsertRun(s_Pads, LayoutRunKind.Pad, line.Kind, first.Style, -1, first.TokenIndex);
                        }
                        break;
                }

                for (int ri = 0; ri < line.RunCount; ri++)
                {
                    int runIndex = line.FirstRun + ri;
                    var run = document.Runs[runIndex];
                    int t = run.TokenIndex;
                    if (t >= 0 && Tokens[t].Placement == InlinePlacement.Atomic)
                    {
                        InsertRun(ObjectChar.ToString(), LayoutRunKind.Object, line.Kind, run.Style, run.LinkIndex, t);
                        continue;
                    }
                    bool padded = t >= 0 && Tokens[t].PaddingEm > 0f && line.Kind != BlockKind.CodeBlock;
                    if (padded && _tokenFirstRun[t] == runIndex)
                        InsertRun(s_Pads, LayoutRunKind.Pad, line.Kind, run.Style, -1, t);
                    Copy(document.Text, run.Start, run.Length, line.Kind, run.Style, run.LinkIndex, t);
                    if (padded && _tokenLastRun[t] == runIndex)
                        InsertRun(s_Pads, LayoutRunKind.Pad, line.Kind, run.Style, -1, t);
                }
            }
            _stringDirty = true;
        }

        /// <summary>The layout index of a source index. Inside skipped markup it is the next shown character; past the end, the layout length.</summary>
        public int ToLayout(int sourceIndex)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                var s = _segments[i];
                if (s.SourceStart < 0)
                    continue;
                if (sourceIndex < s.SourceStart)
                    return s.LayoutStart;
                if (sourceIndex < s.SourceStart + s.Length)
                    return s.LayoutStart + (sourceIndex - s.SourceStart);
            }
            return _text.Length;
        }

        /// <summary>The source index of a layout index. Inside inserted text it is the next shown source character; past the end, the source length.</summary>
        public int ToSource(int layoutIndex)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                var s = _segments[i];
                if (layoutIndex >= s.LayoutStart + s.Length)
                    continue;
                if (s.SourceStart >= 0)
                    return s.SourceStart + (layoutIndex - s.LayoutStart);
                for (int j = i + 1; j < _segments.Count; j++)
                    if (_segments[j].SourceStart >= 0)
                        return _segments[j].SourceStart;
                return _sourceLength;
            }
            return _sourceLength;
        }

        private void Clear()
        {
            _text.Clear();
            _segments.Clear();
            _tokenFirstRun.Clear();
            _tokenLastRun.Clear();
            Runs.Clear();
            Tokens.Clear();
            _string = "";
            _stringDirty = false;
        }

        private void Copy(string source, int start, int length, BlockKind block, RunStyle style, int linkIndex, int tokenIndex)
        {
            if (length <= 0)
                return;
            int layoutStart = _text.Length;
            _text.Append(source, start, length);
            _segments.Add(new Segment { LayoutStart = layoutStart, Length = length, SourceStart = start });
            AddRun(layoutStart, length, LayoutRunKind.Text, block, style, linkIndex, tokenIndex);
        }

        private void Insert(string text)
        {
            int layoutStart = _text.Length;
            _text.Append(text);
            _segments.Add(new Segment { LayoutStart = layoutStart, Length = text.Length, SourceStart = -1 });
        }

        private void InsertRun(string text, LayoutRunKind kind, BlockKind block, RunStyle style, int linkIndex, int tokenIndex)
        {
            int layoutStart = _text.Length;
            Insert(text);
            AddRun(layoutStart, text.Length, kind, block, style, linkIndex, tokenIndex);
        }

        private void AddRun(int start, int length, LayoutRunKind kind, BlockKind block, RunStyle style, int linkIndex, int tokenIndex)
        {
            Runs.Add(new LayoutRun { Start = start, Length = length, Kind = kind, Block = block, Style = style, LinkIndex = linkIndex, TokenIndex = tokenIndex });
            if (tokenIndex < 0)
                return;
            var token = Tokens[tokenIndex];
            if (token.Start < 0)
                token.Start = start;
            token.Length = start + length - token.Start;
            Tokens[tokenIndex] = token;
        }
    }
}
