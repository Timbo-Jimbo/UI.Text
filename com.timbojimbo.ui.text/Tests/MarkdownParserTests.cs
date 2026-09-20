using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TimboJimbo.UI.Text.Markup;
using UnityEngine;

namespace TimboJimbo.UI.Text.Tests
{
    /// <summary>The parser classifies the source and records source indices; it inserts nothing.</summary>
    public class MarkdownParserTests
    {
        private TextDocument _doc;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp() => _doc = new TextDocument();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Track<T>(T handler) where T : Object
        {
            _created.Add(handler);
            return handler;
        }

        private void Parse(string source, params InlineHandler[] handlers) => MarkdownParser.Parse(source, _doc, handlers.Length > 0 ? handlers : null);

        private string TextOf(TextRun run) => _doc.Text.Substring(run.Start, run.Length);

        /// <summary>Everything the runs show, in order: the source with its markup removed.</summary>
        private string Shown() => string.Concat(_doc.Runs.Select(TextOf));

        private TextRun[] RunsOf(int line)
        {
            var l = _doc.Lines[line];
            return _doc.Runs.GetRange(l.FirstRun, l.RunCount).ToArray();
        }

        private void AssertRun(TextRun run, string text, RunStyle style)
        {
            Assert.AreEqual(text, TextOf(run));
            Assert.AreEqual(style, run.Style);
        }

        [Test]
        public void PlainTextIsOneRunOnOneLine()
        {
            Parse("Hello world");
            Assert.AreEqual(1, _doc.Lines.Count);
            Assert.AreEqual(BlockKind.Paragraph, _doc.Lines[0].Kind);
            var runs = RunsOf(0);
            Assert.AreEqual(1, runs.Length);
            AssertRun(runs[0], "Hello world", RunStyle.None);
            Assert.AreEqual(0, runs[0].Start);
        }

        [Test]
        public void EmphasisMarkersStyleTheirText()
        {
            Parse("**bold** *italic* __under__ ~~gone~~");
            Assert.AreEqual("bold italic under gone", Shown());
            var runs = _doc.Runs;
            AssertRun(runs[0], "bold", RunStyle.Bold);
            AssertRun(runs[1], " ", RunStyle.None);
            AssertRun(runs[2], "italic", RunStyle.Italic);
            AssertRun(runs[4], "under", RunStyle.Underline);
            AssertRun(runs[6], "gone", RunStyle.Strikethrough);
            Assert.AreEqual(2, runs[0].Start);
        }

        [Test]
        public void EmphasisNests()
        {
            Parse("***both*** and **bold *and italic***");
            Assert.AreEqual("both and bold and italic", Shown());
            var runs = _doc.Runs;
            AssertRun(runs[0], "both", RunStyle.Bold | RunStyle.Italic);
            AssertRun(runs[1], " and ", RunStyle.None);
            AssertRun(runs[2], "bold ", RunStyle.Bold);
            AssertRun(runs[3], "and italic", RunStyle.Bold | RunStyle.Italic);
        }

        [Test]
        public void UnmatchedAndIntrawordMarkersStayLiteral()
        {
            const string source = "a * b snake_case_name 2*3";
            Parse(source);
            Assert.AreEqual(1, _doc.Runs.Count);
            AssertRun(_doc.Runs[0], source, RunStyle.None);
        }

        [Test]
        public void EscapesShowTheCharacter()
        {
            Parse(@"\*not italic\* and \`not code\`");
            Assert.AreEqual("*not italic* and `not code`", Shown());
            Assert.IsTrue(_doc.Runs.All(r => r.Style == RunStyle.None));
            Assert.AreEqual(0, _doc.Tokens.Count);
        }

