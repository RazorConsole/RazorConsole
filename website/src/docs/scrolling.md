# Scrolling

Scrolling is a property of a container. Set `Overflow="OverflowMode.Scroll"` on a container and its content scrolls inside it. The layout engine owns the scroll position, so scrolling works with the mouse wheel, the keyboard, a draggable scrollbar and code, and it repaints without a Blazor render.

```razor
<Panel Title="Log" Border="BoxBorder.Rounded" Height="8" Overflow="OverflowMode.Scroll">
    @foreach (var line in _lines)
    {
        <Markup Content="@line" />
    }
</Panel>
```

The border and title stay fixed; only the content scrolls. The scrollbar is drawn on the right border by default.

## Which containers scroll

`Box`, `Panel`, `Padder`, `Border`, `Flex`, `Rows` and `Columns` take `Overflow`, together with `Scrollbar`, `Controller` and `StickToBottom`.

| Value                    | Behavior                                                         |
| ------------------------ | ---------------------------------------------------------------- |
| `OverflowMode.Visible`   | The container grows with its content. This is the default.       |
| `OverflowMode.Hidden`    | Content taller than the container is clipped and cannot scroll.  |
| `OverflowMode.Scroll`    | Content taller than the container is clipped and scrolls.        |

A scroll container needs a height to scroll in: set `Height`, or `FillHeight` inside a parent that has a bounded height. Without one, it grows to fit its content and there is nothing to scroll. Plain HTML elements scroll too, with the `overflow="scroll"` attribute.

`FlexBox` and `Grid` are backed by Spectre.Console and do not support `Overflow`; wrap them in a `Box` with `Overflow="OverflowMode.Scroll"`.

## Scrollbar

The `Scrollbar` parameter takes a `ScrollbarSettings`:

- `Placement`: `Border` (the default) draws the bar over the right border, `Inset` reserves a column inside the container, and `Outside` adds a column to the right of it. Without a right border, `Border` falls back to `Inset`. `Inset` and `Outside` reserve their column only while the content overflows.
- `Visibility`: `Auto` (the default) shows the bar only when the content overflows, `Always` and `Never` do what they say.
- Characters and colors: `TrackChar`, `ThumbChar`, `TrackColor`, `ThumbColor` and `MinThumbHeight`.

Clicking the track pages the content, and dragging the thumb scrolls it. Both are handled by the engine, with no event handlers in your component.

## Mouse wheel

The wheel scrolls the innermost scroll container under the pointer. When that container is at its limit, the scroll continues in the next container outward, like a browser. Set `OverscrollBehavior="OverscrollBehavior.Contain"` on a container to stop the chain there.

An `@onwheel` handler still runs first. Add `@onwheel:preventDefault` to suppress native scrolling for that element, or `@onwheel:stopPropagation` to keep the containers above it from scrolling.

## Keyboard

A scroll container is not a Tab stop by default, so adding `Overflow` does not change the Tab order of an existing layout. Set `Focusable="true"` to make it one. While it has focus, `Up`, `Down`, `PageUp`, `PageDown`, `Home` and `End` scroll it.

Without focusing the container, `PageUp`, `PageDown`, `Home` and `End` that the focused element does not handle scroll the nearest scroll container around it. The arrow keys are left to the focused element.

Moving focus scrolls the focused element into view. Tabbing to a button below the fold scrolls its container just enough to show it.

## Scrolling from code

Pass a `ScrollController` to a container to scroll it, and to read its position:

```razor
<Box Height="10" Overflow="OverflowMode.Scroll" Controller="_scroll" StickToBottom="true">
    @foreach (var line in _lines)
    {
        <Markup Content="@line" />
    }
</Box>

<TextButton Content="Top" OnClick="() => _scroll.ScrollToStartAsync()" />

@code {
    private readonly ScrollController _scroll = new();
}
```

| Member                          | Purpose                                                                          |
| ------------------------------- | -------------------------------------------------------------------------------- |
| `ScrollToAsync(top)`            | Scrolls to a row; the value is limited to the scrollable range.                  |
| `ScrollByAsync(delta)`          | Scrolls by a number of rows; negative values scroll up.                          |
| `ScrollToStartAsync()`, `ScrollToEndAsync()` | Scrolls to the first or last row.                                   |
| `ScrollIntoViewAsync(target)`   | Scrolls an element into view. `target` is its `data-vnode-hook`, `data-focus-key` or `@key`. |
| `Metrics`                       | The current `Top`, `ContentHeight` and `ViewportHeight`, plus `MaxTop`, `IsAtStart` and `IsAtEnd`. |
| `MetricsChanged`                | Raised when the position or the size changes. It can fire on any thread.         |

A controller drives one container at a time and does nothing until that container has rendered.

`ScrollTop` sets the offset from a parameter, and `@bind-ScrollTop` follows it. The value is applied when it changes, so a constant value never resets scrolling the user did.

### Stick to the bottom

`StickToBottom="true"` keeps the end of the content in view while it grows, like a log or a chat. Scrolling up releases it, and scrolling back to the end follows the content again.

## Scroll state and ids

Each scroll container has a scroll id. Pass `ScrollId` for state that must survive re-renders and reordering. Without it, the state follows the container's identity, which is not stable when siblings are inserted or reordered. Use `@key` or an explicit `ScrollId` for containers in lists.

## Moving from the older components

`Scrollable<TItem>` and `ViewHeightScrollable` are obsolete. They still work, and will be removed in a later major version.

| Before                                                  | After                                                              |
| ------------------------------------------------------- | ------------------------------------------------------------------ |
| `<ViewHeightScrollable LinesToRender="10">`             | `<Box Height="10" Overflow="OverflowMode.Scroll">`                 |
| `LinesToRender="0"` (fill)                              | `<Box FillHeight="true" Overflow="OverflowMode.Scroll">` in a parent with a bounded height |
| `IsScrollbarEmbedded="true"`                            | `Scrollbar` with `Placement = ScrollbarPlacement.Border`           |
| `IsScrollbarEmbedded="false"`                           | `Scrollbar` with `Placement = ScrollbarPlacement.Outside`          |
| `ScrollOffset`, `ScrollOffsetChanged`                   | `ScrollTop`, `ScrollTopChanged`                                    |
| `<Scrollable Items="..." PageSize="3">`                 | A scrolling container around all the items                         |

Long lists render every row. Windowed lists, such as a process table with thousands of rows, are better served by paging the data yourself until a virtualized list exists.
