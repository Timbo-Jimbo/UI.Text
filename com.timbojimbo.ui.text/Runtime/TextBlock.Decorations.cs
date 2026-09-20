using UnityEngine;

namespace TimboJimbo.UI.Text
{
    public sealed partial class TextBlock
    {
        [SerializeField] private bool _underline;
        [SerializeField] private bool _strikethrough;

        /// <summary>Draws a line under the whole text, on top of any markup underlines.</summary>
        public bool Underline
        {
            get => _underline;
            set { if (_underline != value) { _underline = value; SetVerticesDirty(); } }
        }

        public bool Strikethrough
        {
            get => _strikethrough;
            set { if (_strikethrough != value) { _strikethrough = value; SetVerticesDirty(); } }
        }
    }
}
