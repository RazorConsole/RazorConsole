# FlexBoxRenderable

The `FlexBoxRenderable` is a custom `IRenderable` implementation that brings CSS-like flexbox layout semantics to Spectre.Console rendering. It supports configurable direction, wrapping, alignment, justification, and gap — giving RazorConsole authors a powerful one-dimensional layout primitive.

## Motivation

Existing layout renderables offer limited control:

- **`Rows`** — vertical-only stacking, no cross-axis alignment or justification.
- **`Columns`** — horizontal flow, but auto-wraps based on content width with no explicit control.
- **`Grid`** — fixed column count, no flexible sizing.
- **`BlockInlineRenderable`** — block/inline grouping, no alignment or distribution.

A flexbox renderable fills the gap by allowing authors to control:
1. **Direction** — horizontal (`Row`) or vertical (`Column`).
2. **Justification** — how items are distributed along the main axis (`Start`, `End`, `Center`, `SpaceBetween`, `SpaceAround`, `SpaceEvenly`).
3. **Alignment** — how items are aligned along the cross axis (`Start`, `End`, `Center`, `Stretch`).
4. **Wrapping** — whether items overflow or wrap to the next line.
5. **Gap** — uniform spacing between items on the main axis.

## Scope

This design covers:
- `FlexBoxRenderable` — the Spectre.Console `IRenderable` in `Renderables/`.
- `FlexBox.razor` — the Razor component in `Components/`.
- `FlexBoxElementTranslator.cs` — the translation middleware in `Rendering/Translation/Translators/`.
- Registration in `RazorConsoleServiceCollectionExtensions`.
- Unit tests in `RazorConsole.Tests/Renderables/`.

## Design

### Enums

```csharp
public enum FlexDirection  { Row, Column }
public enum FlexJustify    { Start, End, Center, SpaceBetween, SpaceAround, SpaceEvenly }
public enum FlexAlign      { Start, End, Center, Stretch }
public enum FlexWrap       { NoWrap, Wrap }
```

### FlexBoxRenderable

```
FlexBoxRenderable : IRenderable
├── IReadOnlyList<IRenderable> Items
├── FlexDirection  Direction   = Row
├── FlexJustify    Justify     = Start
├── FlexAlign      Align       = Start
├── FlexWrap       Wrap        = NoWrap
├── int            Gap         = 0
├── int?           Width       = null   (explicit constraint; falls back to maxWidth)
└── int?           Height      = null
```

### Layout algorithm (Row direction)

```
┌─────────────────────────────── maxWidth ───────────────────────────────┐
│  ┌──────┐  gap  ┌──────────┐  gap  ┌────┐   ←── distributed by Justify│
│  │ A    │       │ B        │       │ C  │                             │
│  │      │       │          │       │    │   ←── cross-axis by Align   │
│  └──────┘       └──────────┘       └────┘                             │
└───────────────────────────────────────────────────────────────────────┘
```

#### Measure

1. Measure every child item against `maxWidth` (Row) or `maxHeight` (Column).
2. **Row direction**:
   - `Min` = max of each child's `Min` (widest single item).
   - `Max` = sum of each child's `Max` + gaps.
3. **Column direction**:
   - `Min` = max of each child's `Min`.
   - `Max` = max of each child's `Max`.

#### Render — Row

1. **Partition items into flex lines** based on `Wrap`:
   - `NoWrap` → all items in one line.
   - `Wrap` → greedily fill lines up to `maxWidth`, accounting for gaps.
2. **For each flex line**:
   a. Measure each item; compute `totalContentWidth` (sum of item widths + gaps between).
   b. Compute `freeSpace = maxWidth − totalContentWidth`.
   c. Distribute `freeSpace` according to `Justify`:
      - `Start` → all free space on the right.
      - `End` → all free space on the left (leading padding).
      - `Center` → half on left, half on right.
      - `SpaceBetween` → evenly between items (none at edges).
      - `SpaceAround` → equal space around each item.
      - `SpaceEvenly` → equal space between items and at edges.
   d. Determine `lineHeight` = max height of items in this line.
   e. Render each item into a segment grid (cell buffer), applying cross-axis `Align`:
      - `Start` → item at top, pad below.
      - `End` → pad above, item at bottom.
      - `Center` → equal padding above/below.
      - `Stretch` → render with `options.Height = lineHeight`.
   f. Compose the line by interleaving item columns with spacing columns.
