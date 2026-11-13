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

#### Async handlers keep input flowing

RazorConsole dispatches events the way Blazor does in a browser: each key or mouse event goes to the renderer in arrival order, and the next one is handled as soon as the previous handler has *started* (its code up to the first real `await` has run). Input never waits for an `async` handler's `Task`. A Cancel button therefore works while a click handler awaits long-running work:

```razor
<TextButton Content="Start" OnClick="StartAsync" />
<TextButton Content="Cancel" OnClick="@(() => _cts?.Cancel())" />
<Markup Content="@_status" />

@code {
    private CancellationTokenSource? _cts;
    private string _status = "idle";

    private async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _status = "working...";
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token);
            _status = "done";
        }
        catch (OperationCanceledException)
        {
            _status = "cancelled";
        }
    }
}
```

Things to keep in mind:

- **Handlers can overlap.** A second click (or a key) can run while an earlier handler is still awaiting, so guard state that two runs must not share, for example by disabling a button while its work runs.
- **A handler that blocks the thread still blocks input.** `Thread.Sleep`, a CPU-bound loop, or `.Result` on a task freezes the app, as it freezes a browser tab. Await instead, or move the work to `Task.Run`.
- **An exception thrown after an `await`** is reported once, like any rendering error (it shows up in the app's logs and in `razorconsole errors`), and does not stop input.
- **Default key actions are decided when the handler yields.** RazorConsole moves focus on Tab and activates the focused element on Enter unless a keydown handler marks the key as handled. Like `preventDefault` in a browser, that decision must be made before the handler's first real `await`; marking the key handled later is too late. `TextArea.OnPreviewKey` follows the same rule: only a returned task that is already complete can consume Tab.
- **Built-in inputs keep their callbacks in order.** `TextInput` delivers `ValueChanged`, `OnInput` and `OnSubmit` one at a time in typing order, even when they are `async`, and `TextArea` clears its text before it awaits `OnSubmit`, so keys typed during a slow submit are kept.

To restore the earlier behavior, where every handler finishes before the next input is handled, set `SerializeEventHandlers`:

```csharp
builder.UseRazorConsole<App>(configure: config =>
{
    config.Services.Configure<ConsoleAppOptions>(options =>
        options.SerializeEventHandlers = true);
});
```

With it on, an `async` handler blocks every later key and click until its `Task` completes, so a Cancel button only runs after the work it was meant to cancel.
