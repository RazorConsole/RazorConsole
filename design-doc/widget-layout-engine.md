# Widget Layout Engine Plan

## Implementation Progress

- [x] Phase 0 foundation started: added `RazorConsole.Core.Layout` geometry primitives, widget/layout-box scaffolding, and a `TerminalCanvas` that can emit a precomputed Spectre `IRenderable`.
- [x] Added deterministic unit coverage for layout geometry and canvas rendering/clipping.
- [x] Added a minimal `LayoutEngine` plus native `TextWidget` and vertical `StackWidget` to prove measure/arrange/paint flow end to end.
- [x] Added layout result enumeration so committed `LayoutBox` values can be projected to `VNodeLayoutInfo`, including resolved `ZIndex`.
- [x] Added native `RowWidget`, `PaddingWidget`, and `AlignWidget` with clipping-aware text painting and focused layout tests.
- [x] Added native `PanelWidget` with border styles, title rendering, padding, explicit sizing support, and focused layout tests.
- [x] Wired the widget pipeline into `ConsoleRenderer` behind `ConsoleAppOptions.RenderingPipeline = WidgetLayout`.
- [x] Added higher-level widget translation mappings for `Rows`, `Columns`, `Padder`, `Align`, and `Panel`.
- [x] Populated `IVNodeIdAccessor` and `IVNodeLayoutAccessor` from committed widget layout boxes.
- [x] Added a Gallery dogfood switch: pass `--widget-layout` or set `RAZORCONSOLE_RENDERING_PIPELINE=WidgetLayout` to opt into the widget layout pipeline.
- [x] Ran the Counter example through the widget pipeline by adding the same opt-in switch and translating `Markup`/`Figlet` leaf content.
- [x] Improved Counter visual parity by adding a `SpectreWidget` leaf adapter for `Figlet`, ignoring Razor indentation text nodes, and applying default `Columns` spacing.
- [x] Further aligned Counter widget output with legacy by honoring panel `Expand`, Spectre-like default panel padding, filtered whitespace children, and full-width `Figlet` measurement.
- [x] Connected widget viewport constraints to `TerminalMonitor` so layout uses the real console width/height and re-runs layout on terminal resize.
- [x] Started FileExplorer alignment: added the `--widget-layout`/`RAZORCONSOLE_RENDERING_PIPELINE` switch and translated `SpectreTable`/`Scrollable` into widget-compatible leaf/container output.
- [x] Added a minimal `ScrollableMinimal` example and a native `ScrollableWidget` side-scrollbar path; the minimal legacy and widget outputs now match (`One/Two/Three` with a `█/│` scrollbar).
- [x] Added a native `TableWidget` with column sizing, borders, header separators, cell child arrangement, row/cell layout boxes, and `WidgetTranslationContext` table wiring.
- [ ] Expand widget translation fallback coverage for richer HTML inline/block semantics and legacy Spectre leaf adapters.

## Problem

RazorConsole currently lowers the Blazor render tree to `VNode`, then immediately translates the `VNode` tree into Spectre.Console `IRenderable` objects. That makes Spectre.Console responsible for both composition/layout and terminal painting.

This creates two hard limits:

1. RazorConsole cannot reliably answer “where is this element?” for normal-flow nodes, because Spectre computes positions inside `IRenderable.Render(...)` without exposing a full box tree.
2. Adding a RazorConsole-owned layout engine is difficult because layout decisions are spread across translators and custom renderables that directly create Spectre layout primitives such as `Rows`, `Columns`, `Grid`, `Panel`, `Align`, and `Padder`.

The target architecture should keep Spectre.Console as the final terminal renderer only. Layout should be computed by RazorConsole before Spectre sees the output.

## Current Pipeline

```text
Blazor RenderBatch
  -> ConsoleRenderer builds/updates VNode tree
  -> TranslationContext / ITranslationMiddleware translates VNode to IRenderable
  -> ConsoleLiveDisplayContext diffs VNode and pushes IRenderable
  -> LiveDisplayCanvas writes DiffRenderable
  -> Spectre.Console renders segments/ANSI
```

Important current files:

