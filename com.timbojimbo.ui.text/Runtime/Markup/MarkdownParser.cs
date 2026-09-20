using System;
using System.Collections.Generic;

namespace TimboJimbo.UI.Text.Markup
{
    /// <summary>
    /// A small Markdown dialect for user-generated and UI text, Discord-flavoured where that differs from
    /// CommonMark: a newline is a line break, `__` is underline, no HTML, no images, no tables.
    /// Inline: **bold**, *italic* or _italic_, __underline__, ~~strike~~, `code`, [label](url), bare URLs,
    /// backslash escapes, and any handler triggers. Line level: # to ### headings, - and 1. lists, > quotes,
    /// ``` fenced code blocks.
    /// The parser only classifies the source: every index it records points into the source string, and it
    /// inserts nothing. <see cref="LayoutText"/> turns the result into the text the generator lays out.
    /// </summary>
    public static class MarkdownParser
    {
        /// <summary>Token kinds of the built-in code syntax.</summary>
        public const string CodeKind = "code";
        public const string CodeBlockKind = "codeblock";

        private struct State
        {
            public RunStyle Style;
            public int LinkIndex;
            public int TokenIndex;

            public bool Same(in State other) => Style == other.Style && LinkIndex == other.LinkIndex && TokenIndex == other.TokenIndex;
        }

        private sealed class Cursor
        {
            public TextDocument Doc;
            public string Source;
            public IReadOnlyList<InlineHandler> Handlers;
            public State State;
            public int Line;
            public int RunStart;
            public int RunEnd;
            public bool RunOpen;
        }

        [ThreadStatic] private static Cursor s_Cursor;

        /// <summary>Parses <paramref name="source"/> into <paramref name="doc"/>, which is reset first.</summary>
        public static void Parse(string source, TextDocument doc, IReadOnlyList<InlineHandler> handlers = null)
        {
            doc.Reset(source);
            if (string.IsNullOrEmpty(source))
                return;

            var c = s_Cursor ??= new Cursor();
            c.Doc = doc;
            c.Source = source;
            c.Handlers = handlers;
            c.State = new State { LinkIndex = -1, TokenIndex = -1 };
            c.RunOpen = false;

            bool inCodeBlock = false;
            int codeBlockToken = -1, codeBlockStart = -1, codeBlockEnd = -1, fenceStart = 0;

            // Lines split on "\r\n" or "\n"; a trailing newline yields a final empty line, as String.Split would.
            int pos = 0, length = source.Length;
            while (pos <= length)
            {
                int lineEnd = source.IndexOf('\n', pos);
                int next;
                if (lineEnd < 0)
                {
                    lineEnd = length;
                    next = length + 1;
                }
                else
                {
                    next = lineEnd + 1;
                    if (lineEnd > pos && source[lineEnd - 1] == '\r')
                        lineEnd--;
                }

                int trimStart = pos;
                while (trimStart < lineEnd && char.IsWhiteSpace(source[trimStart]))
                    trimStart++;

                if (StartsWith(source, trimStart, lineEnd, "```"))
                {
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeBlockStart = -1;
                        fenceStart = pos;
                        codeBlockToken = doc.Tokens.Count;
                        doc.Tokens.Add(new InlineToken { Kind = CodeBlockKind, Start = pos, Payload = "", Raw = "" });
                    }
                    else
                    {
                        inCodeBlock = false;
                        CloseCodeBlock(c, codeBlockToken, fenceStart, codeBlockStart, codeBlockEnd, "```\n", "\n```");
                    }
                    pos = next;
                    continue;
                }

                c.Line = doc.Lines.Count;
                var line = new TextLine { Kind = BlockKind.Paragraph, FirstRun = doc.Runs.Count };

                if (inCodeBlock)
                {
                    if (codeBlockStart < 0)
                        codeBlockStart = pos;
                    codeBlockEnd = lineEnd;
                    line.Kind = BlockKind.CodeBlock;
                    doc.Lines.Add(line);
                    SetState(c, RunStyle.Code, -1, codeBlockToken);
                    EmitRange(c, pos, lineEnd);
                    CloseRun(c);
                    FinishLine(c);
                    pos = next;
                    continue;
                }

                int content = trimStart;
                int hashes = 0;
                while (trimStart + hashes < lineEnd && source[trimStart + hashes] == '#')
                    hashes++;
                if (hashes is >= 1 and <= 3 && trimStart + hashes < lineEnd && source[trimStart + hashes] == ' ')
                {
                    line.Kind = (BlockKind)((int)BlockKind.Heading1 + hashes - 1);
                    content = trimStart + hashes + 1;
                }
                else if (StartsWith(source, trimStart, lineEnd, "> "))
                {
                    line.Kind = BlockKind.Quote;
                    content = trimStart + 2;
                }
                else if (StartsWith(source, trimStart, lineEnd, "- ") || StartsWith(source, trimStart, lineEnd, "* ") || StartsWith(source, trimStart, lineEnd, "+ "))
                {
                    line.Kind = BlockKind.ListItem;
                    content = trimStart + 2;
                }
                else
                {
                    int digits = 0;
                    while (trimStart + digits < lineEnd && char.IsDigit(source[trimStart + digits]))
                        digits++;
                    if (digits > 0 && trimStart + digits + 1 < lineEnd && source[trimStart + digits] == '.' && source[trimStart + digits + 1] == ' ')
                    {
                        line.Kind = BlockKind.ListItem;
                        line.ListLabel = source.Substring(trimStart, digits + 1);
                        content = trimStart + digits + 2;
                    }
                }

                doc.Lines.Add(line);
                ParseInline(c, trimStart, content, lineEnd);
                CloseRun(c);
                FinishLine(c);
                pos = next;
            }

