using System;
using System.Collections.Generic;
using TimboJimbo.UI.Text.Bridge;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UI.Text
{
    /// <summary>
    /// An ordered list of fonts: the first that resolves is the primary font and the rest are tried, in order,
    /// for any glyph it lacks. With OS Fallbacks on, the fonts the OS names for missing glyphs come after the
    /// stack, so a stack only needs to list what should win over them. A stack with no sources draws with the
    /// OS default font, which is what texts without a stack use (<see cref="Default"/>). The Emoji fonts are
    /// consulted first for characters Unicode defines as emoji, before the stack and the OS: for colour emoji that
    /// look the same everywhere, and on devices whose own emoji font the engine cannot draw (Android 13 and later
    /// ship only a COLRv1 emoji font, which FreeType does not rasterise; a bundled bitmap font such as the CBDT
    /// build of Noto Color Emoji works everywhere).
    /// </summary>
    [CreateAssetMenu(menuName = "Timbo Jimbo/UI/Font Stack", fileName = "FontStack")]
    public sealed class FontStack : ScriptableObject
    {
        [SerializeField] private List<FontSource> _sources = new();
        [Tooltip("Fonts consulted first for emoji, before the sources and the OS fonts. A colour font imported into the project (such as the CBDT build of Noto Color Emoji) draws emoji on every platform, where the OS's own emoji font may not be one the engine can draw.")]
        [SerializeField] private List<FontSource> _emoji = new();
        [Tooltip("Consult the fonts the OS names for missing glyphs (emoji, other scripts) after this stack's own sources. Off makes the stack self-contained and never touches the OS fonts.")]
        [SerializeField] private bool _osFallbacks = true;

        [NonSerialized] private bool _resolved;
        [NonSerialized] private FontAsset _primary;
        [NonSerialized] private AtgFallbackSet _fallbacks;

        private static FontStack s_Default;

        public List<FontSource> Sources => _sources;

        /// <summary>The fonts consulted first for emoji; call <see cref="Invalidate"/> after changing the list.</summary>
        public List<FontSource> Emoji => _emoji;

        /// <summary>Whether the OS fallback fonts are consulted after this stack's sources.</summary>
        public bool OsFallbacks
        {
            get => _osFallbacks;
            set
            {
                if (_osFallbacks == value) return;
                _osFallbacks = value;
                Invalidate();
            }
        }

        /// <summary>The stack texts without one use: no sources and OS fallbacks on, so the OS default font.</summary>
        public static FontStack Default
        {
            get
            {
                if (s_Default == null)
                {
                    s_Default = CreateInstance<FontStack>();
                    s_Default.name = "OS default";
                    s_Default.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_Default;
            }
        }

        /// <summary>The first source that resolves; with OS fallbacks on and no source resolving, the OS default font.</summary>
        public FontAsset Primary
        {
            get
            {
                Resolve();
                return _primary;
            }
        }

        /// <summary>The remaining sources as the generator's fallback chain, followed by the OS fonts when enabled.</summary>
        public AtgFallbackSet Fallbacks
        {
            get
            {
                Resolve();
                return _fallbacks;
            }
        }

        /// <summary>Drop every resolved font so the next use re-resolves the sources.</summary>
        public void Invalidate()
        {
            foreach (var source in _sources)
                source?.Invalidate();
            foreach (var source in _emoji)
                source?.Invalidate();
            _primary = null;
            _fallbacks?.Dispose();
            _fallbacks = null;
            _resolved = false;
        }

        private void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;

            var rest = new List<FontAsset>();
            foreach (var source in _sources)
            {
                var asset = source?.Resolve();
                if (asset == null)
                    continue;
                if (_primary == null)
                    _primary = asset;
                else
                    rest.Add(asset);
            }
            if (_primary == null && _osFallbacks)
                _primary = AtgFallbackSet.DefaultFont;
            var emoji = new List<UnityEngine.TextCore.Text.TextAsset>();
            foreach (var source in _emoji)
            {
                var asset = source?.Resolve();
                if (asset != null)
                    emoji.Add(asset);
            }
            _fallbacks = new AtgFallbackSet(name) { IncludeOsFallbacks = _osFallbacks };
            _fallbacks.SetFonts(rest);
            _fallbacks.SetEmojiAssets(emoji);
        }

        private void OnDisable() => Invalidate();

#if UNITY_EDITOR
        private void OnValidate() => Invalidate();
#endif
    }
}