- `Rendering/ConsoleRenderer.cs` owns `RenderSnapshot(Root, Renderable, AnimatedRenderables)` and translates the `VNode` root in `CreateSnapshot()`.
- `Rendering/Translation/Contexts/TranslationContext.cs` exposes a middleware pipeline that returns `IRenderable`.
- `Abstractions/Rendering/ITranslationMiddleware.cs` hard-codes the middleware contract as `VNode -> IRenderable`.
- `Rendering/Translation/Translators/*` frequently use Spectre layout components directly.
- `Rendering/LiveDisplayCanvas.cs` writes the final `IRenderable` through `DiffRenderable`.
- `Vdom/VNodeLayoutAccessor.cs` can expose layout metadata, but today there is no central normal-flow layout pass feeding it.

## Spectre Layout Coupling Hotspots

The strongest coupling is not the final `ansiConsole.Write(...)`; it is the translation layer:

- `RowsElementTranslator` returns Spectre `Rows`.
- `ColumnsElementTranslator` returns Spectre `Columns`.
- `GridElementTranslator` returns Spectre `Grid`.
- `PanelElementTranslator` returns Spectre `Panel` and delegates child composition to `Columns` through `VdomSpectreTranslator.ComposeChildContent(...)`.
- `PadderElementTranslator`, `AlignElementTranslator`, `ScrollableTranslator`, and `ViewHeightScrollableTranslator` also encode layout in Spectre renderables.
- `AbsolutePositionMiddleware` and `ModalTranslator` are already conceptually layout-aware, but they only collect overlays/metadata instead of participating in a unified layout tree.
- `FlexBoxRenderable` is a custom RazorConsole renderable, but it still implements Spectre `IRenderable` and performs layout during `Render(...)`, after the main snapshot is created.

This means geometry is discovered too late and in too many places.

## Proposed Architecture

Introduce a RazorConsole `Widget` layer between `VNode` and Spectre.Console.

```text
Blazor RenderBatch
  -> VNode tree
  -> Widget tree
  -> RazorConsole layout engine computes LayoutTree / boxes
  -> Painter converts boxes to a terminal canvas or final IRenderable
  -> Spectre.Console only writes the final rendered buffer
```

New conceptual pipeline:

```text
VNode
  -> WidgetTranslator: VNode -> WidgetNode
  -> LayoutEngine.Measure/Arrange: WidgetNode -> LayoutTree
  -> LayoutStore: updates IVNodeLayoutAccessor by VNode ID / hook
  -> SpectrePainter: LayoutTree -> IRenderable
```

The key change is that `IRenderable` becomes a terminal-stage implementation detail, not the primary internal UI representation.

## Core Types

### Geometry primitives

```csharp
public readonly record struct Size(int Width, int Height);
public readonly record struct Point(int X, int Y);
public readonly record struct Rect(int X, int Y, int Width, int Height);

public readonly record struct BoxConstraints(
    int MinWidth,
    int MaxWidth,
    int MinHeight,
    int MaxHeight);
```

All values are terminal cells/rows. Width must use Spectre's cell counting rules or a shared Unicode width service so layout matches terminal output.

### Widget contract

```csharp
public abstract class Widget
{
    public string VNodeId { get; init; } = string.Empty;
    public string? Key { get; init; }
    public IReadOnlyDictionary<string, string?> Attributes { get; init; } = EmptyAttributes;
    public IReadOnlyList<Widget> Children { get; init; } = [];

    public abstract Size Measure(LayoutContext context, BoxConstraints constraints);
    public abstract void Arrange(LayoutContext context, Rect bounds);
    public abstract void Paint(PaintContext context);
}
```

Recommended split for implementation:

- `Widget` is immutable input derived from `VNode`.
- `LayoutNode` stores mutable per-pass measurement/arrangement results.
- `PaintContext` writes into a cell canvas rather than directly returning Spectre `Segment`s from every widget.

### Layout tree output

```csharp
public sealed record LayoutBox(
    string VNodeId,
    Rect Bounds,
    int ZIndex,
    IReadOnlyList<LayoutBox> Children);
```

This is the authoritative source for `IVNodeLayoutAccessor`.

## Widget Families

Start with a small set that maps existing semantics:

