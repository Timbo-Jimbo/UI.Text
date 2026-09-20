using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TimboJimbo.UI.Text.Markup;
using UnityEngine;

namespace TimboJimbo.UI.Text.Tests
{
    /// <summary>The layout text owns every insertion and the mapping back to the source.</summary>
    public class LayoutTextTests
    {
        private static readonly string Pads = new(LayoutText.Pad, LayoutText.PadCount);
        private static readonly string Indent = LayoutText.Indent.ToString();

        private TextDocument _doc;
        private LayoutText _layout;
        private TextTheme _theme;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _doc = new TextDocument();
            _layout = new LayoutText();
            _theme = ScriptableObject.CreateInstance<TextTheme>();
            _created.Add(_theme);
        }

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

        private void Build(string source, params InlineHandler[] handlers)
        {
            foreach (var handler in handlers)
                _theme.Handlers.Add(handler);
            MarkdownParser.Parse(source, _doc, _theme.AllHandlers);
            _layout.Build(_doc, _theme);
        }

        private string Kinds() => string.Join(",", _layout.Runs.Select(r => r.Kind.ToString()));

        [Test]
        public void PlainBuildIsTheSourceItself()
        {
            _layout.BuildPlain("abc");
            Assert.AreEqual("abc", _layout.Text);
            Assert.AreEqual(0, _layout.Runs.Count);
            Assert.AreEqual(1, _layout.ToLayout(1));
            Assert.AreEqual(2, _layout.ToSource(2));
            Assert.AreEqual(3, _layout.ToLayout(3));
            Assert.AreEqual(3, _layout.ToSource(5));
        }

        [Test]
        public void MarkupIsDroppedAndIndicesMapAcrossTheGaps()
        {
            Build("**bold** x");
            Assert.AreEqual("bold x", _layout.Text);
            Assert.AreEqual("Text,Text", Kinds());
            Assert.AreEqual(RunStyle.Bold, _layout.Runs[0].Style);
            Assert.AreEqual(4, _layout.Runs[0].Length);

            Assert.AreEqual(0, _layout.ToLayout(2), "first shown character");
            Assert.AreEqual(0, _layout.ToLayout(0), "inside the opening marker: the next shown character");
            Assert.AreEqual(4, _layout.ToLayout(6), "inside the closing marker");
            Assert.AreEqual(2, _layout.ToSource(0));
            Assert.AreEqual(8, _layout.ToSource(4));
            Assert.AreEqual(10, _layout.ToSource(6), "past the end: the source length");
        }

        [Test]
        public void LinesJoinWithInsertedNewlines()
        {
            Build("a\nb");
            Assert.AreEqual("a\nb", _layout.Text);
            Assert.AreEqual(2, _layout.ToLayout(2));
            Assert.AreEqual(2, _layout.ToSource(1), "the inserted newline maps to the next shown source character");
        }

        [Test]
        public void QuotesAndListsGetIndentsAndMarkers()
        {
            Build("> q\n- i\n2. n");
            Assert.AreEqual($"{Indent}q\n{Indent}{LayoutText.Bullet} i\n{Indent}2. n", _layout.Text);
            Assert.AreEqual("Indent,Text,Indent,Marker,Text,Indent,Marker,Text", Kinds());
            Assert.AreEqual(BlockKind.Quote, _layout.Runs[0].Block);
            Assert.AreEqual(BlockKind.ListItem, _layout.Runs[3].Block);
            Assert.AreEqual(2, _layout.ToSource(0), "the indent maps to the quoted text");
            Assert.AreEqual(1, _layout.ToLayout(2));
        }

        [Test]
        public void PaddedTokenGetsPadsEitherSideWithinItsRange()
        {
            var code = Track(KindHandler.Create(MarkdownParser.CodeKind, InlinePlacement.Fragments, 0.2f));
            Build("run `git` now", code);
            Assert.AreEqual("run " + Pads + "git" + Pads + " now", _layout.Text);
            Assert.AreEqual("Text,Pad,Text,Pad,Text", Kinds());
            Assert.AreEqual(0, _layout.Runs[1].TokenIndex);
            Assert.AreEqual(RunStyle.Code, _layout.Runs[1].Style);

            var token = _layout.Tokens.Single();
            Assert.AreEqual(4, token.Start);
            Assert.AreEqual(3 + 2 * LayoutText.PadCount, token.Length);
            Assert.AreEqual(0.2f, token.PaddingEm);
            Assert.AreEqual(InlinePlacement.Fragments, token.Placement);
            Assert.AreSame(code, token.Handler);
        }

        [Test]
        public void CodeBlockLinesArePaddedAtTheirStart()
        {
            var block = Track(KindHandler.Create(MarkdownParser.CodeBlockKind, InlinePlacement.Block, 0.4f));
            Build("```\na\nb\n```", block);
            Assert.AreEqual(Pads + "a\n" + Pads + "b", _layout.Text);
            Assert.AreEqual("Pad,Text,Pad,Text", Kinds());
            var token = _layout.Tokens.Single();
            Assert.AreEqual(0, token.Start);
            Assert.AreEqual(2 * LayoutText.PadCount + 3, token.Length);
        }

        [Test]
        public void AtomicTokenBecomesOneObjectCharacter()
        {
            var emote = Track(DelimitedHandler.Create(InlinePlacement.Atomic));
            Build("hi :smile: there", emote);
            Assert.AreEqual("hi " + LayoutText.ObjectChar + " there", _layout.Text);
            Assert.AreEqual("Text,Object,Text", Kinds());
            var token = _layout.Tokens.Single();
            Assert.AreEqual(3, token.Start);
            Assert.AreEqual(1, token.Length);
            Assert.AreEqual(InlinePlacement.Atomic, token.Placement);
            Assert.AreEqual(10, _layout.ToSource(3), "the object maps to the source after the token");
            Assert.AreEqual(4, _layout.ToLayout(5), "inside the token: the next shown character");
        }

        [Test]
        public void FragmentTokensStayTextAndPadOnlyWhenAsked()
        {
            var plain = Track(DelimitedHandler.Create(InlinePlacement.Fragments));
            Build("hi :smile: there", plain);
            Assert.AreEqual("hi :smile: there", _layout.Text);
            Assert.AreEqual("Text,Text,Text", Kinds());
            Assert.AreEqual(0f, _layout.Tokens[0].PaddingEm);
            Assert.AreEqual(7, _layout.Tokens[0].Length);

            _theme.Handlers.Clear();
            var padded = Track(DelimitedHandler.Create(InlinePlacement.Fragments, 0.25f));
            Build("hi :smile: there", padded);
            Assert.AreEqual("hi " + Pads + ":smile:" + Pads + " there", _layout.Text);
            Assert.AreEqual(7 + 2 * LayoutText.PadCount, _layout.Tokens[0].Length);
        }
    }
}
