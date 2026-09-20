namespace TimboJimbo.UI.Text.Bridge
{
    /// <summary>Vertical font metrics at a size, in pixels, relative to the baseline; values below it are negative.</summary>
    public struct AtgFontMetrics
    {
        public float LineHeight;
        public float Ascent;
        public float Descent;
        public float UnderlineOffset;
        public float UnderlineThickness;
        public float StrikethroughOffset;
        public float StrikethroughThickness;
    }
}
