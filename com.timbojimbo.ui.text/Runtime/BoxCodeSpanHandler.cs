using TimboJimbo.UI.Text.Markup;
using TimboJimbo.UI;
using UnityEngine;

namespace TimboJimbo.UI.Text
{
    /// <summary>
    /// Backgrounds for `code` spans and fenced code blocks drawn with a <see cref="Box"/>. Registered as the
    /// built-in handler for both kinds when the UI package is present; assign your own prefab or create a theme
    /// handler of the same kind to override it.
    /// </summary>
    [CreateAssetMenu(menuName = "Timbo Jimbo/UI/Inline Handlers/Box Code Background", fileName = "BoxCodeBackground")]
    public sealed class BoxCodeSpanHandler : InlineHandler
    {
        [Tooltip("Optional prefab with a BoxInlineBackground on its root; empty builds a plain Box background.")]
        [SerializeField] private GameObject _prefab;
        [SerializeField] private Color _color = new(1f, 1f, 1f, 0.1f);
        [SerializeField, Min(0f)] private float _strokeWidth = 1f;
        [SerializeField] private Color _strokeColor = new(1f, 1f, 1f, 0.15f);
        [Tooltip("x: horizontal padding, part of the layout (the text either side moves over). y: vertical padding, drawn over the line gap. In em.")]
        [SerializeField] private Vector2 _paddingEm = new(0.2f, 0.05f);
        [SerializeField] private float _cornerRadiusEm = 0.25f;

        private GameObject _template;

        public override float PaddingEm => _paddingEm.x;

        public override InlineObjectRequest Request(in InlineToken token, in InlineContext context)
        {
            return new InlineObjectRequest { Prefab = _prefab != null ? _prefab : Template };
        }

        private GameObject Template
        {
            get
            {
                if (_template != null)
                    return _template;

                var go = new GameObject($"Code background ({Kind})", typeof(RectTransform), typeof(CanvasRenderer));
                go.hideFlags = HideFlags.HideAndDontSave;
                go.SetActive(false);
                var background = go.AddComponent<BoxInlineBackground>();
                var box = go.AddComponent<Box>();
                box.raycastTarget = false;
                box.color = _color;
                box.StrokeWidth = 0f;
                background.Box = box;
                background.VerticalPaddingEm = _paddingEm.y;
                background.CornerRadiusEm = _cornerRadiusEm;

                if (_strokeWidth > 0f)
                {
                    // A second Box on a stretched child draws the border ring over the fill.
                    var strokeGo = new GameObject("Stroke", typeof(RectTransform), typeof(CanvasRenderer));
                    strokeGo.hideFlags = HideFlags.HideAndDontSave;
                    strokeGo.transform.SetParent(go.transform, false);
                    var strokeRect = (RectTransform)strokeGo.transform;
                    strokeRect.anchorMin = Vector2.zero;
                    strokeRect.anchorMax = Vector2.one;
                    strokeRect.offsetMin = Vector2.zero;
                    strokeRect.offsetMax = Vector2.zero;
                    var stroke = strokeGo.AddComponent<Box>();
                    stroke.raycastTarget = false;
                    stroke.color = _strokeColor;
                    stroke.StrokeWidth = _strokeWidth;
                    stroke.Concentric = true;
                }

                _template = go;
                return _template;
            }
        }

        private void OnDisable()
        {
            if (_template != null)
                DestroyImmediate(_template);
            _template = null;
        }

        // ---- Built-in registration ----

        private static BoxCodeSpanHandler s_Inline;
        private static BoxCodeSpanHandler s_Block;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void RegisterBuiltIns()
        {
            if (s_Inline == null)
            {
                s_Inline = CreateInstance<BoxCodeSpanHandler>();
                s_Inline.name = "Built-in code background";
                s_Inline.hideFlags = HideFlags.HideAndDontSave;
                s_Inline.Configure("code", default, InlinePlacement.Fragments);
            }
            if (s_Block == null)
            {
                s_Block = CreateInstance<BoxCodeSpanHandler>();
                s_Block.name = "Built-in code block background";
                s_Block.hideFlags = HideFlags.HideAndDontSave;
                s_Block._paddingEm = new Vector2(0.4f, 0.25f);
                s_Block.Configure("codeblock", default, InlinePlacement.Block);
            }
            TextTheme.RegisterBuiltIn(s_Inline);
            TextTheme.RegisterBuiltIn(s_Block);
        }
    }
}
