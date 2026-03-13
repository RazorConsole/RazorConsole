// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Benchmarks.Components;
using RazorConsole.Core;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.Translation.Contexts;

namespace RazorConsole.Benchmarks;

/// <summary>
/// Benchmarks for frame rate and update performance.
/// Measures the throughput of rapid re-renders and updates.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class FrameRateBenchmarks
{
    private IServiceProvider? _serviceProvider;
    private ConsoleRenderer? _renderer;

    [Params(10, 50, 100)]
    public int FrameCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        _serviceProvider = services.BuildServiceProvider();

        var translationContext = _serviceProvider.GetRequiredService<TranslationContext>();
        _renderer = new ConsoleRenderer(_serviceProvider, NullLoggerFactory.Instance, translationContext);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _renderer?.Dispose();
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [Benchmark(Description = "Multiple renders of simple component")]
    public async Task MultipleRendersSimple()
    {
        for (int i = 0; i < FrameCount; i++)
        {
            await _renderer!.MountComponentAsync<SimpleComponent>(
                ParameterView.Empty,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Benchmark(Description = "Multiple renders of complex component")]
    public async Task MultipleRendersComplex()
    {
        for (int i = 0; i < FrameCount; i++)
        {
            await _renderer!.MountComponentAsync<ComplexComponent>(
                ParameterView.Empty,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Benchmark(Description = "Calculate effective frame rate (simple)")]
    public async Task<double> EffectiveFrameRateSimple()
    {
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < FrameCount; i++)
        {
            await _renderer!.MountComponentAsync<SimpleComponent>(
                ParameterView.Empty,
                CancellationToken.None).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return FrameCount / stopwatch.Elapsed.TotalSeconds;
    }
}