            if (inCodeBlock && codeBlockToken >= 0)
                CloseCodeBlock(c, codeBlockToken, fenceStart, codeBlockStart, codeBlockEnd, "```\n", "");
        }

        private static void FinishLine(Cursor c)
        {
            var line = c.Doc.Lines[c.Line];
            line.RunCount = c.Doc.Runs.Count - line.FirstRun;
            c.Doc.Lines[c.Line] = line;
        }

        private static void CloseCodeBlock(Cursor c, int tokenIndex, int fenceStart, int start, int end, string rawOpen, string rawClose)
        {
            var token = c.Doc.Tokens[tokenIndex];
            if (start >= 0 && end >= start)
            {
                token.Start = start;
                token.Length = end - start;
                token.Payload = c.Source.Substring(start, token.Length).Replace("\r\n", "\n");
            }
            else
            {
                token.Start = fenceStart;
                token.Length = 0;
                token.Payload = "";
            }
            token.Raw = rawOpen + token.Payload + rawClose;
            c.Doc.Tokens[tokenIndex] = token;
        }

        // ---- Inline ----

        private static void ParseInline(Cursor c, int lineStart, int start, int end)
        {
            var source = c.Source;
            var style = RunStyle.None;
            int i = start;
            while (i < end)
            {
                char ch = source[i];

                if (ch == '\\' && i + 1 < end && IsEscapable(source[i + 1]))
                {
                    SetState(c, style, -1, -1);
                    Emit(c, i + 1);
                    i += 2;
                    continue;
                }

                if (ch == '`')
                {
                    int close = IndexOf(source, '`', i + 1, end);
                    if (close > i + 1)
                    {
                        EmitCode(c, i, close);
                        i = close + 1;
                        continue;
                    }
                }

                if (TryDelimiter(source, lineStart, i, end, "**", ref style, RunStyle.Bold, ref i)) continue;
                if (TryDelimiter(source, lineStart, i, end, "__", ref style, RunStyle.Underline, ref i)) continue;
                if (TryDelimiter(source, lineStart, i, end, "~~", ref style, RunStyle.Strikethrough, ref i)) continue;
                if (ch == '*' && TryDelimiter(source, lineStart, i, end, "*", ref style, RunStyle.Italic, ref i)) continue;
                if (ch == '_' && IsWordBoundary(source, lineStart, i, 1, end) && TryDelimiter(source, lineStart, i, end, "_", ref style, RunStyle.Italic, ref i)) continue;

                if (ch == '[' && TryLink(c, style, i, end, out int linkEnd))
                {
                    i = linkEnd;
                    continue;
                }

                if (TryAutolink(c, style, lineStart, i, end, out int urlEnd))
                {
                    i = urlEnd;
                    continue;
                }

                if (c.Handlers != null && TryHandler(c, style, lineStart, i, end, out int tokenEnd))
                {
                    i = tokenEnd;
                    continue;
                }

                SetState(c, style, -1, -1);
                Emit(c, i);
                i++;
            }
        }

