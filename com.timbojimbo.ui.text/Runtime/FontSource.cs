using System;
using TimboJimbo.UI.Text.Bridge;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UI.Text
{
    public enum FontSourceKind
    {
        /// <summary>A font installed on the device, by family name. Free, but only present where the OS ships it.</summary>
        OsFamily,
        /// <summary>A TTF or OTF imported into the project. Ships with the game.</summary>
        FontFile,
        /// <summary>A pre-made dynamic font asset (Create > Text Core > Font Asset).</summary>
        FontAsset,
    }

    /// <summary>
    /// One entry of a <see cref="FontStack"/>. Bold and italic faces are optional: an OS family is probed for
    /// "Bold", "Italic" and "Bold Italic" styles automatically, a font file can name its variant files, and a font
    /// asset brings its own weight table. Without a real face the engine synthesises bold and italic.
    /// </summary>
    [Serializable]
    public sealed class FontSource
    {
        public FontSourceKind Kind = FontSourceKind.OsFamily;
        public string Family = "";
        public Font File;
        public Font BoldFile;
        public Font ItalicFile;
        public Font BoldItalicFile;
        public FontAsset Asset;
        [Tooltip("How heavy the bold the engine synthesises is when this source has no real bold face, 0 to 1. Unity's default is 0.75.")]
        [Range(0f, 1f)] public float SyntheticBold = 0.75f;

        [NonSerialized] private FontAsset _resolved;
        [NonSerialized] private bool _tried;

        /// <summary>The runtime font asset for this source, created on first use; null when it cannot be resolved.</summary>
        public FontAsset Resolve()
        {
            if (_resolved != null || _tried)
                return _resolved;
            _tried = true;

            switch (Kind)
            {
                case FontSourceKind.FontAsset:
                    _resolved = Asset;
                    break;

                case FontSourceKind.FontFile:
                    _resolved = AtgFontAssets.CreateFromFont(File);
                    if (_resolved != null)
                    {
                        AddVariant(700, false, BoldFile);
                        AddVariant(400, true, ItalicFile);
                        AddVariant(700, true, BoldItalicFile);
                    }
                    break;

                case FontSourceKind.OsFamily:
                    if (!string.IsNullOrWhiteSpace(Family))
                    {
                        _resolved = AtgFontAssets.CreateFromOs(Family, "Regular");
                        if (_resolved != null)
                            AtgFontAssets.AddOsVariants(_resolved);
                    }
                    break;
            }
            if (_resolved != null && Kind != FontSourceKind.FontAsset)
                AtgFontAssets.SetSyntheticBold(_resolved, SyntheticBold);
            return _resolved;
        }

        /// <summary>Forget the resolved asset so the next <see cref="Resolve"/> starts over (after an edit).</summary>
        public void Invalidate()
        {
            _resolved = null;
            _tried = false;
        }

        private void AddVariant(int weight, bool italic, Font file)
        {
            if (file == null) return;
            var variant = AtgFontAssets.CreateFromFont(file);
            if (variant != null)
                AtgFontAssets.SetVariant(_resolved, weight, italic, variant);
        }
    }
}
