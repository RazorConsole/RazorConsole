# How fast is the WidgetLayout renderer? A live FPS benchmark

Since 0.6.0 RazorConsole renders through the **WidgetLayout** pipeline by default (see [what's new in 0.6.0](/docs/whats-new-in-razorconsole-0-6-0) and the [WidgetLayout guide](/docs/widget-layout)). Instead of a synthetic micro-benchmark, we built a real RazorConsole app that redraws itself as fast as it can and shows its own frame rate on screen.

```preview
FpsBenchmark
```

The terminal above is not a recording: it is a live RazorConsole component (a trimmed-down version of `examples/FpsBenchmark`) running in your browser through WebAssembly and rendered with the WidgetLayout pipeline. Browser numbers will be lower than a native terminal because WASM runs on a single thread.

## What the app measures

The component calls `StateHasChanged` in a tight loop, so every iteration runs a full Razor render → WidgetLayout measure/arrange → terminal write. Two numbers are shown:

| Metric | Meaning |
| --- | --- |
| **Render FPS** | Component render passes completed per second by the WidgetLayout pipeline. |
| **Drawn FPS** | Calls Spectre.Console received to write a frame to the terminal (counted by a small `IAnsiConsole` wrapper). Closer to what the user sees, but still not the monitor refresh rate. |

Both also show average frame time, frame count, peak and average-since-start.

## Run it yourself

```bash
dotnet run --project examples/FpsBenchmark -c Release
dotnet run --project examples/FpsBenchmark -c Release -- --rows 40   # heavier tree
```

Press `Ctrl+C` to exit. `--rows` controls how many animated progress rows are on screen, so you can see how frame rate scales with UI size.

## Results

In a native run (12 rows, 100x24 terminal, .NET 9, Release, Windows ConPTY inside a VM) the app sustained roughly **2,800 Render FPS** (about 0.4 ms per frame) and **~800 Drawn FPS**. Numbers vary a lot with the terminal emulator, hardware and row count, so treat them as an order of magnitude, not a guarantee. Terminals themselves typically paint at 60-240 Hz, so the pipeline is far from being the bottleneck for typical TUIs.

> [!NOTE]
> The benchmark deliberately runs without any frame limiter. Real apps only render when state changes.

## Takeaways

- WidgetLayout comfortably renders well beyond display refresh rates for ordinary screens.
- The example makes regressions easy to spot: run it before and after a change and compare the on-screen numbers.
- Source: [`examples/FpsBenchmark`](https://github.com/RazorConsole/RazorConsole/tree/main/examples/FpsBenchmark).