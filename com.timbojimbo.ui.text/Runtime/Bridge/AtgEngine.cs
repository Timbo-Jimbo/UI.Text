// This assembly is named UnityEngine.TextCore.Tools on purpose: the engine's TextCore module lists that name in its
// InternalsVisibleTo attributes, which is what lets this code call the internal Advanced Text Generator. It is the
// only assembly in the package that touches Unity internals, and it exposes them through the public types in this
// folder so that everything else compiles against a stable, version-independent surface.
using System;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UI.Text.Bridge
{
    /// <summary>Process-wide generator state: the native text library, loaded once with ICU data.</summary>
    public static class AtgEngine
    {
        private const string IcuDataAssetName = "icudt73l.bytes";

        private static TextLib s_Lib;

        /// <summary>
        /// Creates the generator if needed. <paramref name="icuData"/> is the ICU asset a component carries into
        /// builds; without it the editor's copy is used, and without either the native side falls back to basic
        /// line breaking (emoji sequences may not render correctly). Only the first call's data is used.
        /// </summary>
        public static void Initialize(UnityEngine.TextAsset icuData)
        {
            if (s_Lib != null)
                return;

            var icu = icuData != null ? icuData : TextHandle.GetICUAssetStaticFalback();
            if (icu == null)
                Debug.LogWarning("[UI.Text] ICU data asset not found: falling back to basic line breaking.");
            s_Lib = new TextLib(icu != null ? icu.bytes : Array.Empty<byte>());
        }

        internal static TextLib Lib
        {
            get
            {
                if (s_Lib == null)
                    Initialize(null);
                return s_Lib;
            }
        }

#if UNITY_EDITOR
        /// <summary>The engine's bundled ICU data, shipped as a built-in extra resource.</summary>
        public static UnityEngine.TextAsset FindEditorIcuData()
        {
            return UnityEditor.AssetDatabase.GetBuiltinExtraResource<UnityEngine.TextAsset>(IcuDataAssetName);
        }
#endif
    }
}
