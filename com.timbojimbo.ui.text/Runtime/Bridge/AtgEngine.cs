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
        private static bool s_shuttingDown;

        /// <summary>
        /// True once the domain is being torn down (an assembly reload in the editor, or the application quitting).
        /// The native engine and the font assets it reads go away in that phase while the editor still flushes
        /// canvases (closing the Scene View's preview scene flushes an undo record, which forces a canvas update
        /// that rebuilds dirty texts), so no text may be generated or measured after this turns true.
        /// </summary>
        public static bool IsShuttingDown => s_shuttingDown;

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

            // In the editor Application.quitting means play mode is ending, not that the domain is going away, and
            // with domain reload disabled the flag would then outlive the play session; the reload event is the
            // teardown signal there.
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () => s_shuttingDown = true;
#else
            Application.quitting += () => s_shuttingDown = true;
#endif
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
