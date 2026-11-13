# Widget Layout

WidgetLayout is RazorConsole's rendering pipeline: the default since 0.6.0 and the only pipeline since 1.0. It gives RazorConsole ownership of terminal layout before output reaches Spectre.Console, which means components can expose reliable bounds, focus targets, and layout metadata instead of depending on layout decisions that happen inside Spectre renderables.

## Why it exists

Rendering each `VNode` directly into Spectre.Console `IRenderable` objects, as RazorConsole did before 0.6.0, works well for final terminal output, but it makes it difficult for RazorConsole to answer practical UI questions like "where is this element?", "which node is under focus?", or "what bounds should this component report?" because Spectre computes much of that geometry while rendering.

The larger goal is to support richer coordinate-aware events. Events such as hover, click, drag, and pointer-style interactions need RazorConsole to map a terminal coordinate back to the component or delegate that owns that cell. WidgetLayout gives RazorConsole a committed box tree so those future event dispatch paths can be built on known element bounds instead of best-effort render tracing.

WidgetLayout adds a RazorConsole-owned widget layer between the virtual DOM and Spectre. Layout is measured and arranged first, so RazorConsole can commit a box tree, update layout accessors, and then paint the final terminal frame.

## How rendering flows

At a high level, a render goes through this path:

```text
Razor component
  -> Blazor render batch
  -> RazorConsole VNode tree
  -> Widget tree
  -> LayoutEngine measure/arrange
  -> TerminalCanvas paint
  -> Spectre.Console IRenderable
  -> terminal output
```

Native widgets such as rows, columns, padding, alignment, panels, tables, text input, and scrollable regions compute their own sizes and child bounds. Spectre-backed output such as charts, Figlet text, syntax highlighting and spinners is wrapped in `SpectreWidget` leaves, which take part in layout and paint into the same terminal cells.

## Scroll containers

Scrolling is part of layout. A container with `Overflow="OverflowMode.Scroll"` measures its content with unbounded height, arranges it at its scrolled position, and clips and culls it when painting. The layout it reports distinguishes the logical bounds of a node, which can lie outside the container, from its visible bounds, which are clipped by the scroll viewport; hit testing uses the visible bounds, so a row that has scrolled out of view receives no pointer events. See [Scrolling](/blog/scrolling/).

## Upgrading from 0.x

RazorConsole 1.0 removes the legacy Spectre layout pipeline, which rendered each `VNode` straight to a Spectre.Console `IRenderable` and could not report element bounds. WidgetLayout, the default since 0.6.0, is now the only pipeline.

The 0.x deprecation warning said the legacy pipeline would be removed in 1.1.0. It was removed in 1.0.0 instead, together with the option that selected it, so code that followed the warning's advice and set `RenderingPipeline = RazorConsoleRenderingPipeline.WidgetLayout` no longer compiles.

- Remove any `ConsoleAppOptions.RenderingPipeline` assignment. The option and the `RazorConsoleRenderingPipeline` enum no longer exist.
- Remove the `RAZORCONSOLE_RENDERING_PIPELINE` environment variable. It is no longer read, so setting it to `LegacySpectre` has no effect: the app renders with WidgetLayout.
- Move custom `ITranslationMiddleware` translators to `IWidgetTranslator`; the [custom translator guide](/docs#custom-translators) has a migration table.
- Replace `ConsoleAppOptions.AfterRenderAsync`, which was removed. Despite its name, it ran once, after the first render. Use one of these instead:
  - the root component's `OnAfterRenderAsync(bool firstRender)`, for work tied to the UI;
  - `IHostApplicationLifetime.ApplicationStarted`, for work that should start with the app;
  - an `IHostedService` or `BackgroundService`, for long-running work.
- Stop using `ConsoleViewResult`, `ConsoleLiveDisplayContext`, `FocusManager.BeginSession` and `FocusManager.FocusSession`. They are now internal: the host creates and drives the live display. For tests, use `HeadlessTerminal` from `RazorConsole.Testing`; see the [testing guide](/blog/testing/).

Spectre.Console-backed components and `IRenderable` content remain supported. Charts, Figlet text, syntax highlighting, spinners and canvases are painted as `SpectreWidget` leaves, and a custom translator can wrap any `IRenderable` the same way.

## Share feedback

If a component renders differently than you expect, or you are building interactions that need coordinate-aware events such as hover or click, please open an issue with:

- the component or example you are rendering
- the terminal size, operating system, and any relevant styles or layout options
- a small repro snippet when possible

That feedback helps prioritize which widgets and event scenarios to improve next.
