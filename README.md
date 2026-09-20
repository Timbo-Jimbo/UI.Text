# Timbo Jimbo - UI Text

UGUI text rendered by Unity's Advanced Text Generator (ATG), the engine behind UI Toolkit's advanced text: HarfBuzz shaping, ICU line breaking and bidi, OS font fallback and colour emoji. No native plugins, no bundled fonts. Every script the OS ships a font for renders out of the box.

Requires Unity 6000.5 or newer, mobile or desktop.

## How it works

The generator's managed API is internal to Unity. The small `UnityEngine.TextCore.Tools` assembly in `Runtime/Bridge` borrows a name the TextCore module lists in its `InternalsVisibleTo` attributes, and wraps the generator behind a public API. Nothing else in the package touches Unity internals, so a Unity release that moves things only ever breaks the bridge. This is unsupported by Unity; expect small patches per Unity version. `Tests/BridgeAssumptionTests` pins down the engine behaviours the package depends on, so a change shows up there first.

Above the bridge, `TextLayout` does the work without knowing about GameObjects: it parses the source, builds the text the engine lays out, generates, and answers questions about the result. `TextBlock` is the UGUI component that feeds it a rect and turns the result into a mesh and prefabs.

## Usage

Add **Timbo Jimbo / UI / Text Block** to a `RectTransform` under a Canvas and type into Text.

**Fonts.** Every font reference is a `FontStack` (Create > Timbo Jimbo > UI > Font Stack): an ordered list of installed OS families, TTF/OTF files from the project, or dynamic font assets. The first source that resolves is the primary font, so a stack of Cascadia Mono, Menlo, DejaVu Sans Mono and Courier New picks the first one the device has; the rest fill in missing glyphs. With **OS Fallbacks** on, the fonts the OS names for missing glyphs come after the stack, which is how emoji and other scripts render from your own Latin font; off makes the stack self-contained. Leave a TextBlock's Font empty for the project default (a `TextBlockSettings` asset in a Resources folder), and without one the OS default font, which is simply a stack with no sources. Static (pre-baked) font assets are not supported by the generator.

**Bold and italic.** An OS family is probed for its Bold, Italic and Bold Italic faces and a font file can name its variant files, so bold text uses a real face wherever one exists; the OS default font gets its real faces too. Without one the engine synthesises bold by thickening the glyphs, and each source's Synthetic Bold slider (0 to 1, Unity's default 0.75) sets how much. Italic without a real face is a shear.

**Layout.** `TextBlock` is an `ILayoutElement`, so ContentSizeFitter and layout groups size it. `TextBlock.Measure` measures text without a component, and `TextLayout` can lay text out without one. Overflow behaves like TextMeshPro's: wrap and spill, ellipsis, or truncate, with an optional max line count.

**Markup.** With Rich Text on, a small Markdown dialect applies: `**bold**`, `*italic*`, `__underline__`, `~~strike~~`, `` `code` ``, `[label](url)`, bare URLs, `\` escapes, `#` headings, `-`/`1.` lists, `>` quotes and ``` fenced code blocks. A newline is a line break. No HTML, images or tables, so it is safe for user-generated text. The parser records only source indices; `LayoutText` builds what the engine lays out and keeps the mapping both ways, so a caret or a selection can move between the source and the layout. A `TextTheme` asset sets heading sizes, link and quote colours, the code font stack and the inline handlers.

**Inline content.** `InlineHandler` assets on a theme extend the markup with a trigger (`:emote:`, `@name`), a placement and a prefab. `PrefabInlineHandler` needs no code; subclass `InlineHandler` when the size or the acceptance depends on the token, as an emote set that declines unknown names or a mention chip measured to its text does. `Fragments` handlers style the text and put the prefab behind each line it spans; `Block` handlers put one prefab across the full width of every line; `Atomic` handlers replace the token with an unbreakable box the prefab sits over, sized in em and centred on the text unless the request says how much of it sits above the baseline. A handler can ask for layout padding: the text either side moves over, as CSS padding on an inline element does, and a span that wraps keeps it at its start and end only. Vertical padding is the prefab's business and is drawn over the line gap. Prefab roots implement `IInlineContent`. Code spans and blocks get a `Box` background from the UI package automatically.

**Links** raise `OnLinkClicked` with the href. `ITextBlockGlyphModifier` components on the same object can move or recolour glyphs as the mesh is built.

## Samples

**Chat** (Package Manager > Samples): a message feed with a theme, an emote set for `:smile:`, a mention chip for `@name` with a nested TextBlock, a monospace font stack for code and a scene.

## Known limits

- Flags render as letters on Windows because its emoji font has no flag glyphs; supply an emoji font in the stack for consistency.
- A padded inline span longer than a whole line (a long URL in backticks) has its leading padding wrapped onto a line of its own, which shows as an empty line before it.
- No outline or shadow effects; a second TextBlock offset behind the first makes a drop shadow.
- No selection, input field or hover state yet.
