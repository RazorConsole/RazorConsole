// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests;

internal static class TestHelpers
{
    public static ConsoleRenderer CreateTestRenderer(IServiceProvider? serviceProvider = null)
    {
        if (serviceProvider?.GetService<WidgetTranslationContext>() is null)
        {
            var services = new ServiceCollection();
            services.AddRazorConsoleServices();
            serviceProvider = services.BuildServiceProvider();
        }

        return new ConsoleRenderer(
            serviceProvider,
            NullLoggerFactory.Instance,
            serviceProvider.GetService<WidgetTranslationContext>(),
            serviceProvider.GetService<LayoutEngine>(),
            serviceProvider.GetService<VNodeIdAccessor>(),
            serviceProvider.GetService<VNodeLayoutAccessor>(),
            serviceProvider.GetService<TerminalMonitor>());
    }
}

/// <summary>
/// Frame sources for tests that build a view from a Spectre renderable instead of a laid-out widget tree.
/// </summary>
internal static class TestFrames
{
    private static readonly RenderOptions Options = CreateOptions();

    /// <summary>
    /// A source that paints <paramref name="renderable"/> at 80 columns into a canvas exactly as large as its output.
    /// </summary>
    public static FrameSource Of(IRenderable renderable) => new RenderableFrameSource(renderable);

    /// <summary>
    /// The same, as if it was laid out for the render with sequence <paramref name="renderSequence"/>.
    /// </summary>
    public static FrameSource Of(IRenderable renderable, long renderSequence)
        => new RenderableFrameSource(renderable) { RenderSequence = renderSequence };

    private static RenderOptions CreateOptions()
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        return new RenderOptions(console.Profile.Capabilities, new Size(80, 25));
    }

    private sealed class RenderableFrameSource(IRenderable renderable) : FrameSource
    {
        public override TerminalCanvas Paint()
        {
            var lines = Segment.SplitLines(renderable.Render(Options, 80));
            var width = lines.Count == 0 ? 0 : lines.Max(line => Segment.CellCount(line));
            var canvas = new TerminalCanvas(width, lines.Count);
            for (var y = 0; y < lines.Count; y++)
            {
                canvas.WriteSegments(0, y, lines[y]);
            }

            return canvas;
        }
    }
}
