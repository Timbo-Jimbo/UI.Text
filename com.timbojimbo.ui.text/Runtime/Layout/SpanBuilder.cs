using System;
using System.Collections.Generic;
using TimboJimbo.UI.Text.Bridge;
using TimboJimbo.UI.Text.Markup;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UI.Text
{
    /// <summary>A range of the layout text that needs an underline or strikethrough drawn after layout.</summary>
    internal struct DecorationRun
    {
        public int Start, End;
        public bool Underline, Strikethrough;
        public Color32 Color;
        public FontAsset Font;
        public float FontSizePx;
    }

    /// <summary>A growable span array the generator settings can point at directly.</summary>
    internal sealed class SpanBuffer
    {
        public AtgSpan[] Items = new AtgSpan[64];
        public int Count;

        public void Clear() => Count = 0;

        public void Add(in AtgSpan span)
        {
            if (Count == Items.Length)
                Array.Resize(ref Items, Items.Length * 2);
            Items[Count++] = span;
        }
    }

    /// <summary>Turns layout runs into generator spans, applying the theme, block styles and inline handlers.</summary>
    internal static class SpanBuilder
    {
        /// <summary>
        /// Fills <paramref name="buffer"/> with spans for the layout's runs. Records decoration runs and atomic
        /// requests (indexed like the document's tokens) when those lists are given.
        /// </summary>
        public static void Build(TextDocument document, LayoutText layout, TextTheme theme, FontAsset baseFont, float baseSizePx, bool baseBold,
            List<DecorationRun> decorations, in InlineContext context, List<InlineObjectRequest> atomicRequests, SpanBuffer buffer)
        {
            buffer.Clear();
            if (atomicRequests != null)
                for (int i = 0; i < document.Tokens.Count; i++)
                    atomicRequests.Add(default);

            var runs = layout.Runs;
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                var span = AtgSpan.Range(run.Start, run.Length);
                var style = new SpanStyle();
                bool any = false;

                // Block-level styling.
                float sizeScale = theme.HeadingScale(run.Block);
                bool headingBold = run.Block is BlockKind.Heading1 or BlockKind.Heading2 or BlockKind.Heading3;
                if (run.Block == BlockKind.Quote)
                {
                    style.HasColor = true;
                    style.Color = theme.QuoteColor;
                }

                // Inline styling.
                if ((run.Style & RunStyle.Code) != 0 || run.Block == BlockKind.CodeBlock)
                {
                    style.Font = theme.CodeFont;
                    style.SizeScale = theme.CodeScale;
                    style.HasColor = true;
                    style.Color = theme.CodeColor;
                }
                if ((run.Style & RunStyle.Link) != 0)
                {
                    style.HasColor = true;
                    style.Color = theme.LinkColor;
                    style.Underline = theme.UnderlineLinks;
                    span.LinkId = run.LinkIndex;
                    any = true;
                }
                if ((run.Style & RunStyle.Bold) != 0) style.Bold = true;
                if ((run.Style & RunStyle.Italic) != 0) style.Italic = true;
                if ((run.Style & RunStyle.Underline) != 0) style.Underline = true;
                if ((run.Style & RunStyle.Strikethrough) != 0) style.Strikethrough = true;

                // Handler-owned tokens.
                LayoutToken layoutToken = default;
                if (run.TokenIndex >= 0)
                {
                    layoutToken = layout.Tokens[run.TokenIndex];
                    var token = document.Tokens[run.TokenIndex];
                    var handler = layoutToken.Handler;
                    if (run.Kind == LayoutRunKind.Object)
                    {
                        // The engine sits a sprite on the baseline, so only the part of the box above the
                        // baseline is reserved; the prefab is placed over the whole box, hanging below it.
                        var request = handler != null ? handler.Request(in token, in context) : default;
                        if (atomicRequests != null)
                            atomicRequests[run.TokenIndex] = request;
                        var size = request.SizeEm.x > 0f && request.SizeEm.y > 0f ? request.SizeEm : Vector2.one;
                        span.Sprite = AtgSpriteAssets.Placeholder;
                        span.SpriteWidthPx = size.x * baseSizePx;
                        span.SpriteHeightPx = AtomicAscentPx(in request, size.y * baseSizePx, baseFont, baseSizePx);
                        buffer.Add(span);
                        continue;
                    }
                    if (handler != null)
                        handler.Style(in token, ref style);
                }

                // Apply the accumulated style.
                if (style.Font != null)
                {
                    var font = style.Font.Primary;
                    if (font != null && AtgFontAssets.IsDynamic(font))
                    {
                        span.Font = font;
                        any = true;
                    }
                }
                float sizePx = baseSizePx * sizeScale * (style.SizeScale > 0f ? style.SizeScale : 1f);
                if (!Mathf.Approximately(sizePx, baseSizePx))
                {
                    span.FontSizePx = sizePx;
                    any = true;
                }
                if (style.HasColor)
                {
                    span.HasColor = true;
                    span.Color = style.Color;
                    any = true;
                }
                if ((headingBold || style.Bold) && !baseBold)
                {
                    span.Weight = AtgWeight.Bold;
                    any = true;
                }
                if (style.Italic)
                {
                    span.Style |= AtgStyle.Italic;
                    any = true;
                }

                if (run.Kind == LayoutRunKind.Pad)
                {
                    // Layout padding: the no-break spaces get the font size that makes their run exactly the
                    // padding wide (at most the token's own size), so the text either side moves over and the
                    // token's rectangles include the padding.
                    var padFont = span.Font != null ? span.Font : baseFont;
                    float padPx = layoutToken.PaddingEm * sizePx;
                    span.FontSizePx = Mathf.Min(sizePx, padPx / (LayoutText.PadCount * AtgFontAssets.SpaceAdvanceEm(padFont)));
                    buffer.Add(span);
                    continue;
                }

                if (decorations != null && run.Kind == LayoutRunKind.Text && (style.Underline || style.Strikethrough))
                {
                    decorations.Add(new DecorationRun
                    {
                        Start = run.Start,
                        End = run.End,
                        Underline = style.Underline,
                        Strikethrough = style.Strikethrough,
                        Color = style.HasColor ? style.Color : new Color32(255, 255, 255, 255),
                        Font = span.Font != null ? span.Font : baseFont,
                        FontSizePx = sizePx,
                    });
                }

                if (any)
                    buffer.Add(span);
            }
        }

        /// <summary>
        /// How much of an atomic box sits above the baseline, in pixels: what the request asks for or, by default,
        /// enough to centre the box on the middle of the text, halfway between the font's ascent and descent.
        /// </summary>
        public static float AtomicAscentPx(in InlineObjectRequest request, float heightPx, FontAsset baseFont, float baseSizePx)
        {
            if (request.AscentEm > 0f)
                return Mathf.Clamp(request.AscentEm * baseSizePx, 0f, heightPx);
            if (baseFont == null)
                return heightPx;
            var metrics = AtgFontAssets.MetricsPx(baseFont, baseSizePx);
            float middle = (metrics.Ascent + metrics.Descent) * 0.5f;
            return Mathf.Clamp(heightPx * 0.5f + middle, 0f, heightPx);
        }
    }
}
