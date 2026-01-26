// Copyright (c) RazorConsole. All rights reserved.

using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Benchmarks.Components;
using RazorConsole.Core;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Benchmarks;

/// <summary>
/// Benchmarks for VNode to Spectre.Console translation.
/// Measures the time to convert virtual DOM to renderable objects.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class TranslationBenchmarks
{
    private IServiceProvider? _serviceProvider;
    private ConsoleRenderer? _renderer;
    private TranslationContext? _translationContext;
    private VNode? _simpleVNode;
    private VNode? _complexVNode;
    private IRenderable? _lastRenderable;

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        _serviceProvider = services.BuildServiceProvider();

        _translationContext = _serviceProvider.GetRequiredService<TranslationContext>();
        _renderer = new ConsoleRenderer(_serviceProvider, NullLoggerFactory.Instance, _translationContext);

        // Pre-render components to get VNodes
        var simpleSnapshot = await _renderer.MountComponentAsync<SimpleComponent>(
            ParameterView.Empty,
            CancellationToken.None).ConfigureAwait(false);
        _simpleVNode = simpleSnapshot.Root;

        var complexSnapshot = await _renderer.MountComponentAsync<ComplexComponent>(
            ParameterView.Empty,
            CancellationToken.None).ConfigureAwait(false);
        _complexVNode = complexSnapshot.Root;
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

    [Benchmark(Description = "Translate simple VNode to Renderable")]
    public void TranslateSimpleVNode()
    {
        if (_simpleVNode is not null)
        {
            _lastRenderable = _translationContext!.Translate(_simpleVNode);
        }
    }

    [Benchmark(Description = "Translate complex VNode to Renderable")]
    public void TranslateComplexVNode()
    {
        if (_complexVNode is not null)
        {
            _lastRenderable = _translationContext!.Translate(_complexVNode);
        }
    }
}
