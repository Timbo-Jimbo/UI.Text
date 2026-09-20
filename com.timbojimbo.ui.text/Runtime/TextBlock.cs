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
        private float _measuredForWidth = float.NaN;
        private float _preferredWidth;
        private float _preferredHeight;

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
            set { if (_alignment != value) { _alignment = value; SetVerticesDirty(); } }
        }

        public bool WordWrap
        {
            get => _wordWrap;
            set { if (_wordWrap != value) { _wordWrap = value; MarkTextChanged(); } }
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

        /// <summary>The layout behind this text: the parsed document, the laid-out text and its queries, as of the last rebuild.</summary>
        public TextLayout Layout => _layout;

        /// <summary>The parsed form of the current text (lines, runs, links and tokens, in source indices) as of the last rebuild.</summary>
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

        private void EnsureMeasures()
        {
            float width = rectTransform.rect.width;
            if (!_measuresDirty && Mathf.Approximately(width, _measuredForWidth))
                return;
            _measuresDirty = false;
            _measuredForWidth = width;
            var theme = EffectiveTheme;
            _preferredWidth = Measure(_text, _font, _fontSize, -1f, _wordWrap, _bold, _italic, _richText, theme).x;
            _preferredHeight = Measure(_text, _font, _fontSize, _wordWrap ? width : -1f, _wordWrap, _bold, _italic, _richText, theme).y;
        }

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
            SetVerticesDirty();
            SetLayoutDirty();
        }

        public override void SetVerticesDirty()
        {
            _measuresDirty = true;
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
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.pivot = new Vector2(0f, 1f);
            child.offsetMin = Vector2.zero;
            child.offsetMax = Vector2.zero;
            child.localScale = Vector3.one;
            child.SetSiblingIndex(siblingIndex);
            return child;
        }

        // ---- Queries ----

        /// <summary>
        /// Rectangles covering the source characters in [start, end), one per line, in this component's local
        /// space (canvas units, y up). Empty until the text has been laid out.
        /// </summary>
        public void GetCharacterRects(int start, int end, List<Rect> results)
        {
            results.Clear();
            if (_glyphs == null)
                return;
            _layout.GetSourceRangeRects(start, end, s_Rects);
            var rect = _glyphs.GetPixelAdjustedRect();
            float inverseScale = 1f / CanvasScale;
            for (int i = 0; i < s_Rects.Count; i++)
            {
                var r = s_Rects[i];
                results.Add(new Rect(rect.xMin + r.xMin * inverseScale, rect.yMax - r.yMax * inverseScale, r.width * inverseScale, r.height * inverseScale));
            }
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
            float scale = CanvasScale;
            var pointPx = new Vector2((local.x - rect.xMin) * scale, (rect.yMax - local.y) * scale);
            int link = _layout.LinkAt(pointPx);
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

        /// <summary>Regenerates the layout, hands the glyph geometry to the child renderer and places inline content.</summary>
        internal void RebuildGlyphs()
        {
            EnsureChildren();
            ClearGeometry();

            float scale = CanvasScale;
            if (Generate(scale))
                BuildGeometry(scale);
            _glyphs.Apply(s_Positions, s_Colors, s_Uv0, s_Uv1, s_GroupTriangles, s_Materials);

            // Inline prefabs are enabled after UGUI's rebuild loop, which forbids it while running.
            QueueInlineContent(scale);
        }

        private float CanvasScale
        {
            get
            {
                var canvas = this.canvas;
                return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            }
        }

        private bool Generate(float scale)
        {
            var rect = GetPixelAdjustedRect();
            var input = TextLayoutInput.Default;
            input.Text = _text;
            input.RichText = _richText;
            input.Font = _font;
            input.Theme = EffectiveTheme;
            input.FontSizePx = _fontSize * scale;
            input.WidthPx = rect.width * scale;
            input.HeightPx = rect.height * scale;
            input.PixelsPerUnit = scale;
            input.WordWrap = _wordWrap;
            input.Alignment = _alignment;
            input.Overflow = _overflow;
            input.MaxLines = _maxLines;
            input.Direction = _direction;
            input.CharacterSpacingPx = _characterSpacing * scale;
            input.WordSpacingPx = _wordSpacing * scale;
            input.ParagraphSpacingPx = _paragraphSpacing * scale;
            input.Bold = _bold;
            input.Italic = _italic;
            input.Underline = _underline;
            input.Strikethrough = _strikethrough;
            input.IcuData = _icuData;
            var context = new InlineContext(this, _fontSize, color);
            return _layout.Generate(in input, in context);
        }
    }
}