| Widget | Replaces | Notes |
|---|---|---|
| `TextWidget` | `TextNodeTranslator`, `Markup` text | Handles wrapping, style spans, inline measurement. |
| `BlockWidget` | generic `div`, paragraph | Vertical normal-flow container. |
| `InlineWidget` | inline spans/text | Inline flow and line wrapping. |
| `StackWidget` | `Rows` / column-direction flex | Vertical stacking with gap/alignment. |
| `RowWidget` | `Columns` / row-direction flex | Horizontal layout with gap/alignment. |
| `GridWidget` | `Grid` | Owns track sizing; outputs child boxes. |
| `PanelWidget` | `Panel` | Measures border/header/padding itself, paints box characters. |
| `PaddingWidget` | `Padder` | Adjusts constraints and child bounds. |
| `AlignWidget` | `Align` | Positions child within given bounds. |
| `OverlayWidget` | absolute/modal overlay collection | Participates in z-index and top/left/right/bottom placement. |
| `ScrollableWidget` | `ScrollableRenderable` | Computes viewport, content bounds, scrollbar bounds. |
| `SpectreWidget` | charts, figlet, legacy adapters | Escape hatch for hard-to-port components. |

`SpectreWidget` is important for incremental migration. It wraps an existing `IRenderable` as a leaf widget: measure through `IRenderable.Measure(...)`, render to segments at paint time, and record only the leaf bounds. This keeps charts/syntax/figlet working while layout containers migrate first.

## Layout Algorithm

Use a two-pass model inspired by Flutter/SwiftUI/terminal UI frameworks:

1. **Build**: convert `VNode` to immutable `Widget` tree.
2. **Measure**: each widget receives constraints and returns desired size.
3. **Arrange**: parent assigns final `Rect` to children.
4. **Paint**: widgets draw into a `TerminalCanvas` using arranged bounds and z-index.
5. **Commit**: canvas becomes a Spectre `IRenderable` (or a custom `Renderable`) for final terminal output.

Pseudo-flow:

```csharp
var widgetRoot = widgetTranslator.Translate(vnodeRoot);
var layoutRoot = layoutEngine.Layout(widgetRoot, viewportConstraints);
layoutAccessor.UpdateSnapshot(vnodeRoot, layoutRoot.EnumerateLayoutInfos());
var renderable = spectrePainter.Paint(layoutRoot);
return new RenderSnapshot(vnodeRoot, renderable, animations, layoutRoot);
```

## Terminal Canvas

Introduce a cell canvas owned by RazorConsole:

```csharp
public sealed class TerminalCanvas
{
    public int Width { get; }
    public int Height { get; }

    public void Write(int x, int y, string text, Style? style = null);
    public void Fill(Rect rect, Rune rune, Style? style = null);
    public IRenderable ToRenderable();
}
```

The first implementation can expose `ToRenderable()` as a custom Spectre `Renderable` that yields pre-computed `Segment`s. Spectre then only writes rows/segments; it does not decide layout.

Later, `LiveDisplayCanvas` can diff `TerminalCanvas` frames directly and bypass part of `DiffRenderable`.

## Public Layout Introspection

`IVNodeLayoutAccessor` should be fed from the layout pass instead of translator side effects.

Recommended geometry API:

```csharp
public readonly record struct VNodeLayoutInfo(
    string VNodeId,
    int Top,
    int Left,
    int Width,
    int Height,
    int ZIndex,
    bool IsVisible,
    int RenderVersion);
```

Keep the existing nullable fields during migration if necessary, but once the widget engine owns layout, normal-flow nodes should have concrete `Top`, `Left`, `Width`, and `Height` whenever visible.

Snapshot semantics:

- Layout info is produced for each committed render snapshot.
- Hook lookup remains based on `data-vnode-hook`.
- Nodes clipped by scroll/viewport should still have logical bounds and a visible/clipped bounds variant if needed.

## Incremental Migration Plan

### Phase 0 — Introduce types without behavior changes

- Add `Layout/` namespace with geometry primitives, `Widget`, `LayoutNode`, `LayoutEngine`, and `TerminalCanvas`.
- Add `WidgetRenderSnapshot` fields behind internal APIs while keeping existing `RenderSnapshot.Renderable`.
- Add tests for `Rect`, constraints, and canvas-to-segment output.

### Phase 1 — Widget translator with legacy leaves

- Add `IWidgetTranslator` / `WidgetTranslationContext`.
- Translate every `VNode` to a widget tree.
- For unsupported elements, use `SpectreWidget` by delegating to the existing `TranslationContext`.
- Keep the existing `VNode -> IRenderable` path as default behind an option.

