using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TimboJimboEditor.UI.Text
{
    /// <summary>
    /// Puts the active settings asset into the player's preloaded assets for the duration of a build, so it and the
    /// fonts and themes it references ship and load before the first scene. The list is restored afterwards; an
    /// aborted build leaves the entry behind, which is harmless and tidied by the next build.
    /// </summary>
    internal sealed class TextBlockSettingsBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private static bool s_Added;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = TextBlockSettingsProvider.Active;
            if (settings == null)
                return;
            var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
            if (preloaded.Contains(settings))
                return;
            preloaded.Add(settings);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            s_Added = true;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!s_Added)
                return;
            s_Added = false;
            var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
            preloaded.Remove(TextBlockSettingsProvider.Active);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }
    }
}
