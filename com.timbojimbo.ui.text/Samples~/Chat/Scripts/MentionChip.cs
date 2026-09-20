using TimboJimbo.UI.Text.Markup;
using UnityEngine;

namespace TimboJimbo.UI.Text.Samples.Chat
{
    /// <summary>
    /// The chip a <see cref="MentionHandler"/> places over an @name: a background (any Graphic, a Box from the UI
    /// package in the sample prefab) and a nested TextBlock for the label. The chip owns its own line breaking,
    /// which is why an atomic token may hold a TextBlock while fragment content never does.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class MentionChip : MonoBehaviour, IInlineContent
    {
        [SerializeField] private TextBlock _label;

        public void Bind(in InlineToken token, in InlineContext context, in InlineFragment fragment)
        {
            if (token.Handler is MentionHandler mention)
            {
                _label.Font = mention.Font;
                _label.FontSize = context.FontSize * mention.LabelScale;
            }
            _label.Text = "@" + token.Payload;
        }

        public void Unbind() { }
    }
}