        private static bool TryDelimiter(string source, int lineStart, int i, int end, string delimiter, ref RunStyle style, RunStyle flag, ref int next)
        {
            int len = delimiter.Length;
            if (!StartsWith(source, i, end, delimiter))
                return false;

            bool open = (style & flag) == 0;
            if (open)
            {
                // An opener needs content right after it and a closer later on the line.
                if (i + len >= end || char.IsWhiteSpace(source[i + len]))
                    return false;
                int close = IndexOf(source, delimiter, i + len, end);
                while (close > 0 && char.IsWhiteSpace(source[close - 1]))
                    close = IndexOf(source, delimiter, close + 1, end);
                if (close < 0)
                    return false;
                style |= flag;
            }
            else
            {
                // A closer must follow content.
                if (i == lineStart || char.IsWhiteSpace(source[i - 1]))
                    return false;
                style &= ~flag;
            }
            next = i + len;
            return true;
        }

        private static bool TryLink(Cursor c, RunStyle style, int i, int end, out int next)
        {
            next = i;
            var source = c.Source;
            int labelEnd = IndexOf(source, ']', i + 1, end);
            if (labelEnd < 0 || labelEnd + 1 >= end || source[labelEnd + 1] != '(')
                return false;
            int hrefEnd = IndexOf(source, ')', labelEnd + 2, end);
            if (hrefEnd < 0)
                return false;

            var label = source.Substring(i + 1, labelEnd - i - 1);
            var href = source.Substring(labelEnd + 2, hrefEnd - labelEnd - 2).Trim();
            if (label.Length == 0 || href.Length == 0)
                return false;

            int linkIndex = c.Doc.Links.Count;
            c.Doc.Links.Add(new TextLink { Href = href, Label = label });
            SetState(c, style | RunStyle.Link, linkIndex, -1);
            EmitRange(c, i + 1, labelEnd);
            next = hrefEnd + 1;
            return true;
        }

        private static bool TryAutolink(Cursor c, RunStyle style, int lineStart, int i, int end, out int next)
        {
            next = i;
            var source = c.Source;
            if (!StartsWith(source, i, end, "http://") && !StartsWith(source, i, end, "https://"))
                return false;
            if (i > lineStart && !char.IsWhiteSpace(source[i - 1]) && source[i - 1] != '(')
                return false;

            int j = i;
            while (j < end && !char.IsWhiteSpace(source[j]) && source[j] != ')' && source[j] != ']' && source[j] != '<')
                j++;
            // Trailing punctuation belongs to the sentence, not the URL.
            while (j > i && ".,;:!?'\"".IndexOf(source[j - 1]) >= 0)
                j--;
            if (j - i <= 8)
                return false;

            var url = source.Substring(i, j - i);
            int linkIndex = c.Doc.Links.Count;
            c.Doc.Links.Add(new TextLink { Href = url, Label = url });
            SetState(c, style | RunStyle.Link, linkIndex, -1);
            EmitRange(c, i, j);
            next = j;
            return true;
        }

