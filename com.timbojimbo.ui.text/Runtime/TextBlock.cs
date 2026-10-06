using System;
using System.Collections.Generic;
using TimboJimbo.UI.Text.Bridge;
using TimboJimbo.UI.Text.Markup;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimboJimbo.UI.Text
{
    /// <summary>
    /// UGUI text laid out and shaped by Unity's Advanced Text Generator: HarfBuzz shaping, ICU line breaking and
    /// bidi, OS font fallback and colour emoji, with no native plugins. Understands a small Markdown dialect and
    /// places prefabs over inline content. The layout itself lives in a <see cref="TextLayout"/>; this component
    /// feeds it the rect and settings and turns the result into UGUI: a mesh on a child renderer and prefabs in
    /// two child containers. The root draws nothing itself; it owns three children in draw order: decorations,
    /// the glyph renderer, and inline objects.
    /// </summary>
    [AddComponentMenu("Timbo Jimbo/UI/Text Block")]
    [RequireComponent(typeof(CanvasRenderer))]
    [ExecuteAlways]
    public sealed partial class TextBlock : MaskableGraphic, ILayoutElement, IPointerClickHandler
    {
        private const string SdfShaderName = "TimboJimbo/UI/TextSdf";
        private const string BitmapShaderName = "TimboJimbo/UI/TextBitmap";
        private const string DecorationsName = "Decorations";
        private const string GlyphsName = "Glyphs";
        private const string ObjectsName = "Objects";
        private const AdditionalCanvasShaderChannels RequiredChannels = AdditionalCanvasShaderChannels.TexCoord1;

        [SerializeField, TextArea(3, 10)] private string _text = "";
        [Tooltip("Parse the text as Markdown: **bold**, *italic*, __underline__, ~~strike~~, `code`, [links](url), # headings, - lists, > quotes.")]
        [SerializeField] private bool _richText = true;
        [Tooltip("The Font Stack to draw with. Empty uses the project default, then the OS default font.")]
        [SerializeField] private FontStack _font;
        [SerializeField, Min(1f)] private float _fontSize = 24f;
        [SerializeField] private bool _bold;
        [SerializeField] private bool _italic;
        [SerializeField] private TextAnchor _alignment = TextAnchor.UpperLeft;
        [SerializeField] private bool _wordWrap = true;
        [Tooltip("Let a wrapped text break inside a word when a line is too narrow for it (CSS overflow-wrap: anywhere): its minimum width in layout is 0, so a growing or fitted node holding long unbroken words can shrink and wrap.")]
        [SerializeField] private bool _breakWordsAnywhere;
        [SerializeField] private TextBlockOverflow _overflow = TextBlockOverflow.Overflow;
        [SerializeField, Min(0)] private int _maxLines;
        [SerializeField] private TextBlockDirection _direction = TextBlockDirection.LeftToRight;
        [SerializeField] private float _characterSpacing;
        [SerializeField] private float _wordSpacing;
        [SerializeField] private float _paragraphSpacing;
        [Tooltip("How markup renders. Empty uses the project default, then the built-in theme.")]
        [SerializeField] private TextTheme _theme;
        [SerializeField] private UnityEvent<string> _onLinkClicked = new();
        // The engine's ICU data (line breaking, bidi, emoji sequences). Holding a reference to the built-in asset
        // is what gets it into player builds; the editor assigns it automatically.
        [SerializeField, HideInInspector] private UnityEngine.TextAsset _icuData;

        private readonly TextLayout _layout = new();
        private TextBlockGlyphs _glyphs;
        private RectTransform _decorations;
        private RectTransform _objects;
        private bool _measuresDirty = true;
        private bool _longestWordDirty = true;
        private float _longestWord;
        // What a LayoutNode's layout last asked it to measure, kept until the text or how it is drawn changes, as the
        // measures above are: the layout asks every frame, so it is measured once, not every frame.
        private bool _layoutMeasuresDirty = true;
        private Vector2 _layoutUnwrapped;
        private float _layoutWrapWidth = float.NaN;
        private Vector2 _layoutWrapped;

        // The size a LayoutNode on it is given, which its rect may still be springing towards; negative when nothing
        // sizes it so (it is laid out at its rect). Never saved: layout tells it again once it is laid out.
        private Vector2 _arranged = new(-1f, -1f);
        private float _measuredForWidth = float.NaN;
        private float _preferredWidth;
        private float _preferredHeight;

        // Whether the layout no longer matches the text and how it is drawn: every such change reaches SetVerticesDirty,
        // which marks it stale. The glyph rect's size and the canvas scale it was laid out at are compared as well, so
        // a resize that marks nothing still lays it out again. Never saved: a reloaded text has no layout yet.
        [NonSerialized] private bool _layoutStale = true;
        [NonSerialized] private Vector2 _laidOutSize;
        [NonSerialized] private float _laidOutScale;

        /// <summary>Raised with the link's href when a link in the text is clicked.</summary>
        public event Action<string> LinkClicked;

        // ---- Properties ----

        public string Text
        {
            get => _text;
            set
            {
                value ??= "";
                if (_text == value) return;
                _text = value;
                MarkTextChanged();
            }
        }

        public bool RichText
        {
            get => _richText;
            set { if (_richText != value) { _richText = value; MarkTextChanged(); } }
        }

        /// <summary>The font stack to draw with; null uses the project default, then the OS default font.</summary>
        public FontStack Font
        {
            get => _font;
            set
            {
                if (_font == value) return;
                _font = value;
                MarkTextChanged();
                SetMaterialDirty();
            }
        }

        public float FontSize
        {
            get => _fontSize;
            set
            {
                value = Mathf.Max(1f, value);
                if (Mathf.Approximately(_fontSize, value)) return;
                _fontSize = value;
                MarkTextChanged();
            }
        }

        public bool Bold
        {
            get => _bold;
            set { if (_bold != value) { _bold = value; MarkTextChanged(); } }
        }

        public bool Italic
        {
            get => _italic;
            set { if (_italic != value) { _italic = value; MarkTextChanged(); } }
        }

        public TextAnchor Alignment
        {
            get => _alignment;
            set { if (_alignment != value) { _alignment = value; PlaceChildren(); SetVerticesDirty(); } }
        }

        public bool WordWrap
        {
            get => _wordWrap;
            set { if (_wordWrap != value) { _wordWrap = value; MarkTextChanged(); } }
        }

        /// <summary>
        /// Lets a wrapped text break inside a word when a line is too narrow for it, as CSS overflow-wrap: anywhere
        /// does: the minimum width it reports to a LayoutNode's layout is 0 rather than its longest word, so a Grow or
        /// Fit node holding long unbroken words (URLs, CJK text without spaces) can shrink and wrap instead of
        /// widening its row. Where lines break is still the generator's ICU line breaking; UGUI's layout gets a
        /// minimum width of 0 either way.
        /// </summary>
        public bool BreakWordsAnywhere
        {
            get => _breakWordsAnywhere;
            set { if (_breakWordsAnywhere != value) { _breakWordsAnywhere = value; SetLayoutDirty(); } }
        }

        public TextBlockOverflow Overflow
        {
            get => _overflow;
            set { if (_overflow != value) { _overflow = value; MarkTextChanged(); } }
        }

        /// <summary>Upper bound on lines drawn; 0 means no limit.</summary>
        public int MaxLines
        {
            get => _maxLines;
            set { value = Mathf.Max(0, value); if (_maxLines != value) { _maxLines = value; MarkTextChanged(); } }
        }

        public TextBlockDirection Direction
        {
            get => _direction;
            set { if (_direction != value) { _direction = value; SetVerticesDirty(); } }
        }

        public float CharacterSpacing
        {
            get => _characterSpacing;
            set { if (!Mathf.Approximately(_characterSpacing, value)) { _characterSpacing = value; MarkTextChanged(); } }
        }

        public float WordSpacing
        {
            get => _wordSpacing;
            set { if (!Mathf.Approximately(_wordSpacing, value)) { _wordSpacing = value; MarkTextChanged(); } }
        }

        public float ParagraphSpacing
        {
            get => _paragraphSpacing;
            set { if (!Mathf.Approximately(_paragraphSpacing, value)) { _paragraphSpacing = value; MarkTextChanged(); } }
        }

        public TextTheme Theme
        {
            get => _theme;
            set { if (_theme != value) { _theme = value; MarkTextChanged(); } }
        }

        /// <summary>Raised with the link's href when a link in the text is clicked (inspector-wired).</summary>
        public UnityEvent<string> OnLinkClicked => _onLinkClicked;

        /// <summary>True when the last layout cut the text short.</summary>
        public bool IsTruncated => _layout.IsElided;

        /// <summary>The font's line height (the distance between baselines) at the current font size, in local units.</summary>
        public float LineHeight => TextLayout.LineHeightOf(_font, _fontSize);

        /// <summary>The layout behind this text: the parsed document, the laid-out text and its queries, as of the last layout (see <see cref="EnsureLayout"/>).</summary>
        public TextLayout Layout => _layout;

        /// <summary>Marks every loaded text for a rebuild, for project-wide settings that change how texts draw.</summary>
        internal static void RebuildAll()
        {
            foreach (var text in FindObjectsByType<TextBlock>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                text.MarkTextChanged();
                text.SetMaterialDirty();
            }
        }

        /// <summary>The parsed form of the current text (lines, runs, links and tokens, in source indices) as of the last layout.</summary>
        public TextDocument Document => _layout.Document;

        /// <summary>Container for inline-content prefabs, above the glyphs.</summary>
        public RectTransform ObjectsContainer
        {
            get
            {
                EnsureChildren();
                return _objects;
            }
        }

        /// <summary>Container for decoration prefabs, below the glyphs.</summary>
        public RectTransform DecorationsContainer
        {
            get
            {
                EnsureChildren();
                return _decorations;
            }
        }

        private TextTheme EffectiveTheme
        {
            get
            {
                if (_theme != null) return _theme;
                var settings = TextBlockSettings.Instance;
                return settings != null && settings.DefaultTheme != null ? settings.DefaultTheme : TextTheme.Default;
            }
        }

        // ---- Measuring ----

        /// <summary>
        /// The size a text would take, in canvas units, without a component: for virtualised lists that need row
        /// heights ahead of time. A negative width is unconstrained. Markup is parsed when <paramref name="richText"/> is set.
        /// </summary>
        public static Vector2 Measure(string text, FontStack font, float fontSize, float width, bool wordWrap = true, bool bold = false, bool italic = false, bool richText = false, TextTheme theme = null)
            => TextLayout.Measure(text, font, fontSize, width, wordWrap, bold, italic, richText, theme);

        public float minWidth => 0f;
        public float minHeight => 0f;
        public float flexibleWidth => -1f;
        public float flexibleHeight => -1f;
        public int layoutPriority => 0;

        public float preferredWidth
        {
            get
            {
                EnsureMeasures();
                return _preferredWidth;
            }
        }

        public float preferredHeight
        {
            get
            {
                EnsureMeasures();
                return _preferredHeight;
            }
        }

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }

        // ILayoutElement answers for the width on the transform, cached until the text or that width changes.
        private void EnsureMeasures()
        {
            float width = rectTransform.rect.width;
            if (!_measuresDirty && Mathf.Approximately(width, _measuredForWidth))
                return;
            _measuresDirty = false;
            _measuredForWidth = width;
            _preferredWidth = MeasureInUnits(-1f).x;
            _preferredHeight = MeasureInUnits(_wordWrap ? width : -1f).y;
        }

        // Layout is done in canvas units, never in device pixels: the same numbers measure the text and place its
        // glyphs, at every canvas scale, so what the layout was told fits is what is drawn. The canvas scale only
        // sizes the anti-aliasing margin of the quads.

        /// <summary>The size of this text laid out at a width in canvas units (negative for unconstrained), in canvas units.</summary>
        private Vector2 MeasureInUnits(float widthUnits) => MeasureInUnits(_text, widthUnits);

        private Vector2 MeasureInUnits(string text, float widthUnits)
            => Measure(text, _font, _fontSize, widthUnits < 0f ? -1f : widthUnits, _wordWrap, _bold, _italic, _richText, EffectiveTheme);

        /// <summary>
        /// The widest whitespace-separated run of the text, in canvas units: how narrow a wrapped text can go
        /// before words themselves would break. Measured per word, so it is cached until the text changes.
        /// </summary>
        private float LongestWordWidth()
        {
            if (!_longestWordDirty)
                return _longestWord;
            _longestWordDirty = false;
            _longestWord = 0f;
            if (!string.IsNullOrEmpty(_text))
            {
                foreach (var word in _text.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries))
                    _longestWord = Mathf.Max(_longestWord, MeasureInUnits(word, -1f).x);
            }
            return _longestWord;
        }

