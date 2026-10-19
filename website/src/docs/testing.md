# Testing

`RazorConsole.Testing` runs RazorConsole applications under test: it sends keyboard, mouse and raw terminal input, waits for the screen to reach a state, and lets you assert on the rendered cells. It works with any test framework; the examples use xUnit.

```bash
dotnet add package RazorConsole.Testing
```

The package has two hosts:

| | `HeadlessTerminal` | `PtyTerminal` |
| --- | --- | --- |
| Runs | One component, in the test process | A built console application, in a real pseudoterminal |
| Use it for | Component and integration tests | End-to-end tests of the whole application |
| Input | Keys, text, paste, mouse, raw VT input | The bytes a real terminal sends |
| Frames | Cells, focus and layout | Cells, cursor and terminal modes |
| Speed | Fast; no process, no terminal | Starts a process per test |

Start with `HeadlessTerminal`. Add `PtyTerminal` tests for what only a real terminal shows: startup and exit, Ctrl+C, resize signals, and how the application's output looks after a terminal has parsed it.

## HeadlessTerminal

`HeadlessTerminal.StartAsync<TComponent>` mounts a component in an in-memory terminal of a fixed size, renders the first frame and initializes focus. Register the services your component needs in the callback:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Testing;

public class CounterTests
{
    [Fact]
    public async Task Enter_IncrementsTheCount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await HeadlessTerminal.StartAsync<Counter>(
            width: 60,
            height: 10,
            services => services.AddSingleton<IClock, FakeClock>(),
            ct);

        Assert.Matches(@"Current count +0\b", terminal.CurrentFrame.ScreenText);

        await terminal.SendKeyAsync(ConsoleKey.Enter, '\r', cancellationToken: ct);

