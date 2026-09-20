using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UI.Text.Bridge
{
    /// <summary>Sprite assets the generator can lay out inline: emote sheets, and the blank placeholder used to reserve room for inline objects.</summary>
    public static class AtgSpriteAssets
    {
        /// <summary>Code point of the single sprite in the placeholder asset. Put it in the text where an inline object goes.</summary>
        public const char PlaceholderCodePoint = '';

        private static SpriteAsset s_Placeholder;
        private static Texture2D s_Solid;

        /// <summary>An opaque white 4x4 texture, for drawing solid quads (underlines, rules) with the bitmap text material.</summary>
        public static Texture2D SolidTexture
        {
            get
            {
                if (s_Solid != null)
                    return s_Solid;
                s_Solid = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "UI.Text solid", hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(255, 255, 255, 255);
                s_Solid.SetPixels32(pixels);
                s_Solid.Apply(false, true);
                return s_Solid;
            }
        }

        /// <summary>True when a quad group draws from the placeholder asset and should be skipped when rendering.</summary>
        public static bool IsPlaceholder(in AtgQuadGroup group) => group.IsSprite && s_Placeholder != null && group.Atlas == s_Placeholder.spriteSheet;

        /// <summary>
        /// A one-sprite asset whose sprite is a blank 4x4 texture. A span over the placeholder code point with
        /// this asset and explicit metrics reserves exactly that box in the layout; the quad it emits tells you
        /// where the box landed and can be skipped when drawing. The engine treats a sprite as a word of its own
        /// for line breaking, so this is for objects, not for spacing.
        /// </summary>
        public static SpriteAsset Placeholder
        {
            get
            {
                if (s_Placeholder != null)
                    return s_Placeholder;

                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "UI.Text placeholder", hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(0, 0, 0, 0);
                texture.SetPixels32(pixels);
                texture.Apply(false, true);

                var glyph = new SpriteGlyph(0, new GlyphMetrics(4, 4, 0, 4, 4), new GlyphRect(0, 0, 4, 4), 1f, 0);
                var asset = ScriptableObject.CreateInstance<SpriteAsset>();
                asset.name = "UI.Text placeholder";
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.spriteSheet = texture;
                asset.spriteGlyphTable = new List<SpriteGlyph> { glyph };
                asset.spriteCharacterTable = new List<SpriteCharacter> { new SpriteCharacter(PlaceholderCodePoint, asset, glyph) { name = "placeholder" } };
                asset.UpdateLookupTables();
                s_Placeholder = asset;
                return asset;
            }
        }
    }
}
