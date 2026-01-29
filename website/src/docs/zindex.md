# Absolute Positioning & Z-Index

This document explains how absolute positioning and layering work in `RazorConsole` and how to create complex layouts using the `absolute` coordinate system.

---

## 1. Positioning Basics

By default, elements in `RazorConsole` follow the **Normal Flow** (stacked vertically or horizontally depending on the container).

When you apply `position="absolute"`, the element is:

- **Removed from the normal flow**: It no longer takes up space in the layout, and other elements behave as if it isn't there.
- **Placed on an Overlay Layer**: It is rendered on top of the background content.
- **Positioned relative to the Document Root (Canvas)** or its nearest positioned ancestor.

---

## 2. Coordinate System

You can control the position of an absolute element using four attributes: `top`, `bottom`, `left`, and `right`.

| Attribute    | Description                                              |
| ------------ | -------------------------------------------------------- |
| **`top`**    | Distance from the top edge of the document/ancestor.     |
| **`left`**   | Distance from the left edge of the document/ancestor.    |
| **`bottom`** | Distance from the bottom edge of the **total document**. |
| **`right`**  | Distance from the right edge of the **total document**.  |

### 2.1. Stretching

If you provide both `left` and `right` , the element will **stretch** to fill the specified range.
`top` and `bottom` vertical stretching doesn't work, because of the current render system, that does not allow to set height directly.

```razor
<div position="absolute" left="0" right="0" top="5">
    <Panel Title="I am full width!" />
</div>

```
> [!NOTE] Horizontal stretch will work correctly only with elements that allows to be expanded (like `<Panel Expand="true">`).

---

## 3. Hierarchical Positioning (Cumulative Offsets)

`RazorConsole` supports nested absolute positioning. If an `absolute` element is placed inside another `absolute` element, the child's `top` and `left` values are added to the parent's coordinates.

This is known as **Cumulative Offsets**. The formula for the final global position is:

### Example of nesting:

```razor
<div position="absolute" top="10" left="10">
    <Markup Content="I am at (10, 10)" />

    <div position="absolute" top="2" left="5">
        <Markup Content="I am at (12, 15)" />
    </div>
</div>

```

> [!NOTE] `bottom` and `right` always calculate their position relative to the **edges of the entire document (Canvas)**, regardless of nesting.

---

## 4. Z-Index and Layering

The `z-index` attribute determines the stacking order of elements that overlap.

- **Default value:** `0`.
- **Higher values:** Elements move "closer" to the user (rendered on top).
- **Lower/Negative values:** Elements move "further back".

```razor
<div position="absolute" top="2" left="2" z-index="10">
    <Panel Border="BoxBorder.Double" Foreground="Color.Red">Top Layer</Panel>
</div>

<div position="absolute" top="3" left="3" z-index="5">
    <Panel Foreground="Color.Blue">Bottom Layer</Panel>
</div>

```

In this example, the **Red Panel** will be rendered over the **Blue Panel** because it has a higher `z-index`, even though it starts earlier in the code.

---

## 5. Canvas Expansion

In `RazorConsole`, the "Document" (Canvas) is dynamic.

1. It starts with the size of the background content.
2. If an absolute element is placed outside these bounds (e.g., `top="50"` when the background only has 10 lines), the **Canvas automatically expands** with empty lines to accommodate the element.

---

## 6. Implementation Details

Absolute positioning is handled by the [`AbsolutePositionMiddleware`](https://github.com/LittleLittleCloud/RazorConsole/blob/main/src/RazorConsole.Core/Rendering/Translation/Translators/AbsolutePositionMiddleware.cs). It intercepts nodes with the `position="absolute"` attribute and moves them to a special `CollectedOverlays` list in the `TranslationContext`.

The final composition is performed by [`OverlayRenderable`](https://github.com/LittleLittleCloud/RazorConsole/blob/main/src/RazorConsole.Core/Renderables/OverlayRenderable.cs), which:

1. Renders the background.
2. Splits the output into a line-based `canvas`.
3. Handles `z-index` sorting before merging
4. Merges overlay segments into the canvas.

---

## 7. Unsupported Features

- **`position="relative"` or `position="fixed"`**: Currently, we have only `absolute` position or non-positioned elements in main flow.
- **Percentage units**: Only integer values (character units) are supported (e.g., `top="10%"` is not supported).