#if TJ_TEXT_LAYOUT
        // ---- ILayoutMeasurable (UI Layout package) ----

        /// <summary>
        /// The content of a LayoutNode: the text's size at the width the layout offers, as a pure measure. Kept until
        /// the text or how it is drawn changes: unwrapped, and wrapped to the last width asked for. A width it fits in
        /// unwrapped needs no measure of its own: it lays out the same.
        /// </summary>
        Vector2 TimboJimbo.UI.Layout.ILayoutMeasurable.Measure(float availableWidth)
        {
            var unwrapped = LayoutUnwrapped();
            // A hair under its own width (a fitted node's width less its padding, in floats) still fits it.
            if (!_wordWrap || availableWidth < 0f || availableWidth >= unwrapped.x - 0.01f)
                return unwrapped;
            if (availableWidth != _layoutWrapWidth)
            {
                _layoutWrapWidth = availableWidth;
                _layoutWrapped = MeasureInUnits(availableWidth);
            }
            return _layoutWrapped;
        }

        /// <summary>
        /// The longest unbreakable run, so a wrapped text is never squeezed narrower than its longest word; 0 when words
        /// may break anywhere (<see cref="BreakWordsAnywhere"/>).
        /// </summary>
        float TimboJimbo.UI.Layout.ILayoutMeasurable.MinWidth => !_wordWrap ? LayoutUnwrapped().x : _breakWordsAnywhere ? 0f : LongestWordWidth();

        /// <summary>
        /// The size its LayoutNode is given: while the node's rect springs there, the text is laid out at it (wrapped
        /// to its width, so it has the lines it will end with from the start), held at its alignment's side of the rect,
        /// as a node's children are laid out at its new size. Negative: laid out at its rect again.
        /// </summary>
        void TimboJimbo.UI.Layout.ILayoutMeasurable.Arrange(Vector2 size)
        {
            if (size.x < 0f || size.y < 0f)
                size = new Vector2(-1f, -1f);
            if (size == _arranged)
                return;
            _arranged = size;
            PlaceChildren();
            SetVerticesDirty();
        }

        // The text's unwrapped size, measured again only once it has changed (which drops the wrapped one too).
        private Vector2 LayoutUnwrapped()
        {
            if (_layoutMeasuresDirty)
            {
                _layoutMeasuresDirty = false;
                _layoutUnwrapped = MeasureInUnits(-1f);
                _layoutWrapWidth = float.NaN;
            }
            return _layoutUnwrapped;
        }
