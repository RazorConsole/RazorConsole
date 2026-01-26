// Copyright (c) RazorConsole. All rights reserved.

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
/// Benchmarks for component rendering performance.
/// Measures the time to mount and render components to the virtual DOM.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class RenderingBenchmarks
{
    private IServiceProvider? _serviceProvider;
    private ConsoleRenderer? _renderer;
    private ConsoleRenderer.RenderSnapshot _lastSnapshot;

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

    [Benchmark(Description = "Render simple component")]
    public async Task RenderSimpleComponent()
    {
        _lastSnapshot = await _renderer!.MountComponentAsync<SimpleComponent>(
            ParameterView.Empty,
            CancellationToken.None).ConfigureAwait(false);
    }

    [Benchmark(Description = "Render complex component")]
    public async Task RenderComplexComponent()
    {
        _lastSnapshot = await _renderer!.MountComponentAsync<ComplexComponent>(
            ParameterView.Empty,
            CancellationToken.None).ConfigureAwait(false);
    }

    [Benchmark(Description = "Render interactive component")]
    public async Task RenderInteractiveComponent()
    {
        _lastSnapshot = await _renderer!.MountComponentAsync<InteractiveComponent>(
            ParameterView.Empty,
            CancellationToken.None).ConfigureAwait(false);
    }
}
