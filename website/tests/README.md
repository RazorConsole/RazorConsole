# Browser terminal input

The browser preview uses xterm `onData`, not parallel `onKey` dispatch. SGR
mouse reports, keys and pasted text share an ordered input queue with resize
events. A short idle flush resolves standalone Escape. Core's
`TerminalInputDispatcher` feeds the existing decoder, keyboard manager and mouse
manager; Razor components keep their normal `@onclick` / `@onwheel` handlers.
Mouse coordinates are terminal cells, not browser pixels.

## Automated checks

From `website`, run `npm ci` and `npm run test:terminal-input` for input ordering,
single delivery, disposal and recovery after a failed dispatch.

The .NET `TerminalInputStreamTests` use real Razor components and TestTerminal to
exercise raw SGR packets, key/mouse ordering, click synthesis, wheel arguments,
fragmentation, drag capture, resize hit testing and Escape flushing.

## Real browser fixture

```sh
dotnet publish src/RazorConsole.Website/RazorConsole.Website.csproj -c Release
cd website
npm ci
npm run dev:terminal-input
```

Open `http://127.0.0.1:5174/tests/terminal-input.html`. This uses actual xterm,
WASM and existing TextButton/TextInput Razor components, without the documentation
router. Click the button and verify one increment; type/paste and verify one copy;
scroll over the input and verify SGR reports in the log but no report text in the
input. The log is intentionally local and contains test input; do not enter secrets.

This is a manual browser fixture, not an automated browser end-to-end assertion.
SGR mouse encoding is enabled; legacy binary mouse reports are not supported by
this bridge. Native browser clipboard handling is left to xterm (no duplicate
clipboard injection). Disposing a preview removes listeners/timers and unregisters
its .NET renderer.
