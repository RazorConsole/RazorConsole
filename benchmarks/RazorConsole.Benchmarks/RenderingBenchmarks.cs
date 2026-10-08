// Copyright (c) RazorConsole. All rights reserved.

#pragma warning disable BL0006 // RenderTree types are "internal-ish"; acceptable for benchmarks.
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Benchmarks;

/// <summary>
/// Measures the cost of a frame: component update -> VDOM snapshot -> Spectre segments.
/// Update and Frame run <see cref="FramesPerRun"/> frames per invocation; frame rate is
/// FramesPerRun / mean time.
/// </summary>
[MemoryDiagnoser]
public class RenderingBenchmarks
{
    private const int FramesPerRun = 10;
    private const int Width = 120;
    private const int Height = 40;

    private ServiceProvider _serviceProvider = null!;
    private ConsoleRenderer _renderer = null!;
    private IAnsiConsole _console = null!;
    private RenderOptions _options = null!;
    private BenchComponent _component = null!;

    [Params(RazorConsoleRenderingPipeline.LegacySpectre, RazorConsoleRenderingPipeline.WidgetLayout)]
    public RazorConsoleRenderingPipeline Pipeline { get; set; }

    [Params(10, 100)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        services.Configure<ConsoleAppOptions>(o => o.RenderingPipeline = Pipeline);
        _serviceProvider = services.BuildServiceProvider();

        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });
        _options = new RenderOptions(_console.Profile.Capabilities, new Size(Width, Height));

        _renderer = CreateRenderer();
        _component = new BenchComponent(Rows);
        _renderer.MountComponentAsync(_component, ParameterView.Empty, CancellationToken.None).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _renderer.Dispose();
        _serviceProvider.Dispose();
    }

    [Benchmark(Description = "Mount (initial render)")]
    public IRenderable? Mount()
    {
        using var renderer = CreateRenderer();
        var component = new BenchComponent(Rows);
        var snapshot = renderer.MountComponentAsync(component, ParameterView.Empty, CancellationToken.None).GetAwaiter().GetResult();
        return snapshot.Renderable;
    }

    [Benchmark(Description = "Update (state change -> snapshot)")]
    public IRenderable? Update()
    {
        IRenderable? renderable = null;
        for (var i = 0; i < FramesPerRun; i++)
        {
            _component.Tick().GetAwaiter().GetResult();
            renderable = _renderer.RefreshSnapshot().Renderable;
        }

        return renderable;
    }

    [Benchmark(Description = "Frame (update + render to segments)")]
    public int Frame()
    {
        var segments = 0;
        for (var i = 0; i < FramesPerRun; i++)
        {
            _component.Tick().GetAwaiter().GetResult();
            var renderable = _renderer.RefreshSnapshot().Renderable!;
            segments += renderable.Render(_options, Width).Count();
        }

        return segments;
    }

    private ConsoleRenderer CreateRenderer()
    {
        return new ConsoleRenderer(
            _serviceProvider,
            NullLoggerFactory.Instance,
            _serviceProvider.GetRequiredService<TranslationContext>(),
            _serviceProvider.GetService<ConsoleAppOptions>(),
            _serviceProvider.GetService<WidgetTranslationContext>(),
            _serviceProvider.GetService<LayoutEngine>(),
            _serviceProvider.GetService<VNodeIdAccessor>(),
            _serviceProvider.GetService<VNodeLayoutAccessor>(),
            _serviceProvider.GetService<TerminalMonitor>());
    }

    private sealed class BenchComponent : ComponentBase
    {
        private int _tick;

        public BenchComponent(int rows) => Rows = rows;

        public int Rows { get; }

        public Task Tick() => InvokeAsync(() =>
        {
            _tick++;
            StateHasChanged();
        });

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            for (var i = 0; i < Rows; i++)
            {
                builder.OpenElement(1, "span");
                builder.SetKey(i);
                builder.AddContent(2, $"Row {i}: frame {_tick}");
                builder.CloseElement();
            }

            builder.CloseElement();
        }
    }
}
