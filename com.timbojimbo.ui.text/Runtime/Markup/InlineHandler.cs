using System;
using UnityEngine;

namespace TimboJimbo.UI.Text.Markup
{
    public enum InlineTriggerKind
    {
        /// <summary>Text between an opening and a closing delimiter on one line, like `code` or :emote:.</summary>
        Delimited,
        /// <summary>A prefix character followed by a word, like @name or #channel.</summary>
        Prefix,
    }

    /// <summary>What the parser looks for to hand a token to a handler.</summary>
    [Serializable]
    public struct InlineTrigger
    {
        public InlineTriggerKind Kind;
        [Tooltip("Delimited: the opening delimiter. Prefix: the single prefix character.")]
        public string Open;
        [Tooltip("Delimited: the closing delimiter; empty repeats the opening one.")]
        public string Close;
    }

    public enum InlinePlacement
    {
        /// <summary>The payload stays text in the paragraph; the handler only styles it (and may decorate each line fragment).</summary>
        Fragments,
        /// <summary>The token becomes one box in the flow that never splits; a prefab is placed over it.</summary>
        Atomic,
        /// <summary>The payload stays text; one prefab covers the full width of every line it touches, for fenced code blocks and quotes.</summary>
        Block,
    }

    /// <summary>What an <see cref="InlineHandler"/> may change about the text of a token it claimed.</summary>
    public struct SpanStyle
    {
        /// <summary>The font stack to draw with, or null to keep the paragraph's.</summary>
        public FontStack Font;
        /// <summary>Multiplier on the paragraph font size; 0 keeps it.</summary>
        public float SizeScale;
        public bool HasColor;
        public Color32 Color;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikethrough;
    }

    /// <summary>Everything a handler or inline object may need to size or bind itself.</summary>
    public readonly struct InlineContext
    {
        public readonly TextBlock Owner;
        /// <summary>Paragraph font size in canvas units.</summary>
        public readonly float FontSize;
        public readonly Color Color;

        public InlineContext(TextBlock owner, float fontSize, Color color)
        {
            Owner = owner;
            FontSize = fontSize;
            Color = color;
        }
    }

    /// <summary>Box an <see cref="InlinePlacement.Atomic"/> handler wants reserved, in em of the paragraph font size.</summary>
    public struct InlineObjectRequest
    {
        public GameObject Prefab;
        public Vector2 SizeEm;
        /// <summary>Height of the box above the baseline, in em; the rest hangs below. 0 centres the box on the text, halfway between the font's ascent and descent.</summary>
        public float AscentEm;
    }

    /// <summary>
    /// A rectangle a prefab instance is placed over: the whole box for atomic content, or one line's slice of
    /// the range for fragment content. Canvas units, in the TextBlock's containers' local space.
    /// </summary>
    public readonly struct InlineFragment
    {
        public readonly Rect Rect;
        /// <summary>This fragment's index and the total, so a prefab can round only its outer corners.</summary>
        public readonly int Index, Count;
        /// <summary>Text metrics for this line, in canvas units from the fragment's top: baseline distance, ascent and descent.</summary>
        public readonly float Baseline, Ascent, Descent;

        public InlineFragment(Rect rect, int index, int count, float baseline, float ascent, float descent)
        {
            Rect = rect;
            Index = index;
            Count = count;
            Baseline = baseline;
            Ascent = ascent;
            Descent = descent;
        }

        public bool IsFirst => Index == 0;
        public bool IsLast => Index == Count - 1;
    }

    /// <summary>Implemented by the root of a prefab an <see cref="InlineHandler"/> places over a token.</summary>
    public interface IInlineContent
    {
        void Bind(in InlineToken token, in InlineContext context, in InlineFragment fragment);
        void Unbind();
    }

    /// <summary>
    /// Extends the markup: declares what to look for, and either styles the matched text (optionally putting a
    /// prefab behind each line fragment of it) or replaces it with an atomic box a prefab is placed over.
    /// </summary>
    public abstract class InlineHandler : ScriptableObject
    {
        [Tooltip("Identifies the token kind; the built-in kinds are 'code' and 'codeblock'.")]
        [SerializeField] private string _kind = "custom";
        [SerializeField] private InlineTrigger _trigger = new() { Kind = InlineTriggerKind.Delimited, Open = ":", Close = ":" };
        [SerializeField] private InlinePlacement _placement = InlinePlacement.Fragments;

        public string Kind => _kind;
        public InlineTrigger Trigger => _trigger;
        public InlinePlacement Placement => _placement;

        /// <summary>
        /// Fragments and Block handlers: space the layout adds on each side of the token's text, in em of the
        /// token's font size, like CSS padding on an inline element. The text either side moves over, a wrapped
        /// token gets it at its start and end only, and the fragment rectangles include it.
        /// </summary>
        public virtual float PaddingEm => 0f;

        /// <summary>
        /// Whether a token the trigger matched is really this handler's. An emote handler checks its table here,
        /// so ":30:" inside a time stays text. The default accepts everything.
        /// </summary>
        public virtual bool Accepts(string payload) => true;

        /// <summary>For handlers created in code: sets the kind, trigger and placement. An empty trigger means the parser never matches it (built-in syntax only).</summary>
        protected void Configure(string kind, InlineTrigger trigger, InlinePlacement placement)
        {
            _kind = kind;
            _trigger = trigger;
            _placement = placement;
        }

        /// <summary>Fragment handlers: style the token's text. Called once per token per rebuild.</summary>
        public virtual void Style(in InlineToken token, ref SpanStyle style) { }

        /// <summary>
        /// The prefab to place over the token, or null for none. Atomic handlers must also return the box size.
        /// Fragment handlers get one instance per line fragment and may ignore the size.
        /// </summary>
        public virtual InlineObjectRequest Request(in InlineToken token, in InlineContext context) => default;
    }
}