        await terminal.WaitUntilAsync(
            frame => Regex.IsMatch(frame.ScreenText, @"Current count +1\b"),
            description: "the count to reach 1",
            cancellationToken: ct);
    }
}
```

`Counter` is the component from the [README](https://github.com/RazorConsole/RazorConsole#basic-example). The test waits for the count on the "Current count" line, not for any `1` on the screen. The examples below run inside a test like this one: `terminal` is a started terminal, and `ct` is `TestContext.Current.CancellationToken`, which xUnit v3 cancels when the test is aborted.

Use `HeadlessTerminalOptions` to pass component parameters, or to capture the ANSI output (`CaptureAnsiOutput`, read through `AnsiOutput`). The terminal always renders with the WidgetLayout pipeline.

```csharp
await using var terminal = await HeadlessTerminal.StartAsync<Greeting>(new HeadlessTerminalOptions
{
    Width = 40,
    Height = 5,
    Parameters = new Dictionary<string, object?> { ["Name"] = "Ada" },
}, ct);
```

### Input

Every input method returns after the input has been handled and a frame has been captured.

- `SendKeyAsync`: one key press, sent to the focused element.
- `TypeAsync`: text, one key press per character. `\r` and `\n` are Enter, `\t` is Tab, `\b` and DEL are Backspace, upper-case letters add Shift, and other control characters are Ctrl+letter.
- `PasteAsync`: text the way a terminal pastes it. Each run of text reaches the focused input as a single `oninput` event.
- `SendMouseAsync` and `ClickAsync`: mouse events in zero-based cell coordinates.
- `SendTerminalInputAsync`: raw input such as `"\u001b[A"` (Up), decoded the way input from a real terminal is.
- `Resize`: changes the terminal size and captures a frame at the new size.

```csharp
await terminal.TypeAsync("hello", ct);
await terminal.ClickAsync(4, 2, ct);
await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Wheel, 10, 5, DeltaY: 3), ct);
terminal.Resize(100, 30);
```

`TypeAsync` and `PasteAsync` reject text that contains ESC; send escape sequences with `SendTerminalInputAsync`.

### Waiting and reading frames

The application can keep rendering after an input call returns, for example after a timer or an `await`. Wait for the state you expect instead of sleeping:

```csharp
var frame = await terminal.WaitForTextAsync("Saved 3 notes", cancellationToken: ct);
await terminal.WaitUntilAsync(f => f.FocusKey == "search", description: "focus on the search box", cancellationToken: ct);
```

Wait for something only the new state shows. A short text such as a single digit, or a label that is already on the screen, can match before anything has changed; use `WaitUntilAsync` with a predicate on the line, the cell or the focus instead.

Both return the first frame that matches. The default timeout is 2 seconds; pass `timeout` to change it.

A frame (`CurrentFrame`, or the last 50 distinct frames in `Frames`) is immutable:

- `ScreenText`, `GetLine(row)` and `ContainsText(text)` give the plain text;
- `frame[x, y]` gives a `TerminalCell` with its text and Spectre.Console `Style`;
- `FocusKey` is the focused element.

### Layout

Give an element a `data-vnode-hook` attribute to read its position and size:

```razor
<Panel data-vnode-hook="details">...</Panel>
```

```csharp
var details = terminal.GetLayout("details");
Assert.Equal(2, details.Top);
Assert.True(terminal.TryGetLayout("menu", out _));
```

`GetLayout` reads the current layout; `HeadlessTerminalFrame.GetLayout` reads the layout captured with that frame.

### Diagnostics

A wait that times out throws `TerminalTimeoutException`. Its message contains the terminal state: frame count, focus, the last mouse dispatch, any rendering error, and the most recent frames. The same text is in `Diagnostics`, and `DumpDiagnostics()` returns it at any time, which makes a good assertion message:

```csharp
Assert.True(terminal.CurrentFrame.ContainsText("Saved 3 notes"), terminal.DumpDiagnostics());
```

## PtyTerminal

`PtyTerminal` starts a built application in a real pseudoterminal: ConPTY on Windows, a POSIX PTY on Linux and macOS. It sends input as the bytes a terminal sends, and an emulator turns the application's output into frames.

Reference the application project from the test project, so its build output is copied next to the tests, then start it with `StartDotnetAsync`:

```csharp
[Fact]
[Trait("Category", "E2E")]
public async Task App_StartsAndQuitsOnCtrlC()
{
    Assert.SkipUnless(PtyTerminal.IsSupported, "Pseudoterminals are not supported here.");
    var ct = TestContext.Current.CancellationToken;

    await using var terminal = await PtyTerminal.StartDotnetAsync(
        Path.Combine(AppContext.BaseDirectory, "MyApp.dll"),
        width: 100,
        height: 30,
        arguments: ["--mock"],
        cancellationToken: ct);

    await terminal.WaitForTextAsync("MyApp is ready", cancellationToken: ct);

    await terminal.SendKeyAsync(ConsoleKey.C, '\x03', ConsoleModifiers.Control, ct);

    Assert.Equal(0, await terminal.WaitForExitAsync(cancellationToken: ct));
}
```

Here `MyApp is ready` stands for text the application shows only once it has started and can take input.

`StartDotnetAsync` runs the assembly with the `dotnet` host that runs the tests, in the assembly's directory. To start anything else, or to set the environment, working directory, terminal type or default timeout, use `PtyTerminal.StartAsync` with `PtyTerminalOptions`:

```csharp
var options = new PtyTerminalOptions
{
    FileName = "my-app",
    WorkingDirectory = workDir,
    Width = 120,
    Height = 40,
    DefaultTimeout = TimeSpan.FromSeconds(20),
};
options.Arguments.Add("--verbose");
options.Environment["NO_COLOR"] = "1";
options.Environment["MY_APP_TOKEN"] = null;