3. Emit lines separated by `Segment.LineBreak`.

#### Render — Column

Symmetric to Row but along the vertical axis:
1. Items are stacked vertically. Each item renders at full `maxWidth`.
2. `Justify` distributes vertical free space (blank lines) between items.
3. `Align` controls horizontal alignment within each row (padding left/right).
4. `Gap` inserts blank lines between items.

### Cell-buffer approach

Following the pattern in `OverlayRenderable`, each flex line is rendered via a cell buffer:

```
1. Render each item → List<SegmentLine>
2. Normalize heights (pad shorter items to lineHeight)
3. Build composite rows by writing item columns side-by-side into a buffer
4. Convert buffer back to Segments
```

This keeps the implementation simple and avoids complex segment interleaving.

### FlexBox.razor Component

```razor
@namespace RazorConsole.Components

<div class="flexbox"
     data-direction="@Direction.ToString().ToLowerInvariant()"
     data-justify="@Justify.ToString().ToLowerInvariant()"
     data-align="@Align.ToString().ToLowerInvariant()"
     data-wrap="@Wrap.ToString().ToLowerInvariant()"
     data-gap="@Gap.ToString()"
     data-width="@WidthAttr"
     data-height="@HeightAttr">
    @ChildContent
</div>
```

| Parameter   | Type             | Default  | Description |
|-------------|------------------|----------|-------------|
| `Direction` | `FlexDirection`  | `Row`    | Main axis direction. |
| `Justify`   | `FlexJustify`    | `Start`  | Main-axis distribution. |
| `Align`     | `FlexAlign`      | `Start`  | Cross-axis alignment. |
| `Wrap`      | `FlexWrap`       | `NoWrap` | Whether items wrap to new lines. |
| `Gap`       | `int`            | `0`      | Character gap between items. |
| `Width`     | `int?`           | `null`   | Explicit width constraint. |
| `Height`    | `int?`           | `null`   | Explicit height constraint. |
| `ChildContent` | `RenderFragment?` | —    | Flex items. |

### FlexBoxElementTranslator

Follows the standard middleware pattern:

```csharp
public sealed class FlexBoxElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node)) return next(node);
        // Parse data-* attributes → enums
        // Translate children recursively
        // Return new FlexBoxRenderable(children, direction, justify, align, wrap, gap, width, height)
    }
}
```

Matching condition: `node.Kind == Element` and `class == "flexbox"` (case-insensitive).

## Usage Examples

### Horizontal toolbar with centered items

```razor
<FlexBox Direction="FlexDirection.Row" Justify="FlexJustify.Center" Gap="2">
    <TextButton Content="Open" />
    <TextButton Content="Save" />
    <TextButton Content="Close" />
</FlexBox>
```

```
                         [Open]  [Save]  [Close]
```

### Vertical stack with space-between

```razor
<FlexBox Direction="FlexDirection.Column" Justify="FlexJustify.SpaceBetween" Height="10">
    <Markup Content="Top" />
    <Markup Content="Middle" />
    <Markup Content="Bottom" />
</FlexBox>
```

```
Top



Middle



Bottom
```

### Wrapping horizontal layout

```razor
<FlexBox Direction="FlexDirection.Row" Wrap="FlexWrap.Wrap" Gap="1">
    @foreach (var tag in Tags)
    {
        <Markup Content="@($"[blue]{tag}[/]")" />
    }
</FlexBox>
```

```
tag1 tag2 tag3 tag4 tag5
tag6 tag7 tag8
```

## Testing Strategy

Tests in `FlexBoxRenderableTests.cs` will cover:

1. **Construction** — null items → `ArgumentNullException`, empty items → no crash.
2. **Measure** — correct min/max for row and column directions.
3. **Row rendering** — items rendered side-by-side, gaps applied.
4. **Column rendering** — items stacked vertically, gaps applied.
5. **Justification** — verify padding for each `FlexJustify` variant.
6. **Alignment** — verify cross-axis padding for each `FlexAlign` variant.
7. **Wrapping** — items that exceed `maxWidth` wrap to the next line.
8. **Edge cases** — single item, zero gap, items wider than maxWidth.

## See Also

- [BlockInlineRenderable](block-inline-renderable.md) — block/inline flow model.
- [Built-in Components](builtin-components.md) — full component catalog.
- [Rendering Process](rendering-process.md) — translation pipeline details.
