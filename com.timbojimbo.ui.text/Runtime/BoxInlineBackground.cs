using TimboJimbo.UI.Text.Markup;
using TimboJimbo.UI;
using UnityEngine;

namespace TimboJimbo.UI.Text
{
    /// <summary>
    /// A <see cref="Box"/> placed behind inline text, rounding only the outer corners so a span that wraps reads
    /// as one shape broken across lines. Horizontal padding is the handler's business (it is part of the layout,
    /// see <see cref="InlineHandler.PaddingEm"/>); vertical padding is drawn over the line gap, as CSS does for
    /// inline elements. Put it on the root of a background prefab.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BoxInlineBackground : MonoBehaviour, IInlineContent
    {
        [SerializeField] private Box _box;
        [Tooltip("Extra height above and below the text, in em of the paragraph font size. Drawn over the line gap; the layout does not change.")]
        [SerializeField, Min(0f)] private float _verticalPaddingEm = 0.05f;
        [Tooltip("Corner radius in em of the paragraph font size.")]
        [SerializeField] private float _cornerRadiusEm = 0.25f;

        public Box Box { get => _box; set => _box = value; }
        public float VerticalPaddingEm { get => _verticalPaddingEm; set => _verticalPaddingEm = value; }
        public float CornerRadiusEm { get => _cornerRadiusEm; set => _cornerRadiusEm = value; }

        public void Bind(in InlineToken token, in InlineContext context, in InlineFragment fragment)
        {
            var rt = (RectTransform)transform;
            float padding = _verticalPaddingEm * context.FontSize;
            rt.anchoredPosition += new Vector2(0f, padding);
            rt.sizeDelta += new Vector2(0f, padding * 2f);

            if (_box == null)
                return;
            float r = _cornerRadiusEm * context.FontSize;
            // Box order: x = top-left, y = top-right, z = bottom-right, w = bottom-left.
            _box.CornerRadii = new Vector4(fragment.IsFirst ? r : 0f, fragment.IsLast ? r : 0f, fragment.IsLast ? r : 0f, fragment.IsFirst ? r : 0f);
        }

        public void Unbind() { }

        private void Reset()
        {
            _box = GetComponent<Box>();
        }
    }
}