        [Test]
        public void BackticksMakeACodeToken()
        {
            Parse("run `git status` now");
            Assert.AreEqual("run git status now", Shown());
            AssertRun(_doc.Runs[0], "run ", RunStyle.None);
            AssertRun(_doc.Runs[1], "git status", RunStyle.Code);
            Assert.AreEqual(0, _doc.Runs[1].TokenIndex);
            AssertRun(_doc.Runs[2], " now", RunStyle.None);
            Assert.AreEqual(-1, _doc.Runs[2].TokenIndex);

            var token = _doc.Tokens.Single();
            Assert.AreEqual(MarkdownParser.CodeKind, token.Kind);
            Assert.AreEqual("git status", token.Payload);
            Assert.AreEqual("`git status`", token.Raw);
            Assert.AreEqual(4, token.Start);
            Assert.AreEqual(12, token.Length);
            Assert.IsNull(token.Handler);
        }

        [Test]
        public void MarkersInsideCodeAreLiteral()
        {
            Parse("`**not bold**`");
            Assert.AreEqual(1, _doc.Runs.Count);
            AssertRun(_doc.Runs[0], "**not bold**", RunStyle.Code);
        }

        [Test]
        public void LinksRecordHrefAndLabel()
        {
            Parse("see [the docs](https://example.com/docs) now");
            Assert.AreEqual("see the docs now", Shown());
            AssertRun(_doc.Runs[1], "the docs", RunStyle.Link);
            Assert.AreEqual(0, _doc.Runs[1].LinkIndex);
            Assert.AreEqual(-1, _doc.Runs[0].LinkIndex);
            Assert.AreEqual("https://example.com/docs", _doc.Links[0].Href);
            Assert.AreEqual("the docs", _doc.Links[0].Label);
        }

        [Test]
        public void BareUrlsBecomeLinksWithoutTrailingPunctuation()
        {
            const string source = "go to https://unity.com/products, then rest.";
            Parse(source);
            Assert.AreEqual(source, Shown());
            var link = _doc.Runs.Single(r => (r.Style & RunStyle.Link) != 0);
            Assert.AreEqual("https://unity.com/products", TextOf(link));
            Assert.AreEqual("https://unity.com/products", _doc.Links[0].Href);
        }

        [Test]
        public void LineMarkersClassifyLines()
        {
            Parse("# Title\n- item\n1. first\n> quoted");
            Assert.AreEqual(4, _doc.Lines.Count);
            Assert.AreEqual(BlockKind.Heading1, _doc.Lines[0].Kind);
            Assert.AreEqual(BlockKind.ListItem, _doc.Lines[1].Kind);
            Assert.IsNull(_doc.Lines[1].ListLabel);
            Assert.AreEqual(BlockKind.ListItem, _doc.Lines[2].Kind);
            Assert.AreEqual("1.", _doc.Lines[2].ListLabel);
            Assert.AreEqual(BlockKind.Quote, _doc.Lines[3].Kind);
            Assert.AreEqual("Title", TextOf(RunsOf(0).Single()));
            Assert.AreEqual("item", TextOf(RunsOf(1).Single()));
            Assert.AreEqual("first", TextOf(RunsOf(2).Single()));
            Assert.AreEqual("quoted", TextOf(RunsOf(3).Single()));
        }

        [Test]
        public void OnlyThreeHeadingLevels()
        {
            Parse("## Two\n### Three\n#### Not a heading");
            Assert.AreEqual(BlockKind.Heading2, _doc.Lines[0].Kind);
            Assert.AreEqual(BlockKind.Heading3, _doc.Lines[1].Kind);
            Assert.AreEqual(BlockKind.Paragraph, _doc.Lines[2].Kind);
            Assert.AreEqual("#### Not a heading", TextOf(RunsOf(2).Single()));
        }

        [Test]
        public void FencesDelimitACodeBlock()
        {
            Parse("before\n```\nvar x = 1;\nreturn x;\n```\nafter");
            Assert.AreEqual(4, _doc.Lines.Count);
            Assert.AreEqual(BlockKind.Paragraph, _doc.Lines[0].Kind);
            Assert.AreEqual(BlockKind.CodeBlock, _doc.Lines[1].Kind);
            Assert.AreEqual(BlockKind.CodeBlock, _doc.Lines[2].Kind);
            Assert.AreEqual(BlockKind.Paragraph, _doc.Lines[3].Kind);
            AssertRun(RunsOf(1).Single(), "var x = 1;", RunStyle.Code);
            AssertRun(RunsOf(2).Single(), "return x;", RunStyle.Code);
            Assert.AreEqual(0, RunsOf(2).Single().TokenIndex);

            var token = _doc.Tokens.Single();
            Assert.AreEqual(MarkdownParser.CodeBlockKind, token.Kind);
            Assert.AreEqual("var x = 1;\nreturn x;", token.Payload);
            Assert.AreEqual(11, token.Start);
            Assert.AreEqual(20, token.Length);
            Assert.AreEqual("```\nvar x = 1;\nreturn x;\n```", token.Raw);
        }

