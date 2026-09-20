using System;
using System.Collections.Generic;
using TimboJimbo.UI.Text.Markup;
using UnityEngine;

namespace TimboJimbo.UI.Text.Samples.Chat
{
    /// <summary>
    /// A named set of emotes for :name: tokens. Names not in the set are declined, so a time like 10:30:45 stays
    /// text. The prefab's <see cref="EmoteInline"/> asks the handler for the sprite when it is bound.
    /// </summary>
    [CreateAssetMenu(menuName = "Timbo Jimbo/UI/Inline Handlers/Samples/Emote Set", fileName = "Emotes")]
    public sealed class EmoteHandler : InlineHandler
    {
        [Serializable]
        public struct Emote
        {
            public string Name;
            public Sprite Sprite;
        }

        [Tooltip("Prefab with an EmoteInline on its root.")]
        [SerializeField] private GameObject _prefab;
        [Tooltip("Width and height of the emote, in em of the paragraph font size.")]
        [SerializeField, Min(0.1f)] private float _sizeEm = 1.25f;
        [SerializeField] private List<Emote> _emotes = new();

        public List<Emote> Emotes => _emotes;

        public Sprite Find(string name)
        {
            for (int i = 0; i < _emotes.Count; i++)
                if (string.Equals(_emotes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return _emotes[i].Sprite;
            return null;
        }

        public override bool Accepts(string payload) => Find(payload) != null;

        public override InlineObjectRequest Request(in InlineToken token, in InlineContext context)
        {
            return new InlineObjectRequest { Prefab = _prefab, SizeEm = new Vector2(_sizeEm, _sizeEm) };
        }
    }
}
