# Layout, Box, and Flex Design Notes

## Goal

Define a clear, composable layout model for RazorConsole's native `WidgetLayout` pipeline, centered on two primitives:

- `Box`: the rectangle around one child, including margin, border, padding, explicit size, fill behavior, and future overflow/scrollbar behavior.
- `Flex`: the parent layout that arranges sibling widgets along a row or column axis.

The target mental model should feel close to browser layout where that helps, but remain deterministic and terminal-friendly. RazorConsole should not copy every CSS edge case, especially margin collapsing, implicit min-content rules, or complicated percentage sizing.

## Current Pipeline Context

In `WidgetLayout`, RazorConsole converts the `VNode` tree into widgets, runs measure/arrange, then paints into a terminal canvas.

```text
VNode tree
  -> WidgetTranslationContext
  -> Widget tree
  -> LayoutEngine.Measure/Arrange with viewport constraints
  -> LayoutBox tree + VNodeLayoutInfo snapshots
  -> TerminalCanvas / WidgetCanvasRenderable
  -> DiffRenderable / terminal output
```

Viewport size comes from `TerminalMonitor` through `ConsoleRenderer.CreateViewportConstraints()`:

```text
BoxConstraints(0, terminalWidth, 0, terminalHeight)
```

This means widgets should usually express layout intent through constraints rather than reading terminal dimensions directly in Razor components.

## Browser-Inspired Mental Model

A normal web `div` roughly behaves like:

```css
div {
  display: block;
  width: auto;   /* fills available inline space */
  height: auto;  /* fits content */
}
```

RazorConsole should keep the same broad intuition:

```text
default width/height: fit content unless a parent/layout rule says otherwise
fill width/height: opt in explicitly
siblings: parent layout decides distribution
```

The important distinction is that terminal layout has no browser reflow engine. Parent widgets must explicitly offer constraints, and children must measure deterministically within those constraints.

## Box Model

`Box` owns the rectangle around a single child:

```text
allocated bounds from parent
  margin       (outside spacing, not painted)
    border     (painted frame/title)
      padding  (inside spacing, not painted)
        child content
```

### Padding

Padding is inside the border and reduces the child content area.

```razor
<Box Border="BoxBorder.Rounded" Padding="new(1)">
    <Markup Content="Hello" />
</Box>
```

Conceptually:

```text
╭─────────╮
│         │
│ Hello   │
│         │
╰─────────╯
```

### Margin

Margin is supported by `Box`. It is outside the border and affects how the parent places the box. It is not painted.

API:

```razor
<Box Margin="new(1, 0, 1, 0)" Padding="new(1)" Border="BoxBorder.Rounded">
    ...
</Box>
```

Rules:

- Margin contributes to measured outer size.
- Border is painted inside the margin area.
- Child is arranged inside border + padding.
- Margins do not collapse.
- Parent `Flex Gap` and sibling margins both apply, so effective spacing may be `previous margin + gap + next margin`.

### Border and Title

`BoxWidget` owns border/title behavior. `PanelWidget`, `PaddingWidget`, and `RowWidget` were removed in favor of `BoxWidget` and `FlexWidget`.

Current border capabilities:

- margin via `data-margin`
- fill via `data-fill-width` and `data-fill-height`
- compatibility expansion via `data-expand`
- full border via `data-border`
- per-side borders via `data-border-top`, `data-border-right`, `data-border-bottom`, `data-border-left`
- title/header on the top border via `data-header`
- border style/color through parsed attributes

Per-side borders should draw only the requested sides. Corners are drawn when adjacent sides exist.

```text
top only:       ─────────
bottom only:    ═════════
full rounded:   ╭───────╮
                │       │
                ╰───────╯
```

## Flex Model

`Flex` owns sibling layout. It decides how children are placed relative to one another.

```razor
<Flex Direction="FlexDirection.Row" Gap="1" Align="FlexAlign.Center" Justify="FlexJustify.Center">
    ...children...
</Flex>
```

Main axis depends on direction:

```text
Direction=Row
  main axis  = width
  cross axis = height

Direction=Column
  main axis  = height
  cross axis = width
```

Current `FlexWidget` behavior:

- `Direction=Row` measures width as sum of child widths plus gaps, height as max child height.
- `Direction=Column` measures height as sum of child heights plus gaps, width as max child width.
- `FillWidth=true` expands to the width offered by the parent.
- `FillHeight=true` expands to the height offered by the parent.
- `Expand=true` remains a compatibility alias for main-axis expansion.
- Children with `data-fill-width="true"` share remaining main-axis space in rows.
- Children with `data-fill-height="true"` share remaining main-axis space in columns.
- `Align=Stretch` or cross-axis fill stretches children along the cross axis during arrange.
- `Justify` controls placement/distribution along the main axis.

