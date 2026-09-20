using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.UI.Text.Bridge;
using UnityEngine;

namespace TimboJimbo.UI.Text.Tests
{
    /// <summary>
    /// The engine behaviours the package relies on, each learned by measurement rather than documentation. A
    /// Unity update that changes one of them fails here first, before it shows up as a rendering bug.
    /// </summary>
    public class BridgeAssumptionTests
    {
        private const string Pads = "  ";

        private AtgTextHandle _handle;

        [SetUp]
        public void SetUp()
        {
            AtgEngine.Initialize(null);
            _handle = new AtgTextHandle();
        }

        [TearDown]
        public void TearDown() => _handle.Dispose();

        private static AtgTextSettings Settings(string text, float sizePx, float widthPx = -1f)
        {
            var s = AtgTextSettings.Default;
            s.Text = text;
            s.FontSizePx = sizePx;
            s.WidthPx = widthPx;
            s.WordWrap = widthPx > 0f;
            return s;
        }

        private AtgQuad FirstQuad(int group = 0)
        {
            _handle.GetQuad(group, 0, out var quad);
            return quad;
        }

        private static float Width(in AtgQuad quad) => quad.TopRight.Position.x - quad.BottomLeft.Position.x;

        [Test]
        public void VertexPaddingIsDeliveredInPixelsAtAnySize()
        {
            // The engine takes the padding in atlas texels; the bridge converts, so a pixel request is a pixel result.
            foreach (float size in new[] { 90f, 45f })
            {
                var s = Settings("O", size);
                s.VertexPaddingPx = 0f;
                Assert.IsTrue(_handle.Generate(ref s));
                float bare = Width(FirstQuad());
                s.VertexPaddingPx = 6f;
                Assert.IsTrue(_handle.Generate(ref s));
                Assert.AreEqual(12f, Width(FirstQuad()) - bare, 0.6f, $"6 px each side at {size} px");
            }
        }

        [Test]
        public void PlaceholderSpriteReservesTheRequestedBoxOnTheBaseline()
        {
            var s = Settings("a" + AtgSpriteAssets.PlaceholderCodePoint + "b", 40f);
            var span = AtgSpan.Range(1, 1);
            span.Sprite = AtgSpriteAssets.Placeholder;
            span.SpriteWidthPx = 30f;
            span.SpriteHeightPx = 20f;
            s.Spans = new[] { span };
            s.SpanCount = 1;
            Assert.IsTrue(_handle.Generate(ref s));

            int placeholderGroups = 0;
            AtgQuad quad = default;
            for (int g = 0; g < _handle.GroupCount; g++)
            {
                var group = _handle.GetGroup(g);
                if (!AtgSpriteAssets.IsPlaceholder(in group))
                    continue;
                placeholderGroups++;
                Assert.AreEqual(1, group.Count);
                _handle.GetQuad(g, 0, out quad);
            }
            Assert.AreEqual(1, placeholderGroups, "sprites from the placeholder asset form their own group");
            Assert.AreEqual((int)AtgSpriteAssets.PlaceholderCodePoint, quad.GlyphId, "a sprite quad reports its character code");
            var bounds = quad.Bounds;
            Assert.AreEqual(30f, bounds.width, 0.5f);
            Assert.AreEqual(20f, bounds.height, 0.5f);
            float ascent = AtgFontAssets.MetricsPx(AtgFallbackSet.DefaultFont, 40f).Ascent;
            Assert.AreEqual(ascent, bounds.yMax, 1f, "the box sits on the baseline");
        }

