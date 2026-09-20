using UnityEngine;

namespace TimboJimbo.UI.Text
{
    /// <summary>
    /// Project-wide defaults. Create one with Create > Timbo Jimbo > UI > Text Block Settings and place it in a
    /// Resources folder as "TextBlockSettings"; texts that leave a field unset fall back to it.
    /// </summary>
    [CreateAssetMenu(menuName = "Timbo Jimbo/UI/Text Block Settings", fileName = ResourceName)]
    public sealed class TextBlockSettings : ScriptableObject
    {
        public const string ResourceName = "TextBlockSettings";

        [Tooltip("The Font Stack used by texts that don't set their own. Empty means the OS default font.")]
        [SerializeField] private FontStack _defaultFont;
        [Tooltip("The Text Theme used by texts that don't set their own. Empty means the built-in defaults.")]
        [SerializeField] private Markup.TextTheme _defaultTheme;

        private static TextBlockSettings s_Instance;
        private static bool s_Searched;

        /// <summary>The settings asset from Resources, or null when the project has none.</summary>
        public static TextBlockSettings Instance
        {
            get
            {
                if (s_Instance == null && !s_Searched)
                {
                    s_Searched = true;
                    s_Instance = Resources.Load<TextBlockSettings>(ResourceName);
                }
                return s_Instance;
            }
        }

        public FontStack DefaultFont => _defaultFont;
        public Markup.TextTheme DefaultTheme => _defaultTheme;

        private void OnEnable()
        {
            if (s_Instance == null)
                s_Instance = this;
        }
    }
}
