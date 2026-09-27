# Gallery workbench

Run from the repository root (requires the SDK pinned in `global.json`):

```sh
dotnet run --project src/RazorConsole.Gallery -f net10.0
```

Publish a macOS Apple Silicon Native AOT build:

```sh
dotnet publish src/RazorConsole.Gallery/RazorConsole.Gallery.csproj \
  -c Release \
  -f net10.0 \
  -r osx-arm64 \
  -p:PublishAot=true \
  -p:StripSymbols=true
```

Keep the published executable and `Fonts/` directory together. The custom Figlet font is resolved relative to the executable, so the Gallery can be launched from any working directory.

The native host enables terminal mouse reporting. Click a category to collapse it,
click a component to open it, or type in the search field. Search matches component
names and categories. Diagnostics is collapsed by default. Navigation scrolls
independently using the wheel, page controls or PageUp/PageDown. Tab moves focus;
arrow keys move between visible/navigation entries; Enter activates a focused item.
A dot denotes the current selection; the background denotes keyboard focus.
Drag the vertical divider to resize the sidebar. When focused, Left/Right resize
one column and Home restores the default width. The main pane retains at least
30 columns (when the terminal is wide enough); resize limits follow the viewport.
Clickable buttons also highlight on hover without moving focus. Search is read-only
and dimmed until focused, then shows its empty-field placeholder.
Press `/` while a gallery button is focused to jump to search. Text editors retain
their normal character input.

All 28 navigation entries use **GalleryWorkbench**, with Preview selected on entry
and a Code tab. Interactive pages expose Reset; static reference examples do not
show a non-functional Reset button. The content viewport handles wheel input
independently, so properties remain accessible in short terminals. The target
baseline is 80×24 or larger.

- Align: alignment, dimensions and border controls.
- Border, FlexBox, Spinner and Small Charts: selectable example presets.
- Columns, Rows, Panel, Padder and Step Chart: live properties and matching code.
- Text Button, Text Input and Select: real component interaction and Reset.
- Scrollable: independent nested wheel scrolling and Reset for scroll offsets.
- Z-Index: bounded overlapping/nested layers, movement, order, modal and notification.
- Drag: real mouse capture, bounded dragging, position feedback, arrow-key movement and Reset.
- CLI Info: live diagnostics; Reset refreshes readings.
- Focus diagnostic: insert/remove an input and check keyboard/mouse focus.
- Content/reference pages retain their original examples behind Preview/Code tabs.

Full-screen preview and source selection/copy remain follow-up work. The former
multi-line update banner is not displayed in the compact footer.

`GalleryWorkbenchTests` and `GallerySampleTests` exercise real Razor rendering,
mouse controls, navigation, search, keyboard activation, independent scrolling and
resize through TestTerminal. Every route is checked at 80×24 and resized to 100×35;
additional tests cover presets/properties, Reset, real input, Select and modal flow.

For output-path regression tests, opt in to
`TestTerminal.StartAsync<App>(..., captureTerminalOutput: true)`. This replaces the
no-op canvas with real `LiveDisplayCanvas`/`DiffRenderable` execution and captures
the last 50 terminal-output batches in `TerminalOutput`. The transport remains
in-memory: it cannot reproduce OS stdin ownership or a missing terminal reply.
`WheelBurst_WithRealDiffOutput_ResizeAndInput_RemainsResponsive` combines raw SGR
wheel bursts, resize, stale cursor replies, navigation and text input.

The complementary Unix PTY regression deliberately never answers cursor queries:

```sh
dotnet build src/RazorConsole.Gallery -c Release -f net10.0
uv run --with pyte python tests/e2e/gallery-scroll-pty.py
```

It sends 1,000 wheel events, resizes, injects a fragmented stale reply, then checks
clicks, typing and Ctrl+C. Any cursor query or scrollback-erasing sequence fails
the test. This is not a tmux smoke test; it covers the native input/output boundary
that in-process tests cannot model. The live renderer owns the viewport, uses
absolute cell coordinates, clips output to its height and clears only the visible
viewport on initialization/resize or recovery after a failed write. It never
synchronously reads cursor position. Input is owned by the selected `IConsoleInput`
implementation; terminal replies are decoded there rather than by rendering code.

To add a page, compose `GalleryWorkbench` with `Preview`, optional `Properties` or
`Options`, and `Code`. Give buttons stable unique IDs; derive preview and generated
code from the same state. Add the navigation entry and a route test. Do not replace
the demonstrated core component with a gallery-only imitation.
