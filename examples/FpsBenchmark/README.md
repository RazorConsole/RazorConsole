# FpsBenchmark

A real RazorConsole app that re-renders as fast as possible and shows the live FPS (renders per second), average frame time, peak and average FPS.

```powershell
dotnet run -c Release --project examples/FpsBenchmark -- --widget-layout --rows 20
dotnet run -c Release --project examples/FpsBenchmark -- --legacy --rows 20
```

- `--widget-layout` (default) / `--legacy` selects the rendering pipeline.
- `--rows N` sets the number of animated rows (default 20); raise it to stress the renderer.
- Press Ctrl+C to exit.

Each frame is a full component update (`StateHasChanged`) awaited through the renderer and live display, so FPS reflects the end-to-end pipeline, bounded by terminal speed.
