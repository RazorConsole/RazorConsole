// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Layout;

public abstract class Widget
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyAttributes =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    protected Widget(
        string vnodeId,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        IReadOnlyList<Widget>? children = null,
        int zIndex = 0)
    {
        VNodeId = vnodeId ?? throw new ArgumentNullException(nameof(vnodeId));
        Key = string.IsNullOrWhiteSpace(key) ? null : key;
        Attributes = attributes ?? EmptyAttributes;
        Children = children ?? Array.Empty<Widget>();
        ZIndex = zIndex;
    }

    public string VNodeId { get; }

    public string? Key { get; }

    public IReadOnlyDictionary<string, string?> Attributes { get; }

    public IReadOnlyList<Widget> Children { get; }

    public int ZIndex { get; }

    public LayoutSize DesiredSize { get; private set; }

    public LayoutRect Bounds { get; private set; }

    public LayoutSize Measure(LayoutContext context, BoxConstraints constraints)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        DesiredSize = MeasureCore(context, constraints);
        return DesiredSize;
    }

    public void Arrange(LayoutContext context, LayoutRect bounds)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        Bounds = bounds;
        ArrangeCore(context, bounds);
    }

    public void Paint(PaintContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        PaintCore(context);
    }

    public virtual LayoutBox CreateLayoutBox()
        => new(
            VNodeId,
            Bounds,
            ZIndex,
            Children.Select(child => child.CreateLayoutBox()).ToArray());

    protected abstract LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints);

    protected abstract void ArrangeCore(LayoutContext context, LayoutRect bounds);

    protected abstract void PaintCore(PaintContext context);
}

public sealed class LayoutContext
{
    public LayoutContext(int renderVersion = 0)
    {
        RenderVersion = renderVersion;
    }

    public int RenderVersion { get; }
}

public sealed class PaintContext
{
    public PaintContext(TerminalCanvas canvas)
    {
        Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    public TerminalCanvas Canvas { get; }
}

public sealed record LayoutBox(
    string VNodeId,
    LayoutRect Bounds,
    int ZIndex,
    IReadOnlyList<LayoutBox> Children)
{
    public VNodeLayoutInfo ToLayoutInfo(bool isVisible, int renderVersion)
        => new(
            VNodeId,
            Bounds.Y,
            Bounds.X,
            Right: null,
            Bottom: null,
            Bounds.Width,
            Bounds.Height,
            ZIndex,
            IsCentered: false);
}
