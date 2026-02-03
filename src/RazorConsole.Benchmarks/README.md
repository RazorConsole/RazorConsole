# RazorConsole Benchmarks

This project contains performance benchmarks for RazorConsole rendering and frame rate measurements using [BenchmarkDotNet](https://benchmarkdotnet.org/).

## Overview

The benchmarks measure key performance characteristics of RazorConsole:

### RenderingBenchmarks
Measures the time to mount and render components to the virtual DOM (VNode).
- **Simple Component**: Basic text rendering
- **Complex Component**: Nested structure with panels, columns, and lists
- **Interactive Component**: Component with buttons and state management

### FrameRateBenchmarks
Measures throughput of rapid re-renders and effective frame rates.
- **Multiple Renders**: Measures performance of rendering the same component multiple times
- **Effective Frame Rate**: Calculates frames per second (FPS) for different component complexities
- Parameterized by frame count (10, 50, 100 frames)

### TranslationBenchmarks
Measures VNode to Spectre.Console IRenderable translation performance.
- **VNode Translation**: Time to convert virtual DOM to console renderables
- Isolated measurement of the translation layer performance

## Running Benchmarks

### Run All Benchmarks
```bash
cd src/RazorConsole.Benchmarks
dotnet run -c Release
```

### Run Specific Benchmark
```bash
dotnet run -c Release -- --filter "*RenderingBenchmarks*"
dotnet run -c Release -- --filter "*FrameRateBenchmarks*"
dotnet run -c Release -- --filter "*TranslationBenchmarks*"
```

### Run with Custom Options
```bash
# Export results to different formats
dotnet run -c Release -- --exporters json html markdown

# Run with memory profiler
dotnet run -c Release -- --memory

# Run with less iterations (faster, less accurate)
dotnet run -c Release -- --job short
```

## Interpreting Results

BenchmarkDotNet provides detailed metrics:
- **Mean**: Average execution time
- **Error**: Half of the 99.9% confidence interval
- **StdDev**: Standard deviation of all measurements
- **Allocated**: Memory allocated per operation

### Example Output
```
| Method                           | FrameCount | Mean      | Error    | StdDev   | Allocated |
|--------------------------------- |----------- |----------:|---------:|---------:|----------:|
| Render simple component          | -          |  1.234 ms | 0.012 ms | 0.011 ms |   45.6 KB |
| Render complex component         | -          |  3.456 ms | 0.034 ms | 0.032 ms |  123.4 KB |
| Multiple renders of simple       | 10         | 12.345 ms | 0.123 ms | 0.115 ms |  456.7 KB |
| Effective frame rate (simple)    | 100        |  123.4 ms | 1.234 ms | 1.154 ms | 4567.8 KB |
```

### What to Look For

**Good Performance Indicators:**
- Simple component renders should be < 5ms
- Frame rate > 30 FPS for simple components
- Memory allocation is consistent across runs
- Low standard deviation indicates stable performance

**Performance Issues:**
- Mean time increasing non-linearly with complexity
- High memory allocation that grows unexpectedly
- Large standard deviation (unstable performance)
- Frame rate dropping below 24 FPS for simple components

## Test Components

The benchmark uses three test components in the `Components/` directory:

1. **SimpleComponent.razor**: Single paragraph, minimal overhead
2. **ComplexComponent.razor**: Multi-level nesting with Panel, Columns, and lists
3. **InteractiveComponent.razor**: Stateful component with button handlers

## Adding New Benchmarks

1. Create a new class with `[MemoryDiagnoser]` and job configuration attributes
2. Add `[Benchmark]` methods for operations to measure
3. Use `[Params]` to test with different configurations
4. Add setup/cleanup with `[GlobalSetup]` and `[GlobalCleanup]`

Example:
```csharp
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MyBenchmarks
{
    [Params(10, 100, 1000)]
    public int Size { get; set; }

    [Benchmark]
    public void MyOperation()
    {
        // Operation to benchmark
    }
}
```

## Continuous Integration

Benchmarks can be integrated into CI/CD pipelines to track performance over time:

```bash
# Run benchmarks and fail if performance degrades
dotnet run -c Release -- --filter "*" --maxStdDev 0.05
```

## Further Reading

- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/articles/overview.html)
- [Performance Best Practices](https://learn.microsoft.com/en-us/dotnet/core/performance/)
- [Spectre.Console Performance](https://spectreconsole.net/)
