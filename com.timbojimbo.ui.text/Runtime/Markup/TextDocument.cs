using System;
using System.Collections.Generic;

namespace TimboJimbo.UI.Text.Markup
{
    /// <summary>Line-level structure a line of the source has.</summary>
    public enum BlockKind
    {
        Paragraph,
        Heading1,
        Heading2,
        Heading3,
        Quote,
        ListItem,
        CodeBlock,
    }

    /// <summary>Inline styling of a run. Flags combine.</summary>
    [Flags]
    public enum RunStyle
    {
        None = 0,
        Bold = 1 << 0,
        Italic = 1 << 1,
        Underline = 1 << 2,
        Strikethrough = 1 << 3,
        Code = 1 << 4,
        Link = 1 << 5,
    }

    /// <summary>One line of the source as the parser classified it. Fence lines produce no line.</summary>
    public struct TextLine
    {
        public BlockKind Kind;
        /// <summary>For a numbered list item, its marker as written ("1."); null for bullets and every other line.</summary>
        public string ListLabel;
        /// <summary>This line's runs, contiguous in <see cref="TextDocument.Runs"/>.</summary>
        public int FirstRun;
        public int RunCount;
    }

    /// <summary>
    /// A range of the source text that is shown as written, with one uniform style. Markup delimiters are the
    /// gaps between runs. Indices are UTF-16 offsets into <see cref="TextDocument.Text"/>. A run never crosses a line.
    /// </summary>
    public struct TextRun
    {
        public int Start;
        public int Length;
        public RunStyle Style;
        /// <summary>Index into <see cref="TextDocument.Lines"/>.</summary>
        public int Line;
        /// <summary>Index into <see cref="TextDocument.Links"/>, or -1.</summary>
        public int LinkIndex;
        /// <summary>Index into <see cref="TextDocument.Tokens"/>, or -1.</summary>
        public int TokenIndex;

        public int End => Start + Length;
    }

    public struct TextLink
    {
        public string Href;
        public string Label;
    }

    /// <summary>A piece of markup a handler owns: inline code, an emote, a mention. The parser fills these.</summary>
    public struct InlineToken
    {
        /// <summary>The handler's kind, or "code" / "codeblock" for the built-in code syntax.</summary>
        public string Kind;
        /// <summary>The wrapped text: "code" for `code`, "name" for @name.</summary>
        public string Payload;
        /// <summary>The full source token including its delimiters, for copy and export.</summary>
        public string Raw;
        /// <summary>Range of the whole token in the source text, delimiters included.</summary>
        public int Start;
        public int Length;
        /// <summary>The handler that claimed the token, or null for the built-in kinds, which the theme resolves.</summary>
        public InlineHandler Handler;

        public int End => Start + Length;
    }

    /// <summary>
    /// The parsed form of a source text: its lines, the runs of source text that are shown, and the links and
    /// tokens the markup declared, all addressed in source indices. What the generator lays out is derived from
    /// this by <see cref="LayoutText"/>. Reusable; <see cref="Reset"/> keeps the allocations.
    /// </summary>
    public sealed class TextDocument
    {
        /// <summary>The source text as given.</summary>
        public string Text { get; private set; } = "";
        public readonly List<TextLine> Lines = new();
        public readonly List<TextRun> Runs = new();
        public readonly List<TextLink> Links = new();
        public readonly List<InlineToken> Tokens = new();

        public void Reset(string source)
        {
            Text = source ?? "";
            Lines.Clear();
            Runs.Clear();
            Links.Clear();
            Tokens.Clear();
        }
    }
}
