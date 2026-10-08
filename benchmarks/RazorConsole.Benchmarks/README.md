# RazorConsole benchmarks

BenchmarkDotNet suite for rendering cost and frame rate (issue #252).

## Plan

1. Drive `ConsoleRenderer` directly (no real terminal) with a component of N rows.
2. Compare both pipelines: `LegacySpectre` vs `WidgetLayout`, at 10 and 100 rows.
3. Measure three scenarios:
   - **Mount** - initial render to a Spectre renderable.
   - **Update** - 10 state changes, each producing a new snapshot.
   - **Frame** - 10 x (update + render to Spectre segments at 120x40). This is the closest proxy for frame rate: `FPS = 10 / mean time`.
4. Track allocations with `MemoryDiagnoser`.

## Run

```powershell
dotnet run -c Release --project benchmarks/RazorConsole.Benchmarks -- --filter "*"
```

Benchmarks run in-process because the repo-wide `Directory.Build.props` multi-targets
BenchmarkDotNet's generated project. Pass `--filter "*Frame*"` to select a scenario.
