### Keyboard Events

Interactive experiences rely on keyboard input. RazorConsole exposes the familiar Blazor event model so you can respond to keystrokes just like you would on the web.

#### Custom key handling

```razor
<div @onkeydown="HandleKeyPress">
    <Markup Content="@message" />
</div>

@code {
    private string message = "Press any key...";

    private void HandleKeyPress(KeyboardEventArgs e)
    {
        message = $"You pressed: {e.Key}";
        StateHasChanged();
    }
}
```

#### Supported events

- `@onkeydown` — fires when a key is pressed.
- `@onkeyup` — fires when a key is released.
- `@onkeypress` — fires when a key is pressed and released.

#### Scrolling keys

A scroll container (`Overflow="OverflowMode.Scroll"` or `ScrollView`) handles `Up`, `Down`, `PageUp`, `PageDown`, `Home` and `End` itself while it has focus; set `Focusable="true"` to make it a Tab stop. `PageUp`, `PageDown`, `Home` and `End` that the focused element does not handle scroll the nearest scroll container around it. See [Scrolling](/blog/scrolling/).
