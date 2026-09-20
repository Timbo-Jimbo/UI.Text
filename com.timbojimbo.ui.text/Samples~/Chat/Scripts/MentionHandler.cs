using TimboJimbo.UI.Text.Markup;
using UnityEngine;

namespace TimboJimbo.UI.Text.Samples.Chat
{
    /// <summary>
    /// @name becomes a chip: an atomic box sized to the name, with a <see cref="MentionChip"/> prefab over it.
    /// Shows a handler whose size depends on the token, measured with <see cref="TextBlock.Measure"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Timbo Jimbo/UI/Inline Handlers/Samples/Mention", fileName = "Mention")]
    public sealed class MentionHandler : InlineHandler
    {
        [Tooltip("Prefab with a MentionChip on its root.")]
        [SerializeField] private GameObject _prefab;
        [Tooltip("Font for the chip's label; empty uses the project default.")]
        [SerializeField] private FontStack _font;
        [Tooltip("Label size relative to the paragraph font size.")]
        [SerializeField, Min(0.1f)] private float _labelScale = 0.9f;
        [Tooltip("Space either side of the label inside the chip, in em of the paragraph font size.")]
        [SerializeField, Min(0f)] private float _chipPaddingEm = 0.3f;
        [Tooltip("Chip height in em of the paragraph font size.")]
        [SerializeField, Min(0.1f)] private float _heightEm = 1.2f;

        public FontStack Font => _font;
        public float LabelScale => _labelScale;

        public override InlineObjectRequest Request(in InlineToken token, in InlineContext context)
        {
            float labelSize = context.FontSize * _labelScale;
            var size = TextBlock.Measure("@" + token.Payload, _font, labelSize, -1f, wordWrap: false);
            return new InlineObjectRequest
            {
                Prefab = _prefab,
                SizeEm = new Vector2(size.x / context.FontSize + _chipPaddingEm * 2f, _heightEm),
            };
        }
    }
}
