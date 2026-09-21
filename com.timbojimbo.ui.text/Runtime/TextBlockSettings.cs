using UnityEngine;

namespace TimboJimbo.UI.Text
{
    /// <summary>
    /// Project-wide text settings, edited under Edit > Project Settings > Timbo Jimbo > Text. The asset lives
    /// anywhere under Assets; the settings page registers it as an EditorBuildSettings config object (stored in
    /// ProjectSettings/EditorBuildSettings.asset) and builds preload it, so a player finds it without a Resources
    /// folder or a fixed name. Texts that leave a field unset fall back to these values.
    /// </summary>
    public sealed class TextBlockSettings : ScriptableObject
    {
        /// <summary>The EditorBuildSettings config object key the active asset is registered under.</summary>
        public const string ConfigObjectKey = "com.timbojimbo.ui.text.settings";

        [Tooltip("The Font Stack used by texts that don't set their own. Empty means the OS default font.")]
        [SerializeField] private FontStack _defaultFont;
        [Tooltip("The Text Theme used by texts that don't set their own. Empty means the built-in defaults.")]
        [SerializeField] private Markup.TextTheme _defaultTheme;

        private static TextBlockSettings s_Instance;

        /// <summary>The active settings asset, or null when the project has none.</summary>
        public static TextBlockSettings Instance
        {
            get
            {
#if UNITY_EDITOR
                if (s_Instance == null)
                    UnityEditor.EditorBuildSettings.TryGetConfigObject(ConfigObjectKey, out s_Instance);
#endif
                return s_Instance;
            }
        }

        public FontStack DefaultFont => _defaultFont;
        public Markup.TextTheme DefaultTheme => _defaultTheme;

#if UNITY_EDITOR
        /// <summary>Called by the settings page after it changes the registered asset; every text redraws with the new defaults.</summary>
        internal static void SetActive(TextBlockSettings settings)
        {
            s_Instance = settings;
            TextBlock.RebuildAll();
        }

        // Edits apply live to every open text.
        private void OnValidate()
        {
            if (this == s_Instance)
                TextBlock.RebuildAll();
        }
#else
        // A player has the asset as a preloaded asset, loaded before the first scene; nothing else references it,
        // so it makes itself the instance here.
        private void OnEnable() => s_Instance = this;
#endif
    }
}