## Expand Compatibility Semantics

The current API uses one `Expand` property in several places. This works, but it is confusing because the axis differs by widget.

```text
Box Expand=true
  current meaning: expand width

Flex Direction=Row Expand=true
  current meaning: expand width

Flex Direction=Column Expand=true
  current meaning: expand height

Child data-expand=true inside Flex
  current meaning: take remaining parent main-axis space
```

This is why layouts such as a full-screen centered box can feel surprising.

## Fill API

Prefer explicit axis names in new code:

```razor
<Box FillWidth="true" FillHeight="true">
    <Flex Direction="FlexDirection.Column"
          FillWidth="true"
          FillHeight="true"
          Align="FlexAlign.Center"
          Justify="FlexJustify.Center">
        ...
    </Flex>
</Box>
```

Current semantics:

```text
FillWidth  = measure width as constraints.MaxWidth
FillHeight = measure height as constraints.MaxHeight
```

These should mean "fill the space offered by my parent", not "fill the global terminal".

Nested fills should compose naturally:

```razor
<Box FillWidth="true" FillHeight="true">
    <Box FillWidth="true" FillHeight="true">
        ...
    </Box>
</Box>
```

The inner box fills the outer box's content area after margin/border/padding, not the full terminal.

Sibling fills should be distributed by the parent layout:

```razor
<Flex Direction="FlexDirection.Column" FillWidth="true" FillHeight="true">
    <Box FillWidth="true" FillHeight="true" />
    <Box FillWidth="true" FillHeight="true" />
</Flex>
```

The two boxes should share height because the parent column flex owns main-axis distribution.

## Full-Screen Centered Layout

With the fill API:

```razor
<Box Border="BoxBorder.Rounded"
     Padding="new(1)"
     FillWidth="true"
     FillHeight="true">
    <Flex Direction="FlexDirection.Column"
          FillWidth="true"
          FillHeight="true"
          Align="FlexAlign.Center"
          Justify="FlexJustify.Center">
        <Markup Content="Centered" />
    </Flex>
</Box>
```

The older compatibility shape is:

```razor
<Flex Direction="FlexDirection.Column" Expand="true">
    <Box Border="BoxBorder.Rounded" Padding="new(1)" Expand="true">
        <Flex Direction="FlexDirection.Column"
              Expand="true"
              Align="FlexAlign.Center"
              Justify="FlexJustify.Center">
            <Markup Content="Centered" />
        </Flex>
    </Box>
</Flex>
```

The parent flex provides vertical expansion. The box fills width. The inner column flex fills height inside the box and centers its children.

## Parent/Child Responsibilities

Use this rule to keep layout predictable:

```text
Box = my own rectangle and one child content area
Flex = my children's sibling arrangement
Parent = how much space children receive
Child = whether it wants to fill the space offered by parent
```

Examples:

- Put `Box` outside `Flex` when the box is the frame and the flex positions content inside it.
- Put `Box` inside `Flex` when the flex arranges multiple framed panels as siblings.
- Use `Gap` for simple sibling spacing.
- Use future `Margin` when spacing belongs to the box itself.

## Diagnostics

Use `LayoutDiagnostics` with `data-vnode-hook` to inspect computed widget dimensions.

```razor
<Flex Direction="FlexDirection.Column" Expand="true" data-vnode-hook="layout-root-flex">
    ...
</Flex>

<LayoutDiagnostics TargetHookKey="layout-root-flex" />
```

Component wrappers such as `Box` and `Flex` should forward unmatched attributes to their emitted `div` so diagnostic hooks can reach the VDOM node.

Diagnostics rendered inline consume layout space. For viewport/fill investigations, inspect the root node first, then account for any diagnostics panels that are also part of the layout.

## Terminal Rendering Notes

Full-height, full-width layouts exercise terminal edge cases:

- Emitting a final newline after the last terminal row can scroll row 0 off-screen.
- Writing to the bottom-right cell with terminal auto-wrap enabled can also scroll.
- `DiffRenderable` should avoid final `NEL()` and temporarily disable auto-wrap while painting full frames, then restore auto-wrap afterward.

These are rendering concerns, but they matter for layout examples because full-screen boxes often draw into the terminal's last row and last column.

## Open Decisions

1. Decide whether `Expand` should eventually become obsolete, or remain as a main-axis compatibility alias.
2. Decide whether `Box` should also own child alignment, replacing `AlignWidget` over time.
3. Decide how overflow and scrollbars compose with border, padding, and margin.
4. Add broader tests for nested fill, sibling fill distribution, margin measurement/arrange/paint offset, and full-screen terminal rendering edge cases as more components adopt the fill API.