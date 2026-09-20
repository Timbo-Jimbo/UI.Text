using TimboJimbo.UI.Text.Markup;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Text.Samples.Chat
{
    /// <summary>Shows the sprite an <see cref="EmoteHandler"/> maps the token's name to. Put it on the root of the emote prefab.</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class EmoteInline : MonoBehaviour, IInlineContent
    {
        [SerializeField] private Image _image;

        public void Bind(in InlineToken token, in InlineContext context, in InlineFragment fragment)
        {
            var sprite = token.Handler is EmoteHandler emotes ? emotes.Find(token.Payload) : null;
            _image.sprite = sprite;
            _image.enabled = sprite != null;
        }

        public void Unbind() { }

        private void Reset()
        {
            _image = GetComponent<Image>();
        }
    }
}
