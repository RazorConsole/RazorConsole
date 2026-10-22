// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

internal sealed class WidgetCanvasRenderable(Widget root, LayoutSize size, Action<TerminalCanvas>? painted = null) : IRenderable
{
    private readonly Widget _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly LayoutSize _size = size;
    private readonly Action<TerminalCanvas>? _painted = painted;

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        var width = Math.Min(_size.Width, Math.Max(0, maxWidth));
        return new Measurement(width, width);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var canvas = PaintToCanvas();
        // Diagnostics only: receives each painted canvas, which is never written again.
        _painted?.Invoke(canvas);
        return canvas.RenderSegments(maxWidth);
    }

    internal TerminalCanvas PaintToCanvas()
    {
        var canvas = new TerminalCanvas(_size.Width, _size.Height);
        var context = new PaintContext(canvas) { DeferAbsolute = true };
        _root.Paint(context);
        context.PaintOverlays();
        return canvas;
    }
}
