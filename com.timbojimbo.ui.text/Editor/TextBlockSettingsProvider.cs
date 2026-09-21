using System.Collections.Generic;
using TimboJimbo.UI.Text;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Text
{
    /// <summary>
    /// Edit > Project Settings > Timbo Jimbo > Text: picks the active <see cref="TextBlockSettings"/> asset and
    /// edits its fields in place. The choice is an EditorBuildSettings config object, so it lives in
    /// ProjectSettings and <see cref="TextBlockSettingsBuild"/> can preload the asset into builds.
    /// </summary>
    internal static class TextBlockSettingsProvider
    {
        /// <summary>The registered settings asset, or null. Setting it registers the asset and applies it to open texts.</summary>
        internal static TextBlockSettings Active
        {
            get
            {
                EditorBuildSettings.TryGetConfigObject(TextBlockSettings.ConfigObjectKey, out TextBlockSettings settings);
                return settings;
            }
            set
            {
                if (value != null)
                    EditorBuildSettings.AddConfigObject(TextBlockSettings.ConfigObjectKey, value, true);
                else
                    EditorBuildSettings.RemoveConfigObject(TextBlockSettings.ConfigObjectKey);
                TextBlockSettings.SetActive(value);
            }
        }

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            SerializedObject serializedObject = null;

            return new SettingsProvider("Project/Timbo Jimbo/Text", SettingsScope.Project)
            {
                label = "Text",
                keywords = new HashSet<string>(new[] { "Text", "Font", "Theme" }),
                deactivateHandler = () => serializedObject = null,
                guiHandler = _ =>
                {
                    var previousLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 250;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(10);
                    EditorGUILayout.BeginVertical();
                    GUILayout.Space(10);

                    var active = Active;
                    var picked = (TextBlockSettings)EditorGUILayout.ObjectField("Settings Asset", active, typeof(TextBlockSettings), false);
                    if (picked != active)
                        Active = active = picked;

                    if (active == null)
                    {
                        EditorGUILayout.HelpBox("No settings asset. Texts use the OS default font and the built-in theme.", MessageType.Info);
                        if (GUILayout.Button("Create", GUILayout.Width(120)))
                        {
                            var created = CreateAsset();
                            if (created != null)
                                Active = created;
                        }
                    }
                    else
                    {
                        if (serializedObject == null || serializedObject.targetObject != active)
                            serializedObject = new SerializedObject(active);
                        serializedObject.Update();
                        EditorGUILayout.Space();
                        var property = serializedObject.GetIterator();
                        for (bool enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
                            if (property.propertyPath != "m_Script")
                                EditorGUILayout.PropertyField(property, true);
                        // Applying runs the asset's OnValidate, which redraws every text.
                        serializedObject.ApplyModifiedProperties();
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();

                    EditorGUIUtility.labelWidth = previousLabelWidth;
                }
            };
        }

        private static TextBlockSettings CreateAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create Text Block Settings", "TextBlockSettings", "asset", "Where to save the settings asset.");
            if (string.IsNullOrEmpty(path))
                return null;
            var asset = ScriptableObject.CreateInstance<TextBlockSettings>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
