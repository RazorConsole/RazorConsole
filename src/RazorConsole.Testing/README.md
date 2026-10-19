# RazorConsole.Testing

Test hosts for [RazorConsole](https://github.com/RazorConsole/RazorConsole) applications. Send keyboard, mouse and raw terminal input, wait for the screen to reach a state, and assert on the rendered cells. Works with any test framework.

- `HeadlessTerminal` mounts one component in the test process, at a fixed terminal size. Use it for component and integration tests.
- `PtyTerminal` runs a built console application in a real pseudoterminal: ConPTY on Windows (10 version 1809 or later), a POSIX PTY on Linux and macOS. Use it for end-to-end tests.

## Install

```bash
dotnet add package RazorConsole.Testing
```

The examples are xUnit v3 test bodies; `ct` is `TestContext.Current.CancellationToken`.

## HeadlessTerminal

Clicking the [README counter](https://github.com/RazorConsole/RazorConsole#basic-example) button with Enter:

```csharp
await using var terminal = await HeadlessTerminal.StartAsync<Counter>(width: 60, height: 10, cancellationToken: ct);

await terminal.SendKeyAsync(ConsoleKey.Enter, '\r', cancellationToken: ct);

await terminal.WaitUntilAsync(
    frame => Regex.IsMatch(frame.ScreenText, @"Current count +1\b"),
    cancellationToken: ct);
```

## PtyTerminal

```csharp
await using var terminal = await PtyTerminal.StartDotnetAsync(
    Path.Combine(AppContext.BaseDirectory, "MyApp.dll"), width: 100, height: 30, cancellationToken: ct);

await terminal.WaitForTextAsync("MyApp is ready", cancellationToken: ct);
await terminal.SendKeyAsync(ConsoleKey.C, '\x03', ConsoleModifiers.Control, ct);

Assert.Equal(0, await terminal.WaitForExitAsync(cancellationToken: ct));
```

## Documentation

See the [Testing guide](https://razorconsole.com/blog/testing/) for input, waits, frames, layout, diagnostics, parallel runs and platform notes.
