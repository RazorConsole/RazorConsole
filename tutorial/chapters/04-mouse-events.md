# Chapter 4 · Mouse Events

Subscribe to mouse events from a Razor component, then combine them into hover, click, wheel, and drag interactions.

<!-- interactive-preview -->

## Learning objectives

By the end of this chapter, you will be able to:

- subscribe to mouse events with Razor event attributes;
- implement hover and click feedback;
- use terminal-cell coordinates from `MouseEventArgs`;
- combine down, move, and up events into a drag interaction; and
- enable mouse reporting in a native terminal application.

## 1. Enable terminal mouse reporting

Native terminals only send mouse input after an application enables mouse reporting. Update `Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.UseRazorConsole<MouseEvents>(configure: app =>
{
    app.Services.Configure<ConsoleAppOptions>(options =>
    {
        options.ConsoleLiveDisplayOptions.EnableMouseEvents = true;
    });
});

using var host = builder.Build();
await host.RunAsync();
```

The browser preview already enables mouse input. On Windows, macOS, and Linux, RazorConsole translates the
platform's terminal input into the same component events, so the component itself does not need platform checks.
Enabling mouse input also uses the alternate screen buffer so terminal mouse reporting is restored reliably on exit.

## 2. Subscribe to hover and click

Razor does not have one `@onhover` event. Hover is a state built from `@onmouseenter` and `@onmouseleave`:

```razor
<div @onmouseenter="HandleMouseEnter"
     @onmouseleave="HandleMouseLeave"
     @onclick="HandleClick">
    <Panel Width="28" Height="3"
           BorderColor="@(_hovered ? Color.Yellow : Color.Grey58)">
        <Markup Content="@(_hovered ? "Hover active — click me" : "Hover or click me")" />
    </Panel>
</div>

<Markup Content="@($"Clicks: {_clickCount}")" />

@code {
    private bool _hovered;
    private int _clickCount;

    private void HandleMouseEnter() => _hovered = true;
    private void HandleMouseLeave() => _hovered = false;
    private void HandleClick(MouseEventArgs _) => _clickCount++;
}
```

Attach the event attributes to the element whose laid-out terminal cells should be interactive. RazorConsole hit-tests
the pointer against that layout. A left-button down and up on the same element, without a drag between them, produces
one `@onclick` callback.

## 3. Read pointer coordinates

Mouse callbacks use the standard Blazor event argument types from `Microsoft.AspNetCore.Components.Web`:

| Event | Argument | Useful values |
| --- | --- | --- |
| `@onmousedown`, `@onmouseup`, `@onmousemove`, `@onclick` | `MouseEventArgs` | `Button`, `ClientX`, `ClientY`, `OffsetX`, `OffsetY`, modifier keys |
| `@onwheel` | `WheelEventArgs` | `DeltaY`, coordinates, modifier keys |
| `@onmouseenter`, `@onmouseleave` | `MouseEventArgs` or no argument | pointer entry and exit |

Unlike browser DOM coordinates, `ClientX` and `ClientY` are zero-based **terminal character-cell coordinates**, not
CSS pixels. `OffsetX` and `OffsetY` are relative to the event target's top-left cell. This makes pointer movement line
up directly with Widget Layout's integer `left` and `top` positions.

## 4. Build a draggable component

Place a card in a relatively positioned `Flex`, then subscribe to down, move, and up on the card:

```razor
<Flex Width="54" Height="8" position="relative">
    <div position="absolute"
         left="@_cardX"
         top="@_cardY"
         data-focus-key="mouse-drag-card"
         data-focusable="true"
         @onmousedown="HandleMouseDown"
         @onmousemove="HandleMouseMove"
         @onmouseup="HandleMouseUp"
         @onkeydown="HandleKeyDown">
        <Panel Width="18" Height="3" BorderColor="Color.DeepSkyBlue1">
            <Markup Content="@(_dragging ? "Dragging…" : "Drag me")" />
        </Panel>
    </div>
</Flex>

@code {
    private int _cardX = 2;
    private int _cardY = 1;
    private bool _dragging;
    private double _dragStartX;
    private double _dragStartY;
    private int _cardStartX;
    private int _cardStartY;

    private void HandleMouseDown(MouseEventArgs e)
    {
        if (e.Button != 0) return;

        _dragging = true;
        _dragStartX = e.ClientX;
        _dragStartY = e.ClientY;
        _cardStartX = _cardX;
        _cardStartY = _cardY;
    }

    private void HandleMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;

        _cardX = Math.Clamp(_cardStartX + (int)(e.ClientX - _dragStartX), 0, 36);
        _cardY = Math.Clamp(_cardStartY + (int)(e.ClientY - _dragStartY), 0, 5);
    }

    private void HandleMouseUp(MouseEventArgs e)
    {
        HandleMouseMove(e);
        _dragging = false;
    }
}
```

RazorConsole captures the element that handled the left-button down event. Its move and up handlers therefore keep
receiving events even when the pointer leaves the card during a drag. Clamp the resulting position to keep the card
inside its parent surface.

The `data-focus-key` and `data-focusable` attributes let a left click focus the card. Add `@onkeydown` and arrow-key
handlers, as the live example does, so the same interaction remains usable without a mouse.

## 5. Subscribe to the wheel

The wheel can be handled on a larger parent surface:

```razor
<div @onwheel="HandleWheel">
    <Border>
        <!-- interactive content -->
    </Border>
</div>

@code {
    private int _wheelSteps;

    private void HandleWheel(WheelEventArgs e)
        => _wheelSteps += Math.Sign(e.DeltaY);
}
```

Use the sign when one logical step is enough, or retain `DeltaY` when the magnitude matters. Wheel events are sent to
the nearest ancestor that subscribes to `@onwheel`.

## Complete source and repository run

The complete component used by the live preview is
`tutorial/Tutorial.Components/Chapters/MouseEvents.razor`.

Run that exact component from a RazorConsole checkout:

```shell
dotnet run --project tutorial/Tutorial.Runner -- --mouse-events
```

Move the mouse across the first card, click it, scroll over the lower canvas, and drag the blue card. Press
<kbd>Ctrl+C</kbd> to exit.

## Exercise

Add a right-click counter. Inspect `MouseEventArgs.Button`, where `0` is the left button, `1` is the middle button,
and `2` is the right button. Keep `@onclick` for the primary action and count the right button in `@onmousedown`.

## Common mouse-input errors

| Symptom | Fix |
| --- | --- |
| No native terminal mouse events arrive | Set `ConsoleLiveDisplayOptions.EnableMouseEvents = true`. |
| Hover remains active | Subscribe to both `@onmouseenter` and `@onmouseleave`. |
| Drag jumps when it starts | Save both the pointer start and card start positions on mouse down, then apply their delta. |
| The card escapes its surface | Clamp `left` and `top` to the surface size minus the card size. |
| Text selection occurs instead of interaction in the browser | Click inside the terminal preview first; browser input is forwarded through xterm. |

[← Chapter 3 · Text Input and Focus](/docs/tutorial/text-input-and-focus) · [Next: Chapter 5 · Widget Layout and Resize →](/docs/tutorial/widget-layout-and-resize)