        [Test]
        public void NoBreakSpacePadsWrapWithTheirText()
        {
            var font = AtgFallbackSet.DefaultFont;
            float padSize = 12f / (2f * AtgFontAssets.SpaceAdvanceEm(font));
            string text = "some words here " + Pads + "code" + Pads + " more";
            var s = Settings(text, 24f, 215f);
            var a = AtgSpan.Range(16, 2);
            a.FontSizePx = padSize;
            var b = AtgSpan.Range(22, 2);
            b.FontSizePx = padSize;
            s.Spans = new[] { a, b };
            s.SpanCount = 2;
            Assert.IsTrue(_handle.Generate(ref s));

            int lineOfCode = _handle.LineOf(18);
            Assert.Greater(lineOfCode, _handle.LineOf(0), "the padded word did not fit the first line");
            Assert.AreEqual(lineOfCode, _handle.LineOf(16), "leading pads stay with the word");
            Assert.AreEqual(lineOfCode, _handle.LineOf(23), "trailing pads stay with the word");
            var rects = new List<Rect>();
            _handle.GetRangeRects(16, 24, rects);
            Assert.AreEqual(1, rects.Count);
            Assert.AreEqual(12f, _handle.CursorPositionPx(18).x - _handle.CursorPositionPx(16).x, 0.5f, "pad width");
        }

        [Test]
        public void SpanFontSizeScalesANoBreakSpace()
        {
            float spaceEm = AtgFontAssets.SpaceAdvanceEm(AtgFallbackSet.DefaultFont);
            var s = Settings("x " + Pads + "ab", 24f);
            var pad = AtgSpan.Range(2, 2);
            pad.FontSizePx = 12f;
            s.Spans = new[] { pad };
            s.SpanCount = 1;
            Assert.IsTrue(_handle.Generate(ref s));
            float width = _handle.CursorPositionPx(4).x - _handle.CursorPositionPx(2).x;
            Assert.AreEqual(2f * spaceEm * 12f, width, 0.3f);
        }

        [Test]
        public void RegisteredBoldFaceIsUsedAndSyntheticBoldCarriesTheWeight()
        {
            var regular = AtgFontAssets.CreateFromOs("Arial");
            var bold = AtgFontAssets.CreateFromOs("Arial", "Bold");
            Assume.That(regular != null && bold != null, "needs Arial Regular and Bold installed");

            var s = Settings("Bold", 32f);
            s.Weight = AtgWeight.Bold;
            s.Font = regular;
            Assert.IsTrue(_handle.Generate(ref s));
            Assert.AreEqual(regular.boldStyleWeight, FirstQuad().BottomLeft.Dilate, 0.01f, "no real face: the engine's synthetic weight rides on the vertex");

            AtgFontAssets.SetVariant(regular, 700, false, bold);
            s.Text = "Bold face";
            Assert.IsTrue(_handle.Generate(ref s));
            StringAssert.Contains("Bold", _handle.GetGroup(0).Atlas.name);
            Assert.AreEqual(0f, FirstQuad().BottomLeft.Dilate, 0.01f);
        }

        [Test]
        public void OsFallbacksProvideADynamicDefaultFont()
        {
            Assert.Greater(AtgFallbackSet.OsFallbacks.Count, 0);
            var font = AtgFallbackSet.DefaultFont;
            Assert.IsNotNull(font);
            Assert.IsTrue(AtgFontAssets.IsDynamic(font));
            Assert.AreEqual(AtgFontAssets.DefaultAtlasPadding, font.atlasPadding, "the engine's own fallback fonts use the padding the package assumes");
            Assert.AreEqual(AtgFontAssets.DefaultSamplingPointSize, font.faceInfo.pointSize);
        }

        [Test]
        public void RangeRectanglesComeOnePerLine()
        {
            var s = Settings("one two three four five six", 24f, 70f);
            Assert.IsTrue(_handle.Generate(ref s));
            var rects = new List<Rect>();
            _handle.GetRangeRects(0, s.Text.Length, rects);
            int lines = _handle.LineOf(s.Text.Length - 1) + 1;
            Assert.Greater(lines, 1);
            Assert.AreEqual(lines, rects.Count);
        }

        [Test]
        public void LinkHitTestingFindsTheSpanUnderAPoint()
        {
            var s = Settings("see link here", 24f);
            var link = AtgSpan.Range(4, 4);
            link.LinkId = 3;
            s.Spans = new[] { link };
            s.SpanCount = 1;
            Assert.IsTrue(_handle.Generate(ref s));
            var rects = new List<Rect>();
            _handle.GetRangeRects(4, 8, rects);
            Assert.AreEqual(3, _handle.LinkAt(rects[0].center));
            Assert.AreEqual(-1, _handle.LinkAt(new Vector2(1f, rects[0].center.y)));
        }
    }
}