        [Test]
        public void CodeBlockWithoutContentIsEmpty()
        {
            Parse("```\n```");
            Assert.AreEqual(0, _doc.Lines.Count);
            var token = _doc.Tokens.Single();
            Assert.AreEqual("", token.Payload);
            Assert.AreEqual(0, token.Length);
        }

        [Test]
        public void NewlinesOfBothKindsSplitLines()
        {
            Parse("one\ntwo\r\nthree");
            Assert.AreEqual(3, _doc.Lines.Count);
            Assert.AreEqual("one", TextOf(RunsOf(0).Single()));
            Assert.AreEqual("two", TextOf(RunsOf(1).Single()));
            Assert.AreEqual("three", TextOf(RunsOf(2).Single()));
        }

        [Test]
        public void TrailingNewlineMakesAnEmptyLastLine()
        {
            Parse("a\n");
            Assert.AreEqual(2, _doc.Lines.Count);
            Assert.AreEqual(0, _doc.Lines[1].RunCount);
        }

        [Test]
        public void LeadingWhitespaceDoesNotHideAMarker()
        {
            Parse("  > quoted");
            Assert.AreEqual(BlockKind.Quote, _doc.Lines[0].Kind);
            Assert.AreEqual("quoted", TextOf(RunsOf(0).Single()));
        }

        [Test]
        public void HandlerTokensKeepTheirRawRangeWhateverThePlacement()
        {
            foreach (var placement in new[] { InlinePlacement.Fragments, InlinePlacement.Atomic })
            {
                var handler = Track(DelimitedHandler.Create(placement));
                Parse("hi :smile: there", handler);
                Assert.AreEqual("hi :smile: there", Shown());
                AssertRun(_doc.Runs[1], ":smile:", RunStyle.None);
                Assert.AreEqual(0, _doc.Runs[1].TokenIndex);
                var token = _doc.Tokens.Single();
                Assert.AreEqual("emote", token.Kind);
                Assert.AreEqual("smile", token.Payload);
                Assert.AreEqual(":smile:", token.Raw);
                Assert.AreEqual(3, token.Start);
                Assert.AreEqual(7, token.Length);
                Assert.AreSame(handler, token.Handler);
            }
        }

        [Test]
        public void PrefixHandlerNeedsAWordBoundary()
        {
            var handler = Track(PrefixHandler.Create());
            Parse("ping @timbo and mail@example.com", handler);
            Assert.AreEqual(1, _doc.Tokens.Count);
            Assert.AreEqual("timbo", _doc.Tokens[0].Payload);
            Assert.AreEqual(5, _doc.Tokens[0].Start);
            Assert.AreEqual(6, _doc.Tokens[0].Length);
        }

        [Test]
        public void HandlerCanDeclineATokenSoItStaysText()
        {
            var handler = Track(DelimitedHandler.Create(InlinePlacement.Atomic));
            handler.Accept = payload => payload == "smile";
            const string source = "at 10:30:45 :smile:";
            Parse(source, handler);
            Assert.AreEqual(source, Shown());
            Assert.AreEqual(1, _doc.Tokens.Count);
            Assert.AreEqual(12, _doc.Tokens[0].Start);
        }

        [Test]
        public void EmptyAndNullSourcesProduceEmptyDocuments()
        {
            Parse("");
            Assert.AreEqual(0, _doc.Lines.Count);
            Assert.AreEqual(0, _doc.Runs.Count);
            Parse(null);
            Assert.AreEqual("", _doc.Text);
            Assert.AreEqual(0, _doc.Lines.Count);
        }
    }
}
