# Chapter 6 · Scrolling with Overflow

Make any container scroll, choose a scrollbar style, and scroll with the wheel, the keyboard, and code.

<!-- interactive-preview -->

## Learning objectives

- make a container scroll with `Overflow`;
- understand why a scroll container needs a bounded height;
- choose a scrollbar placement and style;
- scroll with the wheel, the keyboard, and a `ScrollController`; and
- keep a growing log pinned to the bottom.

## 1. Overflow is a container property

Scrolling is not a separate component. It is a property of the container that holds the content. Start with a `Panel`
that holds more lines than it can show:

```razor
<Panel Title="Log" Border="BoxBorder.Rounded">
    @foreach (var line in lines)
    {
        <Markup Content="@line" />
    }
</Panel>
```

The panel grows to fit all 50 lines. Give it a height and turn scrolling on:

```razor
@using RazorConsole.Core.Layout

<Panel Title="Log" Border="BoxBorder.Rounded" Height="8" Overflow="OverflowMode.Scroll">
    @foreach (var line in lines)
    {
        <Markup Content="@line" />
    }
</Panel>
```

The border and title stay where they are and only the content scrolls. `Overflow` has three modes:

| Value                  | Behavior                                                        |
| ---------------------- | --------------------------------------------------------------- |
| `OverflowMode.Visible` | The container grows with its content. This is the default.      |
| `OverflowMode.Hidden`  | Content taller than the container is clipped and cannot scroll. |
| `OverflowMode.Scroll`  | Content taller than the container is clipped and scrolls.       |

`Box`, `Panel`, `Padder`, `Border`, `Flex`, `Rows`, and `Columns` accept `Overflow`, and plain elements take the
`overflow="scroll"` attribute. `FlexBox` and `Grid` are backed by Spectre.Console and do not support it yet; wrap them in
a `Box` with `Overflow`.

## 2. A scroll container needs a bounded height

This is the most common mistake. A container can only scroll when something limits its height. Without a limit, it
grows to fit its content, and there is nothing to scroll. There are two ways to bound it:

```razor
@* A fixed height *@
<Box Height="8" Overflow="OverflowMode.Scroll">...</Box>

@* The height the parent gives it, which must itself be bounded *@
<Rows Height="20">
    <Markup Content="Header" />
    <Box FillHeight="true" Overflow="OverflowMode.Scroll">...</Box>
</Rows>
```

This follows the model from Chapter 5: the parent measures its children and hands them cells. A scroll container is a
child that measures its content without a height limit and then shows a window of it.

## 3. Style the scrollbar

The scrollbar is drawn for you whenever the content overflows. The `Scrollbar` parameter takes a `ScrollbarSettings`.

`Placement` decides where the bar goes:

```razor
<Panel Height="8" Overflow="OverflowMode.Scroll"
       Scrollbar="@(new ScrollbarSettings(placement: ScrollbarPlacement.Outside))">...</Panel>
```

- `Inset` (the default) reserves a column inside the container, so the bar sits between the content and the border.
- `Outside` adds a column to the right of the container's border.

Both reserve their column only while the content overflows, so a container that fits keeps its full width.

`Visibility` decides when it shows: `Auto` (the default) only while the content overflows, `Always`, or `Never`.

You can also change the characters and colors:

```razor
@using RazorConsole.Core.Rendering

<Panel Height="8" Overflow="OverflowMode.Scroll"
       Scrollbar="@(new ScrollbarSettings(
           trackChar: '┆', thumbChar: '▐',
           trackColor: Color.Grey35, thumbColor: Color.Aqua,
           placement: ScrollbarPlacement.Outside,
           visibility: ScrollbarVisibility.Always))">
    ...
</Panel>
```

## 4. Scroll with the mouse and keyboard

You write no handlers for any of this. The layout engine does it.

- **Wheel.** Roll the wheel over a scroll container to scroll it. Click the track to page, and drag the thumb to scroll
  continuously.
- **Nested containers.** The wheel scrolls the innermost container under the pointer. When it reaches its end, the
  wheel continues in the container around it, like a browser. Stop that on the inner container with
  `OverscrollBehavior="OverscrollBehavior.Contain"`.
- **Keyboard.** A scroll container is not a Tab stop by default, so adding `Overflow` does not change the Tab order of an
  existing layout. Set `Focusable="true"` to make it one. While it has focus, `Up`, `Down`, `PageUp`, `PageDown`,
  `Home`, and `End` scroll it.
- **Focus.** Tab to a button that sits below the fold inside a scroll container and the container scrolls just enough
  to show it.

```razor
<Panel Height="8" Overflow="OverflowMode.Scroll" Focusable="true"
       OverscrollBehavior="OverscrollBehavior.Contain">
    ...
    <TextButton Content="Save" OnClick="Save" />
</Panel>
```

## 5. Scroll from code

Pass a `ScrollController` to the `Controller` parameter to scroll from code and to read the position:

```razor
<Panel Height="8" Overflow="OverflowMode.Scroll" Controller="_scroll">
    @foreach (var line in lines)
    {
        <Markup Content="@line" />
    }
</Panel>

<Columns>
    <TextButton Content="Top" OnClick="() => _scroll.ScrollToStartAsync()" />
    <TextButton Content="Bottom" OnClick="() => _scroll.ScrollToEndAsync()" />
</Columns>
<Markup Content="@($"{_scroll.Metrics.Top} of {_scroll.Metrics.MaxTop}")" />

@code {
    private readonly ScrollController _scroll = new();
}
```

`Metrics` holds `Top`, `ContentHeight`, and `ViewportHeight`, plus `MaxTop`, `IsAtStart`, and `IsAtEnd`. The
`MetricsChanged` event fires when any of them changes, so call `StateHasChanged` from it to keep a status line current.
`ScrollToAsync`, `ScrollByAsync`, and `ScrollIntoViewAsync` cover the other cases.

When you only need the offset, `@bind-ScrollTop` is simpler:

```razor
<Panel Height="8" Overflow="OverflowMode.Scroll" @bind-ScrollTop="_top">...</Panel>
```

### Keep a log pinned to the bottom

`StickToBottom="true"` keeps the end of the content in view while it grows, like a log or a chat. Scroll up and it stops
following; scroll back to the end and it follows again:

```razor
<Panel Title="Log" Height="8" Overflow="OverflowMode.Scroll" StickToBottom="true">
    @foreach (var line in log)
    {
        <Markup Content="@line" />
    }
</Panel>
```

## Callout: `@onwheel:preventDefault`

Chapter 4 handled the wheel with `@onwheel`. Your handler still runs first. `@onwheel:preventDefault` on an element above a
scroll container now also stops that container's built-in scrolling, and `@onwheel:stopPropagation` keeps the containers
above the handler from scrolling. Before scrolling became part of layout, `preventDefault` had no effect on the wheel, so
remove it from handlers that should let the container scroll.

## Run this chapter locally

```shell
dotnet run --project tutorial/Tutorial.Runner -- --scrolling
```

Roll the wheel over the log. It is the first focusable element, so it has focus when the app starts: use the arrow keys, <kbd>PageUp</kbd>, <kbd>PageDown</kbd>, <kbd>Home</kbd>, and <kbd>End</kbd>, press <kbd>Tab</kbd> to move on to the buttons, and try them.

## Exercise

Add a second container next to the log, nest a small `Panel` with `Overflow` inside it, and compare how the wheel
behaves at the end of the inner container with and without `OverscrollBehavior.Contain`.

[← Chapter 5 · Widget Layout and Resize](/docs/tutorial/widget-layout-and-resize) · [Next: Chapter 7 · Routing →](/docs/tutorial/routing)