        private static bool TryHandler(Cursor c, RunStyle style, int lineStart, int i, int end, out int next)
        {
            next = i;
            var source = c.Source;
            for (int h = 0; h < c.Handlers.Count; h++)
            {
                var handler = c.Handlers[h];
                if (handler == null)
                    continue;
                var trigger = handler.Trigger;
                if (string.IsNullOrEmpty(trigger.Open))
                    continue;

                int payloadStart, payloadEnd, tokenEnd;
                if (trigger.Kind == InlineTriggerKind.Delimited)
                {
                    if (!StartsWith(source, i, end, trigger.Open))
                        continue;
                    var close = string.IsNullOrEmpty(trigger.Close) ? trigger.Open : trigger.Close;
                    payloadStart = i + trigger.Open.Length;
                    payloadEnd = IndexOf(source, close, payloadStart, end);
                    if (payloadEnd <= payloadStart)
                        continue;
                    tokenEnd = payloadEnd + close.Length;
                }
                else
                {
                    if (source[i] != trigger.Open[0] || (i > lineStart && IsWordChar(source[i - 1])))
                        continue;
                    payloadStart = i + 1;
                    payloadEnd = payloadStart;
                    while (payloadEnd < end && IsWordChar(source[payloadEnd]))
                        payloadEnd++;
                    if (payloadEnd == payloadStart)
                        continue;
                    tokenEnd = payloadEnd;
                }

                var payload = source.Substring(payloadStart, payloadEnd - payloadStart);
                if (!handler.Accepts(payload))
                    continue;

                int tokenIndex = c.Doc.Tokens.Count;
                c.Doc.Tokens.Add(new InlineToken
                {
                    Kind = handler.Kind,
                    Payload = payload,
                    Raw = source.Substring(i, tokenEnd - i),
                    Handler = handler,
                    Start = i,
                    Length = tokenEnd - i,
                });
                // The whole token, delimiters included, is shown as written; the layout decides whether it stays
                // text or becomes an object.
                SetState(c, style, -1, tokenIndex);
                EmitRange(c, i, tokenEnd);
                next = tokenEnd;
                return true;
            }
            return false;
        }

        private static void EmitCode(Cursor c, int open, int close)
        {
            var source = c.Source;
            int tokenIndex = c.Doc.Tokens.Count;
            var payload = source.Substring(open + 1, close - open - 1);
            c.Doc.Tokens.Add(new InlineToken { Kind = CodeKind, Payload = payload, Raw = "`" + payload + "`", Start = open, Length = close + 1 - open });
            SetState(c, RunStyle.Code, -1, tokenIndex);
            EmitRange(c, open + 1, close);
        }

        // ---- Runs ----

        private static void SetState(Cursor c, RunStyle style, int linkIndex, int tokenIndex)
        {
            var next = new State { Style = style, LinkIndex = linkIndex, TokenIndex = tokenIndex };
            if (c.RunOpen && c.State.Same(next))
                return;
            CloseRun(c);
            c.State = next;
        }

        private static void Emit(Cursor c, int index) => EmitRange(c, index, index + 1);

        private static void EmitRange(Cursor c, int start, int end)
        {
            if (end <= start)
                return;
            if (c.RunOpen && c.RunEnd == start)
            {
                c.RunEnd = end;
                return;
            }
            CloseRun(c);
            c.RunOpen = true;
            c.RunStart = start;
            c.RunEnd = end;
        }

        private static void CloseRun(Cursor c)
        {
            if (!c.RunOpen)
                return;
            if (c.RunEnd > c.RunStart)
                c.Doc.Runs.Add(new TextRun { Start = c.RunStart, Length = c.RunEnd - c.RunStart, Style = c.State.Style, Line = c.Line, LinkIndex = c.State.LinkIndex, TokenIndex = c.State.TokenIndex });
            c.RunOpen = false;
        }

        // ---- Character classes and bounded searches ----

        private static bool IsEscapable(char ch) => "\\`*_~[]()#>-+!:@".IndexOf(ch) >= 0;

        private static bool IsWordChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

        private static bool IsWordBoundary(string source, int lineStart, int i, int len, int end)
        {
            bool before = i == lineStart || !IsWordChar(source[i - 1]);
            bool after = i + len >= end || !IsWordChar(source[i + len]);
            // Intraword underscores (snake_case) are not emphasis: either side must be a boundary.
            return before || after;
        }

        private static bool StartsWith(string source, int i, int end, string value) => i + value.Length <= end && string.CompareOrdinal(source, i, value, 0, value.Length) == 0;

        private static int IndexOf(string source, char value, int from, int end) => from < end ? source.IndexOf(value, from, end - from) : -1;

        private static int IndexOf(string source, string value, int from, int end) => from < end ? source.IndexOf(value, from, end - from, StringComparison.Ordinal) : -1;
    }
}