await using var terminal = await PtyTerminal.StartAsync(options, ct);
```

The application inherits the environment of the test process; `Environment` adds or replaces variables, and a `null` value removes one. Disposing the terminal kills the application and every process it started.

### Input and resize

`SendKeyAsync`, `TypeAsync`, `PasteAsync`, `ClickAsync`, `SendMouseAsync` and `SendTerminalInputAsync` work as in `HeadlessTerminal`, but they encode the input the way an xterm-compatible terminal does and return once the bytes are written, not when the application has handled them. Follow them with a wait.

- Ctrl+C is `SendKeyAsync(ConsoleKey.C, '\x03', ConsoleModifiers.Control)` or `TypeAsync("\x03")`. The terminal turns it into an interrupt, as a real one does.
- `PasteAsync` wraps the text in bracketed-paste markers when the application has turned bracketed paste on.
- `Resize` notifies the application the way a real terminal does (`SIGWINCH`, or a ConPTY resize event). The application redraws on its own schedule, so wait for content that depends on the new size.

By default the terminal answers queries such as the cursor position and device attributes. Set `RespondToTerminalQueries = false` to check that the application copes without answers.

### Waits

- `WaitForTextAsync(text)` and `WaitUntilAsync(predicate)` return the first frame that matches. If the output ends first because the application has exited, they throw `InvalidOperationException` instead of waiting for the timeout.
- `WaitForOutputIdleAsync(quietPeriod)` returns once no output has arrived for `quietPeriod`, or the output has ended. Idle only means quiet: it does not mean the application has finished rendering or handling input, and a slow application can pause longer and draw again. Prefer a wait on the expected content.
- `WaitForExitAsync()` returns the exit code. `HasExited` and `ExitCode` give the same information without waiting. On Linux and macOS, a process killed by a signal reports 128 plus the signal number.

The default timeout is `PtyTerminalOptions.DefaultTimeout`, 10 seconds. A timeout throws `TerminalTimeoutException`, whose message includes `DumpDiagnostics()`: the command, process and exit state, terminal modes, the most recent frames and the end of the raw output.

### Frames and raw output

A `PtyTerminalFrame` has the cells and text of every frame, plus the cursor (`CursorX`, `CursorY`, `CursorVisible`), the active screen buffer (`ActiveBuffer`), the mouse reporting modes (`MouseTracking`) and `BracketedPaste`. Assert on this state rather than on escape sequences:

```csharp
var frame = terminal.CurrentFrame;
Assert.Equal(TerminalScreenBuffer.Alternate, frame.ActiveBuffer);
Assert.NotEqual(TerminalMouseTracking.None, frame.MouseTracking);
```

A frame is captured after each chunk of output. While the application holds a synchronized update open, the frame is captured when the update ends, so a frame does not show a half-drawn screen. An update left open for more than a second is ended by the terminal.

`RawOutput` is all output read so far, decoded as UTF-8.

## Running terminals in parallel

Each `HeadlessTerminal` and `PtyTerminal` has its own services or process, terminal and frames, so tests that use them can run in parallel. One terminal instance is also safe to use from several threads: input calls and `Resize` run one at a time.

`PtyTerminal` tests start processes and take longer. Tag them, and run them as a separate step:

```csharp
[Trait("Category", "E2E")]
public class AppEndToEndTests { }
```

```bash
# Everything except end-to-end tests
dotnet test -- --filter-not-trait "Category=E2E"

# Only end-to-end tests
dotnet test -- --filter-trait "Category=E2E"
```

These are xUnit v3 options on Microsoft.Testing.Platform. With VSTest, use `--filter "Category=E2E"`.

## Platform notes

- **Windows**: needs Windows 10 version 1809 or later (ConPTY). ConPTY renders the application's output again before the test reads it, so `RawOutput` is ConPTY's output, not the application's own bytes, and sequences can be merged or reordered. Prefer assertions on frame state over byte assertions.
- **Linux**: the application gets the pseudoterminal as its controlling terminal, so Ctrl+C arrives as `SIGINT` and a resize as `SIGWINCH`. `TERM` is `xterm-256color` unless `PtyTerminalOptions.TerminalType` or `Environment` says otherwise.
- **macOS**: the application is started through `/bin/bash` and `/usr/bin/env`, which give it the pseudoterminal as its controlling terminal. The application still gets exactly the environment it was started with. `TERM` works as on Linux.

`PtyTerminal.IsSupported` tells whether the current system can run pseudoterminals; skip the test when it is `false`.
