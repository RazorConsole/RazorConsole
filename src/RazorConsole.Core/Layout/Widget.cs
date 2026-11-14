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

    internal bool IsAbsolutePositioned
        => Attributes.TryGetValue("position", out var value)
            && string.Equals(value, "absolute", StringComparison.OrdinalIgnoreCase);

    private bool IsCentered
        => Attributes.TryGetValue("data-centered", out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    public LayoutSize Measure(LayoutContext context, BoxConstraints constraints)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (IsAbsolutePositioned && context.DocumentSize is { } document)
        {
            // Absolute elements are laid out against the document, not the parent's remaining space.
            var left = TryGetIntAttribute("left");
            var right = TryGetIntAttribute("right");
            var availableWidth = Math.Max(0, document.Width - (left ?? 0) - (right ?? 0));
            constraints = new BoxConstraints(0, availableWidth, 0, document.Height);
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

        if (IsAbsolutePositioned && context.DocumentSize is { } document)
        {
            bounds = ResolveAbsoluteBounds(context, document);
            var previousOrigin = context.AbsoluteOrigin;
            context.AbsoluteOrigin = new LayoutPoint(bounds.X, bounds.Y);
            Bounds = bounds;
            ArrangeCore(context, bounds);
            context.AbsoluteOrigin = previousOrigin;
            return;
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

        if (IsAbsolutePositioned && context.TryDeferOverlay(this))
        {
            return;
        }

        // Skip widgets that lie entirely outside the current clip (e.g. rows scrolled out of a viewport).
        if (!Bounds.IsEmpty && !context.Canvas.IsVisible(Bounds))
        {
            return;
        }

        PaintCore(context);
    }

    internal void PaintOverlay(PaintContext context)
        => PaintCore(context);

    public virtual LayoutBox CreateLayoutBox()
        => new(
            VNodeId,
            Bounds,
            ZIndex,
            Children.Select(child => child.CreateLayoutBox()).ToArray());

    private LayoutRect ResolveAbsoluteBounds(LayoutContext context, LayoutSize document)
    {
        var left = TryGetIntAttribute("left");
        var top = TryGetIntAttribute("top");
        var right = TryGetIntAttribute("right");
        var bottom = TryGetIntAttribute("bottom");
        var origin = context.AbsoluteOrigin;
        var width = Math.Min(DesiredSize.Width, Math.Max(0, document.Width - (left ?? 0)));
        var height = DesiredSize.Height;

        // top/left are offsets from the document root or nearest absolute ancestor;
        // bottom/right are always measured from the document edges.
        if (IsCentered)
        {
            return new LayoutRect(
                Math.Max(0, (document.Width - width) / 2),
                Math.Max(0, (document.Height - height) / 2),
                width,
                height);
        }

        var x = left.HasValue
            ? origin.X + left.Value
            : right.HasValue
                ? Math.Max(0, document.Width - right.Value - width)
                : origin.X;
        var y = top.HasValue
            ? origin.Y + top.Value
            : bottom.HasValue
                ? Math.Max(0, document.Height - bottom.Value - height)
                : origin.Y;

        return new LayoutRect(x, y, width, height);
    }

    private int? TryGetIntAttribute(string name)
        => Attributes.TryGetValue(name, out var raw) && int.TryParse(raw, out var value) ? value : null;

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

    internal LayoutSize? DocumentSize { get; set; }

    internal LayoutPoint AbsoluteOrigin { get; set; }
}

public sealed class PaintContext
{
    private readonly List<Widget> _overlays = [];

    public PaintContext(TerminalCanvas canvas)
    {
        Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    public TerminalCanvas Canvas { get; }

    internal bool DeferAbsolute { get; init; }

    internal bool TryDeferOverlay(Widget widget)
    {
        if (!DeferAbsolute)
        {
            return false;
        }

        _overlays.Add(widget);
        return true;
    }

    internal void PaintOverlays()
    {
        // Lowest z-index first; ties keep tree order. Overlays nested in an overlay are queued while it paints.
        while (_overlays.Count > 0)
        {
            var next = 0;
            for (var i = 1; i < _overlays.Count; i++)
            {
                if (_overlays[i].ZIndex < _overlays[next].ZIndex)
                {
                    next = i;
                }
            }

            var widget = _overlays[next];
            _overlays.RemoveAt(next);
            widget.PaintOverlay(this);
        }
    }
}

public sealed record LayoutBox(
    string VNodeId,
    LayoutRect Bounds,
    int ZIndex,
    IReadOnlyList<LayoutBox> Children)
{
    /// <summary>Gets the clip inherited from the nearest scroll viewport ancestor, or <see langword="null"/> when unclipped.</summary>
    public LayoutRect? Clip { get; init; }

    /// <summary>Gets the scrollbar of a scroll container that can be clicked and dragged, or <see langword="null"/>.</summary>
    public ScrollLayoutInfo? Scroll { get; init; }

    /// <summary>Gets <see cref="Bounds"/> limited by <see cref="Clip"/>; empty when scrolled out of view.</summary>
    public LayoutRect VisibleBounds => Clip is { } clip ? Bounds.Intersect(clip) : Bounds;

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
            IsCentered: false)
        {
            VisibleBounds = VisibleBounds,
            Scroll = Scroll,
        };
}