### Phase 2 — Own the major layout containers

Implement native widgets for:

1. `Rows` / `Columns`
2. `Padder`
3. `Align`
4. `Panel`
5. `FlexBox`
6. `Grid`
7. normal HTML block/inline flow

At this point RazorConsole can answer most element positions without render-time tracing.

### Phase 3 — Overlays, modals, scrolling

- Replace `CollectedOverlays` with `OverlayWidget` / z-layer arranging.
- Implement `ScrollableWidget` as a viewport/clipping widget with child logical bounds.
- Move `ScrollableLayoutCoordinator` responsibility into the layout engine.

### Phase 4 — Flip default pipeline

- Default `ConsoleRenderer.CreateSnapshot()` to `VNode -> Widget -> Layout -> CanvasRenderable`.
- Keep the old Spectre translator as compatibility fallback for one release.
- Add a feature flag such as `ConsoleAppOptions.RenderingPipeline = LegacySpectre | WidgetLayout` during transition.

### Phase 5 — Remove Spectre layout dependency

- Port remaining built-ins or wrap them as leaf-only `SpectreWidget` components.
- Disallow Spectre layout primitives inside layout containers.
- Keep Spectre for `Segment`, `Style`, color parsing, and final writing.

## Integration Points

### `ConsoleRenderer`

Current `CreateSnapshot()` should be split:

```text
CreateRenderableRoot(...)
  -> CreateViewModel(...)
  -> CreateSpectreRenderable(...)  // legacy
  -> CreateWidgetLayoutRenderable(...)  // new
```

The new path should clear animation/layout collections, build widget tree, run layout, update layout accessor, then create final `IRenderable` from the canvas.

### `ITranslationMiddleware`

Do not mutate the current interface in-place immediately. Add a parallel interface:

```csharp
public interface IWidgetTranslationMiddleware
{
    Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node);
}
```

This avoids breaking external custom translators. Later, publish a migration guide from `ITranslationMiddleware` to `IWidgetTranslationMiddleware`.

### `ConsoleLiveDisplayContext`

Initially it can keep accepting `ConsoleViewResult.Renderable`. The new renderable is simply a `CanvasRenderable`.

Longer term, add `ConsoleFrame` to `ConsoleViewResult`:

```csharp
public sealed record ConsoleFrame(int Width, int Height, IReadOnlyList<Cell> Cells);
```

Then live display can diff cells directly instead of diffing `VNode` then replacing whole renderables.

## Testing Strategy

Add deterministic layout tests that do not need a terminal:

- `RowsWidget` child bounds.
- `ColumnsWidget` child bounds.
- `PanelWidget` border/padding/header sizing.
- `PaddingWidget` constraint reduction and child offset.
- `AlignWidget` center/end placement.
- `FlexBoxWidget` wrap/gap/justify/align placement.
- `GridWidget` track sizing.
- `OverlayWidget` z-index ordering and top/left/right/bottom resolution.
- `ScrollableWidget` logical bounds vs visible bounds.
- `IVNodeLayoutAccessor` hook lookup after normal-flow layout.

Also add golden segment/canvas tests for painting.

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Text width mismatch with Spectre | Centralize cell width calculation and compare against Spectre `Segment.CellCount` in tests. |
| Large migration surface | Use `SpectreWidget` as a leaf fallback and migrate containers first. |
| External custom translators | Keep `ITranslationMiddleware` during transition; add new widget middleware. |
| Performance regression | Cache measurements by widget identity, constraints, and render version where safe. |
| Animation support | Keep `IAnimatedConsoleRenderable` initially; later expose widget-level animation invalidation. |
| Terminal resize | Re-run layout with new viewport constraints; layout accessor updates snapshot version. |

## Recommendation

Implement the widget engine as a parallel opt-in pipeline first, not as a replacement PR. The first useful milestone should be:

1. `VNode -> Widget` with `SpectreWidget` fallback.
2. Native `Rows`, `Columns`, `Padder`, `Align`, `Panel`, and `Text` widgets.
3. `TerminalCanvas -> IRenderable` final output.
4. `IVNodeLayoutAccessor` populated for all native widgets.
5. A gallery/debug page that displays the layout boxes for selected `data-vnode-hook` values.

This gives immediate value for element position introspection while keeping existing Spectre-based components functional during migration.
