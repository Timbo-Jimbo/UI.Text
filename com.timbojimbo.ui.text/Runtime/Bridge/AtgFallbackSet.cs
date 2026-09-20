using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UI.Text.Bridge
{
    /// <summary>
    /// The fallback chain the generator consults when the primary font lacks a glyph: explicit fonts first, then
    /// emoji assets, then the OS fallback fonts unless a set opts out. All sets share one engine TextSettings
    /// object; a set's lists are pushed into it when a text using the set is generated, so the OS fonts exist once.
    /// </summary>
    public sealed class AtgFallbackSet : IDisposable
    {
        private static TextSettings s_Settings;
        // The engine's own OS fallback list object; we own its contents so a set can leave the OS fonts out.
        private static List<FontAsset> s_OsList;
        private static List<FontAsset> s_OsFallbacks;
        private static AtgFallbackSet s_Applied;
        private static int s_AppliedVersion;
        private static AtgFallbackSet s_Shared;

        private List<FontAsset> _fonts = new();
        private List<UnityEngine.TextCore.Text.TextAsset> _emoji = new();
        private bool _includeOsFallbacks = true;
        private int _version;
        private bool _disposed;

        public string Name { get; }

        /// <summary>A set with no explicit fallbacks and the OS fonts on: what bridge callers without a stack get.</summary>
        public static AtgFallbackSet Shared => s_Shared ??= new AtgFallbackSet("UI.Text Shared");

        public AtgFallbackSet(string name)
        {
            Name = name;
        }

        /// <summary>Fonts tried, in order, before the OS fallbacks. Static font assets are ignored by the engine.</summary>
        public void SetFonts(List<FontAsset> fonts)
        {
            ThrowIfDisposed();
            _fonts = fonts ?? new List<FontAsset>();
            _version++;
        }

        /// <summary>Font or sprite assets consulted for characters Unicode defines as emoji.</summary>
        public void SetEmojiAssets(List<UnityEngine.TextCore.Text.TextAsset> assets)
        {
            ThrowIfDisposed();
            _emoji = assets ?? new List<UnityEngine.TextCore.Text.TextAsset>();
            _version++;
        }

        /// <summary>Whether the OS fallback fonts are consulted after the explicit ones. On by default.</summary>
        public bool IncludeOsFallbacks
        {
            get => _includeOsFallbacks;
            set
            {
                if (_includeOsFallbacks == value) return;
                _includeOsFallbacks = value;
                _version++;
            }
        }

        /// <summary>The OS default UI font, the first OS fallback font: what a text draws with when nothing else resolves.</summary>
        public static FontAsset DefaultFont
        {
            get
            {
                EnsureSettings();
                return s_OsFallbacks.Count > 0 ? s_OsFallbacks[0] : s_Settings.GetFontAsset();
            }
        }

        /// <summary>The OS fallback fonts in the order the engine consults them.</summary>
        public static IReadOnlyList<FontAsset> OsFallbacks
        {
            get
            {
                EnsureSettings();
                return s_OsFallbacks;
            }
        }

        internal IntPtr Native
        {
            get
            {
                ThrowIfDisposed();
                EnsureSettings();
                if (!ReferenceEquals(s_Applied, this) || s_AppliedVersion != _version)
                {
                    s_Settings.fallbackFontAssets = _fonts;
                    s_Settings.emojiFallbackTextAssets = _emoji;
                    s_OsList.Clear();
                    if (_includeOsFallbacks)
                        s_OsList.AddRange(s_OsFallbacks);
                    s_Settings.SetNativeTextSettingsDirty();
                    s_Applied = this;
                    s_AppliedVersion = _version;
                }
                s_Settings.UpdateNativeTextSettings();
                return s_Settings.nativeTextSettings;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (ReferenceEquals(s_Applied, this))
                s_Applied = null;
            if (ReferenceEquals(s_Shared, this))
                s_Shared = null;
        }

        private static void EnsureSettings()
        {
            if (s_Settings != null)
                return;
            s_Settings = ScriptableObject.CreateInstance<TextSettings>();
            s_Settings.name = "UI.Text TextSettings";
            s_Settings.hideFlags = HideFlags.HideAndDontSave;

            // The engine builds its OS fallback list the first time it is read: one dynamic font asset per family
            // the OS names, glyphs rasterised on demand. We keep those assets, take a copy of the list, and add the
            // real bold and italic faces to its first entry, the OS default UI font, so default-font headings are
            // not synthesised.
            s_OsList = s_Settings.fallbackOSFontAssets;
            s_OsFallbacks = new List<FontAsset>(s_OsList);
            if (s_OsFallbacks.Count > 0)
                AtgFontAssets.AddOsVariants(s_OsFallbacks[0]);
            s_Settings.SetNativeTextSettingsDirty();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AtgFallbackSet));
        }
    }
}
