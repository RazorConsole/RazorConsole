// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

internal sealed class CanvasRenderable(TerminalCanvas canvas) : IRenderable
{
    private readonly TerminalCanvas _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        var width = Math.Min(_canvas.Width, Math.Max(0, maxWidth));
        return new Measurement(width, width);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        => _canvas.RenderSegments(maxWidth);
}
