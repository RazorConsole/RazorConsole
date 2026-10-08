# FpsBenchmark

A real RazorConsole app that re-renders as fast as possible and shows two live numbers: **Drawn FPS** (renderables Spectre actually writes to the terminal, counted by a delegating `IAnsiConsole`) and **Render FPS** (component updates completed by the pipeline, which can be higher because the live display coalesces redraws), plus average frame time, peak and average.

```powershell
dotnet run -c Release --project examples/FpsBenchmark -- --widget-layout --rows 20
dotnet run -c Release --project examples/FpsBenchmark -- --legacy --rows 20
```

- `--widget-layout` (default) / `--legacy` selects the rendering pipeline.
- `--rows N` sets the number of animated rows (default 20); raise it to stress the renderer.
- Press Ctrl+C to exit.

Drawn FPS is the closest to what the user sees and is pipeline-agnostic. Render FPS is a full `StateHasChanged` awaited through the renderer.
