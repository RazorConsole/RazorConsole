# Layout Introspection Plan

## Goal

Expose runtime layout information for render-tree nodes/components so callers can query:

- `left`
- `top`
- `width`
- `height`
- `z` (z-index)

The feature should align with the existing hook-based lookup pattern (`data-vnode-hook`) used by `IVNodeIdAccessor`.

## Current State

- `IVNodeIdAccessor` can map `hookKey -> vnodeId` for the latest snapshot.
- Absolute/modal translators already compute position and z-index metadata during translation.
- The metadata is currently used only for rendering overlays and is not exposed via public API.

## Implementation Phases

### Phase 1 (this implementation start)

Deliver a public accessor for layout metadata captured from overlay-capable nodes (`position=absolute`, `modal`):

1. Add `IVNodeLayoutAccessor` + `VNodeLayoutAccessor` in `RazorConsole.Core.Vdom`.
2. Add `VNodeLayoutInfo` to represent layout box data.
3. Extend translation context with a per-snapshot layout collection.
4. Capture layout metadata in:
   - `AbsolutePositionMiddleware`
   - `ModalTranslator`
5. Wire snapshot updates from `ConsoleRenderer` into `VNodeLayoutAccessor`.
6. Register new services in DI.
7. Add renderer tests for lookup and update behavior.

Scope/limitations in Phase 1:

- Supports nodes that pass through overlay translators.
- `width`/`height` are best-effort from explicit node attributes when present.
- Non-overlay normal flow containers are not fully instrumented yet.

### Phase 2

Add structured instrumentation for common layout renderables (`rows`, `columns`, `flex`, `grid`, `padder`, `align`) so non-overlay nodes also receive computed `left/top/width/height`.

#### Current prototype status

- A first FlexBox prototype is in place to validate render-time child box capture.
- Flex child layout boxes are emitted after actual render and merged into `VNodeLayoutAccessor`.
- This confirms the feasibility of render-time tracing for normal-flow composition while staying on Spectre.

### Phase 3

Add richer diagnostics hooks:

- optional render version/timestamp
- optional parent/stack context
- optional debug dumps for snapshot-time geometry

## API Shape

```csharp
public interface IVNodeLayoutAccessor
{
    bool TryGetLayoutByVNodeId(string vnodeId, out VNodeLayoutInfo layout);
    VNodeLayoutInfo? GetLayoutByVNodeIdOrDefault(string vnodeId);

    bool TryGetLayoutByHookKey(string hookKey, out VNodeLayoutInfo layout);
    VNodeLayoutInfo? GetLayoutByHookKeyOrDefault(string hookKey);
}
```

## Threading/Snapshot Semantics

- Layout data is snapshot-scoped.
- Accessor stores immutable dictionary snapshots and atomically swaps references under lock.
- Invalid/empty render snapshots clear previously cached layout data.

## Validation

- Add tests to verify:
  - lookup by hook after mount
  - lookup by vnode id after mount
  - map updates when hook changes
  - old mappings are removed on update
