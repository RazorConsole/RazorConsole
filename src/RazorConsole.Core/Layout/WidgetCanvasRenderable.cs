// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

internal sealed class WidgetCanvasRenderable(Widget root, LayoutSize size) : IRenderable
{
    private readonly Widget _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly LayoutSize _size = size;

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        var width = Math.Min(_size.Width, Math.Max(0, maxWidth));
        return new Measurement(width, width);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var canvas = new TerminalCanvas(_size.Width, _size.Height);
        _root.Paint(new PaintContext(canvas));
        return canvas.RenderSegments(maxWidth);
    }
}