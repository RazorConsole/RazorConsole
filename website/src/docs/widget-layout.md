# Widget Layout

WidgetLayout becomes RazorConsole's default rendering pipeline starting in 0.6.0. It gives RazorConsole ownership of terminal layout before output reaches Spectre.Console, which means components can expose reliable bounds, focus targets, and layout metadata instead of depending on layout decisions that happen inside Spectre renderables.

## Why it exists

The legacy Spectre pipeline renders each `VNode` directly into Spectre.Console `IRenderable` objects. That works well for final terminal output, but it makes it difficult for RazorConsole to answer practical UI questions like "where is this element?", "which node is under focus?", or "what bounds should this component report?" because Spectre computes much of that geometry while rendering.

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

Native widgets such as rows, columns, padding, alignment, panels, tables, text input, and scrollable regions compute their own sizes and child bounds. Components that are still difficult to port can be wrapped as `SpectreWidget` leaves, so charts, figlet text, syntax highlighting, and other Spectre-backed output can continue to work while the layout system grows.

## Falling back to legacy layout

Starting in 0.6.0, WidgetLayout is used by default when no rendering pipeline is configured. To run an app with the legacy Spectre pipeline, set `RAZORCONSOLE_RENDERING_PIPELINE` before launching the app:

```sh
$env:RAZORCONSOLE_RENDERING_PIPELINE = "LegacySpectre"
dotnet run
```

You can also use the shorter values `legacy` or `spectre`. To explicitly request the default widget pipeline, use `WidgetLayout` or `widget`.

In code, you can choose the pipeline through `ConsoleAppOptions`:

```csharp
builder.Services.Configure<ConsoleAppOptions>(options =>
{
    options.RenderingPipeline = RazorConsoleRenderingPipeline.LegacySpectre;
});
```

Use the legacy pipeline when you are comparing output during migration or temporarily depending on Spectre layout behavior that has not been ported to native widgets yet.

## Share feedback

WidgetLayout is still expanding its native widget coverage. If you see rendering differences between WidgetLayout and the legacy Spectre pipeline, or if you are building interactions that need coordinate-aware events such as hover or click, please open an issue with:

- the component or example you are rendering
- whether the behavior differs from `LegacySpectre`
- the terminal size, operating system, and any relevant styles or layout options
- a small repro snippet when possible

That feedback helps prioritize which widgets and event scenarios should be ported next.