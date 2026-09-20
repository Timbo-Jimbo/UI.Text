using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UI.Text.Bridge
{
    /// <summary>Font asset creation and the internal calls the generator needs around a font asset.</summary>
    public static class AtgFontAssets
    {
        /// <summary>Default sampling size for dynamic atlases; glyphs are SDFs so this only bounds quality.</summary>
        public const int DefaultSamplingPointSize = 90;

        /// <summary>
        /// Atlas padding at the sampling size: how far outside a glyph the distance field carries data. Unity's
        /// own value, which is also what the engine's OS fallback fonts use; a tenth of the font size is room
        /// enough for anti-aliasing and synthetic bold.
        /// </summary>
        public const int DefaultAtlasPadding = 9;

        private const int AtlasSize = 1024;

        // Like Unity's own transient font assets: hidden and never saved, but reclaimable by UnloadUnusedAssets
        // once nothing references them any more (after a domain reload, for instance).
        private const HideFlags TransientFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;

        /// <summary>
        /// A dynamic font asset for an installed OS font, or null when the OS has no such family and style.
        /// Style names follow the font's own naming: "Regular", "Bold", "Italic", "Bold Italic".
        /// </summary>
        public static FontAsset CreateFromOs(string family, string style = "Regular", int samplingPointSize = DefaultSamplingPointSize)
        {
            // This overload returns null quietly when the style is missing; the shorter one logs.
            var asset = FontAsset.CreateFontAsset(family, style, samplingPointSize, DefaultAtlasPadding, GlyphRenderMode.DEFAULT);
            if (asset != null)
                MarkTransient(asset);
            return asset;
        }

        /// <summary>The font's line height at a size, in pixels: the distance between baselines.</summary>
        public static float LineHeightPx(FontAsset asset, float fontSizePx)
        {
            var face = asset.faceInfo;
            return face.pointSize > 0 ? face.lineHeight * fontSizePx / face.pointSize : fontSizePx * 1.2f;
        }

        /// <summary>
        /// How far outside a glyph the distance field still has data at a size, in pixels: the atlas padding
        /// scaled from the sampling size. Effects and vertex padding beyond it cannot be represented.
        /// </summary>
        public static float MaxPaddingPx(FontAsset asset, float fontSizePx)
        {
            var face = asset.faceInfo;
            return face.pointSize > 0 ? asset.atlasPadding * fontSizePx / face.pointSize : 0f;
        }

        /// <summary>Vertical metrics of a font at a size, in pixels relative to the baseline (descent and the offsets are negative below it).</summary>
        public static AtgFontMetrics MetricsPx(FontAsset asset, float fontSizePx)
        {
            var face = asset.faceInfo;
            float scale = face.pointSize > 0 ? fontSizePx / face.pointSize : fontSizePx / 90f;
            return new AtgFontMetrics
            {
                LineHeight = face.lineHeight * scale,
                Ascent = face.ascentLine * scale,
                Descent = face.descentLine * scale,
                UnderlineOffset = face.underlineOffset * scale,
                UnderlineThickness = Mathf.Max(1f, face.underlineThickness * scale),
                StrikethroughOffset = face.strikethroughOffset * scale,
                StrikethroughThickness = Mathf.Max(1f, face.strikethroughThickness * scale),
            };
        }

        /// <summary>
        /// A dynamic font asset for a font file imported into the project (TTF/OTF as a <see cref="Font"/>).
        /// Colour fonts get a colour atlas, everything else an SDF atlas.
        /// </summary>
        public static FontAsset CreateFromFont(Font font, int samplingPointSize = DefaultSamplingPointSize, int atlasPadding = DefaultAtlasPadding, int atlasSize = AtlasSize)
        {
            if (font == null)
                return null;
            var asset = FontAsset.CreateFontAsset(font, samplingPointSize, atlasPadding, GlyphRenderMode.DEFAULT, atlasSize, atlasSize, AtlasPopulationMode.Dynamic, true);
            if (asset != null)
                MarkTransient(asset);
            return asset;
        }

        /// <summary>Family names of the fonts installed on this machine.</summary>
        public static string[] OsFamilies() => FontEngine.GetSystemFontNames();

        private static readonly Dictionary<FontAsset, float> s_SpaceAdvanceEm = new();

        /// <summary>
        /// Advance of the no-break space (or the space) in a font, as a fraction of the font size. Text that
        /// needs an exact gap sizes a run of no-break spaces with it. A quarter em when the font has neither.
        /// </summary>
        public static float SpaceAdvanceEm(FontAsset asset)
        {
            const float fallback = 0.25f;
            if (asset == null)
                return fallback;
            if (s_SpaceAdvanceEm.TryGetValue(asset, out float em))
                return em;
            em = fallback;
            if (asset.faceInfo.pointSize > 0 && IsDynamic(asset))
            {
                asset.TryAddCharacters("  ", out _);
                var table = asset.characterLookupTable;
                if (table != null && (table.TryGetValue(0x00A0, out var character) || table.TryGetValue(0x0020, out character)) && character?.glyph != null)
                {
                    float advance = character.glyph.metrics.horizontalAdvance / asset.faceInfo.pointSize;
                    if (advance > 0.01f)
                        em = advance;
                }
            }
            s_SpaceAdvanceEm[asset] = em;
            return em;
        }

        /// <summary>The generator only renders dynamic atlases; static (pre-baked) font assets are rejected.</summary>
        public static bool IsDynamic(FontAsset asset)
        {
#pragma warning disable CS0618
            return asset != null && asset.atlasPopulationMode != AtlasPopulationMode.Static;
#pragma warning restore CS0618
        }

        /// <summary>True for colour (bitmap) fonts such as emoji; their glyphs carry their own colour.</summary>
        public static bool IsColor(FontAsset asset) => asset != null && asset.IsColor();

        /// <summary>
        /// Registers a real face for a weight and slant, so bold or italic text uses it instead of a synthetic
        /// version. <paramref name="weight"/> is 100..900; the asset's own weight is normally Regular (400).
        /// </summary>
        public static void SetVariant(FontAsset asset, int weight, bool italic, FontAsset variant)
        {
            int index = Mathf.Clamp(weight / 100, 1, 9);
            var table = asset.fontWeightTable;
            if (italic)
                table[index].italicTypeface = variant;
            else
                table[index].regularTypeface = variant;
            asset.fontWeightTable = table;
            if (asset.nativeFontAsset != System.IntPtr.Zero)
                asset.UpdateWeightFallbacks();
        }

        /// <summary>Registers the real Bold, Italic and Bold Italic faces of an OS font's family, those the OS has.</summary>
        public static void AddOsVariants(FontAsset asset)
        {
            if (asset == null)
                return;
            string family = asset.faceInfo.familyName;
            if (string.IsNullOrEmpty(family))
                return;
            AddOsVariant(asset, family, 700, false, "Bold");
            AddOsVariant(asset, family, 400, true, "Italic");
            AddOsVariant(asset, family, 700, true, "Bold Italic");
        }

        private static void AddOsVariant(FontAsset asset, string family, int weight, bool italic, string style)
        {
            var variant = CreateFromOs(family, style);
            if (variant != null)
                SetVariant(asset, weight, italic, variant);
        }

        /// <summary>
        /// Weight, 0 to 1, of the bold the engine synthesises for text in this asset when no real bold face is
        /// registered for the requested weight. Unity's default is 0.75.
        /// </summary>
        public static void SetSyntheticBold(FontAsset asset, float weight)
        {
            if (asset == null)
                return;
            weight = Mathf.Clamp01(weight);
            if (Mathf.Approximately(asset.boldStyleWeight, weight))
                return;
            asset.boldStyleWeight = weight;
            if (asset.nativeFontAsset != System.IntPtr.Zero)
                asset.UpdateBoldWeight();
        }

        private static void MarkTransient(FontAsset asset)
        {
            asset.hideFlags = TransientFlags;
            asset.isMultiAtlasTexturesEnabled = true;
            var textures = asset.atlasTextures;
            if (textures == null)
                return;
            foreach (var texture in textures)
                if (texture != null)
                    texture.hideFlags = TransientFlags;
        }

        internal static void Prepare(FontAsset asset) => asset.EnsureNativeFontAssetIsCreated();

        /// <summary>After glyphs were added to any font asset, pushes the atlas and feature updates through.</summary>
        internal static void FlushUpdates()
        {
            FontAsset.CreateHbFaceIfNeeded();
            FontAsset.UpdateFontAssetsInUpdateQueue();
        }
    }
}
