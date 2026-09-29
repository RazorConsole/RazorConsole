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

## Quick Start tutorial browser check

### Built-site navigation regression

`tutorial_navigation.py` uses pinned Python Playwright and real Chromium against the **built**
site. It imports the emitted Tutorial browser module and invokes its client loader (valid chapter,
missing-chapter redirect, and 404), then clicks the homepage Docs, Quick Start, and FAQ entries,
checks actual Hello World keyboard input and restart, switches chapters, and exercises back/forward.
A document sentinel and request checks reject
full-page reloads masquerading as client navigation. The missing-chapter redirect must retain the
deployment base. CI runs it against both `/` preview and `/RazorConsole/` production builds.
Browser errors are logged with stacks for diagnosis; reference errors and errors identifying the
Tutorial/route module explicitly fail the test. The module invocation, DOM assertions, and no-reload
checks also fail independently of that diagnostic filter. This is not a blanket certification of
every WASM/xterm lifecycle case; use the additional terminal interaction checks below for that surface.

After building the website, run from `website`:

```sh
python -m pip install -r tests/requirements.txt
python -m playwright install chromium
python tests/tutorial_navigation.py
```

Set `VITE_BASE` to match the build. `WEBSITE_BUILD_DIR` can point to a downloaded CI preview artifact.
The test uses a temporary localhost port, serves existing prerendered HTML and assets, and makes
no production changes. Python Playwright is test-only; no browser automation dependency is shipped
to site visitors. This complements initial-HTML checks: successful HTML fetches cannot detect a
route module referencing a server loader removed by the client compiler.

### Manual terminal interaction

Run the website with `npm run dev`, then open
`http://127.0.0.1:5173/tutorial/hello-world` in a real browser and verify:

1. the loading overlay is replaced by the Hello World terminal;
2. pressing Enter once changes the message to `Button pressed 1 time.`;
3. **Restart preview** restores `Try the focused button below.`;
4. resizing the browser reflows the xterm surface without losing the component; and
5. navigating away removes the xterm host, with no further input or resize handling.

This is a manual browser acceptance check, not automated E2E coverage. Automated
coverage is split between `HelloWorldTutorialTests` (rendering, interaction,
instance isolation, and resize) and `terminalInput.test.ts` (ordered input and
listener disposal).
