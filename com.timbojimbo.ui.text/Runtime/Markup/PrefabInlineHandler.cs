using UnityEngine;

namespace TimboJimbo.UI.Text.Markup
{
    /// <summary>
    /// An inline handler that needs no code: a trigger, a placement and a prefab. Atomic placement reserves
    /// <see cref="SizeEm"/> in the text and puts the prefab over it; Fragments and Block put the prefab behind
    /// the text, with optional layout padding either side. Subclass <see cref="InlineHandler"/> when the size or
    /// the styling depends on the token, as a mention chip that measures its name does.
    /// </summary>
    [CreateAssetMenu(menuName = "Timbo Jimbo/UI/Inline Handlers/Prefab Handler", fileName = "InlineHandler")]
    public sealed class PrefabInlineHandler : InlineHandler
    {
        [Tooltip("Instantiated over (Atomic) or behind (Fragments, Block) the token. Its root may implement IInlineContent to receive the token.")]
        [SerializeField] private GameObject _prefab;
        [Tooltip("Atomic only: the box reserved in the text, in em of the paragraph font size.")]
        [SerializeField] private Vector2 _sizeEm = Vector2.one;
        [Tooltip("Fragments and Block only: space the layout adds on each side of the text, in em, like CSS padding on an inline element.")]
        [SerializeField, Min(0f)] private float _paddingEm;

        public GameObject Prefab { get => _prefab; set => _prefab = value; }
        public Vector2 SizeEm { get => _sizeEm; set => _sizeEm = value; }

        public override float PaddingEm => _paddingEm;

        public override InlineObjectRequest Request(in InlineToken token, in InlineContext context)
        {
            return new InlineObjectRequest { Prefab = _prefab, SizeEm = _sizeEm };
        }
    }
}
