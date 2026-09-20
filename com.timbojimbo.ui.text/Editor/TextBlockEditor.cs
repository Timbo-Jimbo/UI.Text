using TimboJimbo.UI.Text;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace TimboJimboEditor.UI.Text
{
    [CustomEditor(typeof(TextBlock))]
    [CanEditMultipleObjects]
    public sealed class TextBlockEditor : GraphicEditor
    {
        private SerializedProperty _text, _richText, _theme;
        private SerializedProperty _font, _fontSize, _bold, _italic, _underline, _strikethrough;
        private SerializedProperty _alignment, _wordWrap, _overflow, _maxLines, _direction, _characterSpacing, _wordSpacing, _paragraphSpacing;
        private SerializedProperty _onLinkClicked;

        private static bool s_ShowLayout = true;
        private static bool s_ShowEvents;

        protected override void OnEnable()
        {
            base.OnEnable();
            _text = serializedObject.FindProperty("_text");
            _richText = serializedObject.FindProperty("_richText");
            _theme = serializedObject.FindProperty("_theme");
            _font = serializedObject.FindProperty("_font");
            _fontSize = serializedObject.FindProperty("_fontSize");
            _bold = serializedObject.FindProperty("_bold");
            _italic = serializedObject.FindProperty("_italic");
            _underline = serializedObject.FindProperty("_underline");
            _strikethrough = serializedObject.FindProperty("_strikethrough");
            _alignment = serializedObject.FindProperty("_alignment");
            _wordWrap = serializedObject.FindProperty("_wordWrap");
            _overflow = serializedObject.FindProperty("_overflow");
            _maxLines = serializedObject.FindProperty("_maxLines");
            _direction = serializedObject.FindProperty("_direction");
            _characterSpacing = serializedObject.FindProperty("_characterSpacing");
            _wordSpacing = serializedObject.FindProperty("_wordSpacing");
            _paragraphSpacing = serializedObject.FindProperty("_paragraphSpacing");
            _onLinkClicked = serializedObject.FindProperty("_onLinkClicked");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_text);
            EditorGUILayout.PropertyField(_richText);
            if (_richText.boolValue)
                EditorGUILayout.PropertyField(_theme);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_font);
            EditorGUILayout.PropertyField(_fontSize);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Style");
                _bold.boolValue = GUILayout.Toggle(_bold.boolValue, "B", EditorStyles.miniButtonLeft);
                _italic.boolValue = GUILayout.Toggle(_italic.boolValue, "I", EditorStyles.miniButtonMid);
                _underline.boolValue = GUILayout.Toggle(_underline.boolValue, "U", EditorStyles.miniButtonMid);
                _strikethrough.boolValue = GUILayout.Toggle(_strikethrough.boolValue, "S", EditorStyles.miniButtonRight);
            }
            EditorGUILayout.PropertyField(m_Color);

            EditorGUILayout.Space();
            s_ShowLayout = EditorGUILayout.Foldout(s_ShowLayout, "Layout", true);
            if (s_ShowLayout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_alignment);
                EditorGUILayout.PropertyField(_wordWrap);
                EditorGUILayout.PropertyField(_overflow);
                EditorGUILayout.PropertyField(_maxLines);
                EditorGUILayout.PropertyField(_direction);
                EditorGUILayout.PropertyField(_characterSpacing);
                EditorGUILayout.PropertyField(_wordSpacing);
                EditorGUILayout.PropertyField(_paragraphSpacing);
                EditorGUI.indentLevel--;
            }

            s_ShowEvents = EditorGUILayout.Foldout(s_ShowEvents, "Events", true);
            if (s_ShowEvents)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_onLinkClicked);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            RaycastControlsGUI();
            MaskableControlsGUI();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