#endif

        // ---- Lifecycle ----

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureHook();
            EnsureChildren();
            var canvas = this.canvas;
            if (canvas != null)
                canvas.additionalShaderChannels |= RequiredChannels;
#if UNITY_EDITOR
            AssignIcuData();
#endif
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ReleaseInlineContent();
        }

        protected override void OnDestroy()
        {
            ReleaseInlineContent();
            _layout.Dispose();
            base.OnDestroy();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            var canvas = this.canvas;
            if (canvas != null)
                canvas.additionalShaderChannels |= RequiredChannels;
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            AssignIcuData();
        }

        protected override void OnValidate()
        {
            AssignIcuData();
            _fontSize = Mathf.Max(1f, _fontSize);
            _maxLines = Mathf.Max(0, _maxLines);
            _measuresDirty = true;
            _longestWordDirty = true;
            _layoutMeasuresDirty = true;
            _layoutStale = true;
            base.OnValidate();
        }

        private void AssignIcuData()
        {
            if (_icuData == null)
                _icuData = AtgEngine.FindEditorIcuData();
        }
#endif

        private void MarkTextChanged()
        {
            _measuresDirty = true;
            _longestWordDirty = true;
            _layoutMeasuresDirty = true;
            SetVerticesDirty();
            SetLayoutDirty();
        }

        public override void SetVerticesDirty()
        {
            _measuresDirty = true;
            _longestWordDirty = true;
            _layoutMeasuresDirty = true;
            _layoutStale = true;
            base.SetVerticesDirty();
        }

        private void EnsureChildren()
        {
            if (_glyphs != null && _decorations != null && _objects != null)
                return;
            _decorations = FindOrCreateChild(DecorationsName, 0);
            var glyphsRect = FindOrCreateChild(GlyphsName, 1);
            _objects = FindOrCreateChild(ObjectsName, 2);
            _glyphs = glyphsRect.GetComponent<TextBlockGlyphs>();
            if (_glyphs == null)
                _glyphs = glyphsRect.gameObject.AddComponent<TextBlockGlyphs>();
            _glyphs.Bind(this);
        }

        private RectTransform FindOrCreateChild(string childName, int siblingIndex)
        {
            RectTransform child = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                var candidate = transform.GetChild(i);
                if (candidate.name == childName && candidate is RectTransform rt)
                {
                    child = rt;
                    break;
                }
            }
            if (child == null)
            {
                var go = new GameObject(childName, typeof(RectTransform));
                go.hideFlags = HideFlags.DontSave;
                child = go.GetComponent<RectTransform>();
                child.SetParent(transform, false);
                if (childName == GlyphsName)
                    go.AddComponent<CanvasRenderer>();
            }
            PlaceChild(child);
            child.localScale = Vector3.one;
            child.SetSiblingIndex(siblingIndex);
            return child;
        }

        // Places the children the text is drawn on, once they are there.
        private void PlaceChildren()
        {
            if (_decorations != null) PlaceChild(_decorations);
            if (_glyphs != null) PlaceChild(_glyphs.rectTransform);
            if (_objects != null) PlaceChild(_objects);
        }

        // A child the text is drawn on covers its rect, pivoted at the top-left corner, where the geometry starts. While
        // a layout size is arranged, it is that size instead, held at the side of the rect the text is aligned to (a
        // left-aligned text at the left, a centred one at the centre), which is where it lines up once the rect gets there.
        private void PlaceChild(RectTransform child)
        {
            child.pivot = new Vector2(0f, 1f);
            if (_arranged.x < 0f)
            {
                child.anchorMin = Vector2.zero;
                child.anchorMax = Vector2.one;
                child.offsetMin = Vector2.zero;
                child.offsetMax = Vector2.zero;
                return;
            }
            int a = (int)_alignment;
            // TextAnchor runs upper-left to lower-right, a row of three at a time.
            float x = (a % 3) * 0.5f, down = (a / 3) * 0.5f;
            var anchor = new Vector2(x, 1f - down);
            child.anchorMin = anchor;
            child.anchorMax = anchor;
            child.sizeDelta = _arranged;
            // The pivot (its top-left corner) is that far left of the anchor and that far above it.
            child.anchoredPosition = new Vector2(-x * _arranged.x, down * _arranged.y);
        }

        // ---- Queries ----
        // Source indices (with Rich Text off, UTF-16 indices into Text) and this component's local space (canvas units,
        // y up), whatever its pivot and wherever a layout has arranged the text inside its rect. They answer for the
        // last layout, which is made in the canvas rebuild: call EnsureLayout first when the text, its settings or the
        // rect may have changed this frame. An index where a line wraps both ends the line before and starts the line
        // after; the caret queries take it upstream, at the end of the line before, when asked to, as a caret's
        // affinity does in UIKit and Flutter. A text field keeps that affinity with its caret: upstream after End, or
        // after a tap past a wrapped line's end (GetIndexAt says so), and downstream after any other move.

        /// <summary>
        /// Lays the text out now if the text, how it is drawn or its rect changed since it was last laid out, so the
        /// queries below answer for the current text and rect this frame rather than as of the last canvas rebuild.
        /// Cheap when nothing changed; the rebuild that follows draws this layout rather than making it again. Call it
        /// once layout has sized the rect for the frame (from Canvas.preWillRenderCanvases, after the layout pass).
        /// </summary>
        public void EnsureLayout()
        {
            EnsureChildren();
            if (!_layoutStale && _glyphs.rectTransform.rect.size == _laidOutSize && CanvasScale == _laidOutScale)
                return;
            Generate();
        }

        /// <summary>
        /// The caret at a source index: zero wide, at the insertion point, as tall as the line the index is on. An index
        /// where a line wraps is drawn at the end of the line before when <paramref name="upstream"/>, otherwise at the
        /// start of the line after; one after a final line break, on the empty line it ends with. In an empty text the
        /// caret sits on the first line at the side the alignment names, as tall as <see cref="LineHeight"/>.
        /// </summary>
        public Rect GetCaretRect(int index, bool upstream = false)
        {
            var r = _layout.GetCaretRect(index, upstream);
            var origin = LayoutOrigin();
            return new Rect(origin.x + r.xMin, origin.y - r.yMax, 0f, r.height);
        }

        /// <summary>
        /// The source index of the caret position nearest a point in local space, held within the text: a point above
        /// the first line or below the last is matched on that line, and one past either end of a line takes that end.
        /// </summary>
        public int GetIndexAt(Vector2 localPoint) => GetIndexAt(localPoint, out _);

        /// <summary>
        /// The source index of the caret position nearest a point in local space, as <see cref="GetIndexAt(Vector2)"/>,
        /// and whether to take it upstream: true when the point is on a wrapped line past its end, whose nearest position
        /// is the index the next line starts at.
        /// </summary>
        public int GetIndexAt(Vector2 localPoint, out bool upstream)
        {
            var origin = LayoutOrigin();
            return _layout.GetIndexAt(new Vector2(localPoint.x - origin.x, origin.y - localPoint.y), out upstream);
        }

        /// <summary>How many lines the text has: at least 1, and a text ending in a line break ends with an empty line.</summary>
        public int LineCount => _layout.LineCount;

        /// <summary>
        /// The line a source index is on, counted from 0. An index where a line wraps is on the line before when
        /// <paramref name="upstream"/>, otherwise on the line after.
        /// </summary>
        public int GetLineAt(int index, bool upstream = false) => _layout.GetLineAt(index, upstream);

        /// <summary>The source index of a line's first character; a line past the last starts at the text's end.</summary>
        public int GetLineStart(int line) => _layout.GetLineStart(line);

        /// <summary>
        /// The source index at the end of a line's text, before its line break if it ends in one. A line that wraps ends
        /// at the index the next one starts at; take it upstream (<see cref="GetCaretRect"/>, <see cref="GetLineAt"/>)
        /// to keep it on this line.
        /// </summary>
        public int GetLineEnd(int line) => _layout.GetLineEnd(line);

        /// <summary>
        /// Rectangles covering the source characters in [start, end), one per line; a text with nothing to draw (only
        /// spaces or line breaks) answers too. Empty until the text has been laid out.
        /// </summary>
        public void GetCharacterRects(int start, int end, List<Rect> results)
        {
            results.Clear();
            if (_glyphs == null)
                return;
            _layout.GetSourceRangeRects(start, end, s_Rects);
            var origin = LayoutOrigin();
            for (int i = 0; i < s_Rects.Count; i++)
            {
                var r = s_Rects[i];
                results.Add(new Rect(origin.x + r.xMin, origin.y - r.yMax, r.width, r.height));
            }
        }

        // Where layout space (canvas units from the text's top-left corner, y down) starts in local space. The text is
        // laid out on the glyph child, from its rect's top-left corner (see BuildGeometry). That child is this
        // component's own, never turned or scaled, so its space is this one moved by where its pivot sits in it: its
        // local position, wherever PlaceChild put it (over this rect, or at an arranged size inside it).
        private Vector2 LayoutOrigin()
        {
            EnsureChildren();
            var rect = _glyphs.GetPixelAdjustedRect();
            Vector2 origin = _glyphs.rectTransform.localPosition;
            return new Vector2(origin.x + rect.xMin, origin.y + rect.yMax);
        }

        // ---- Links ----

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_glyphs == null || Document.Links.Count == 0)
                return;
            var eventCamera = eventData.pressEventCamera != null ? eventData.pressEventCamera : canvas != null ? canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_glyphs.rectTransform, eventData.position, eventCamera, out var local))
                return;
            var rect = _glyphs.GetPixelAdjustedRect();
            int link = _layout.LinkAt(new Vector2(local.x - rect.xMin, rect.yMax - local.y));
            if (link < 0)
                return;
            var href = Document.Links[link].Href;
            LinkClicked?.Invoke(href);
            _onLinkClicked.Invoke(href);
        }

        // ---- Rendering ----

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            // The root draws nothing itself; it exists for masking, raycasts, colour and layout.
            vh.Clear();
        }

        protected override void UpdateGeometry()
        {
            base.UpdateGeometry();
            RebuildGlyphs();
        }

        /// <summary>
        /// Lays the text out unless the last layout still matches it (see <see cref="EnsureLayout"/>), hands the glyph
        /// geometry to the child renderer and places inline content.
        /// </summary>
        internal void RebuildGlyphs()
        {
            ClearGeometry();

            EnsureLayout();
            if (_layout.HasContent)
                BuildGeometry();
            _glyphs.Apply(s_Positions, s_Colors, s_Uv0, s_Uv1, s_GroupTriangles, s_Materials);

            // Inline prefabs are enabled after UGUI's rebuild loop, which forbids it while running.
            QueueInlineContent();
        }

        private float CanvasScale
        {
            get
            {
                var canvas = this.canvas;
                return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            }
        }

        private void Generate()
        {
            // The layout gets the rect as sized, not the pixel-adjusted one: on a pixel-perfect canvas the
            // adjustment can take up to a pixel off the width a text was measured to fit exactly, and the
            // last word would wrap. The geometry is placed on the adjusted rect. It is the glyph child's rect: its
            // own, or the size layout arranged it at (see PlaceChild).
            var rect = _glyphs.rectTransform.rect;
            float scale = CanvasScale;
            // Fresh from here on: a change made while it is laid out (a handler dirtying it) marks it stale again.
            _layoutStale = false;
            _laidOutSize = rect.size;
            _laidOutScale = scale;
            var input = TextLayoutInput.Default;
            input.Text = _text;
            input.RichText = _richText;
            input.Font = _font;
            input.Theme = EffectiveTheme;
            input.FontSizePx = _fontSize;
            input.WidthPx = rect.width;
            input.HeightPx = rect.height;
            // The anti-aliasing margin is one device pixel, expressed in canvas units.
            input.AntiAliasMarginPx = 1f / scale;
            input.WordWrap = _wordWrap;
            input.Alignment = _alignment;
            input.Overflow = _overflow;
            input.MaxLines = _maxLines;
            input.Direction = _direction;
            input.CharacterSpacingPx = _characterSpacing;
            input.WordSpacingPx = _wordSpacing;
            input.ParagraphSpacingPx = _paragraphSpacing;
            input.Bold = _bold;
            input.Italic = _italic;
            input.Underline = _underline;
            input.Strikethrough = _strikethrough;
            input.IcuData = _icuData;
            var context = new InlineContext(this, _fontSize, color);
            _layout.Generate(in input, in context);
        }
    }

#if TJ_TEXT_LAYOUT
    // With the UI Layout package present a TextBlock is a LayoutNode's content directly; see the measure above.
    public sealed partial class TextBlock : TimboJimbo.UI.Layout.ILayoutMeasurable
    {
    }
#endif
}
