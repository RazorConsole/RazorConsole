// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

/// <summary>
/// Shows a vertical window onto taller content, like CSS <c>overflow: scroll</c> without the scrollbar.
/// The content is measured with unbounded height, so the viewport grows with its content until its parent limits it.
/// </summary>
public sealed class ScrollViewportWidget : Widget
{
    public ScrollViewportWidget(
        string vnodeId,
        Widget child,
        int top = 0,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, [child], zIndex)
    {
        if (top < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "Scroll offset cannot be negative.");
        }

        RequestedTop = top;
    }

    public Widget Child => Children[0];

    /// <summary>Gets the offset asked for by the caller, before clamping.</summary>
    public int RequestedTop { get; }

    /// <summary>Gets the offset actually applied by the last arrange, clamped to <c>[0, <see cref="MaxTop"/>]</c>.</summary>
    public int Top { get; private set; }

    /// <summary>Gets the height of the content measured with unbounded height.</summary>
    public int ContentHeight { get; private set; }

    /// <summary>Gets the height of the visible window after the last arrange.</summary>
    public int ViewportHeight { get; private set; }

    /// <summary>Gets the largest valid <see cref="Top"/>; known in the same pass that arranges the content.</summary>
    public int MaxTop => Math.Max(0, ContentHeight - ViewportHeight);

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        if (constraints.MaxWidth == 0 || constraints.MaxHeight == 0)
        {
            ContentHeight = 0;
            return constraints.Constrain(LayoutSize.Empty);
        }

        var childSize = Child.Measure(context, constraints.WithUnboundedHeight());
        ContentHeight = childSize.Height;

        // Like CSS max-height: as tall as the content, up to what the parent allows.
        return constraints.Constrain(new LayoutSize(childSize.Width, Math.Min(ContentHeight, constraints.MaxHeight)));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        ViewportHeight = bounds.Height;
        Top = Math.Clamp(RequestedTop, 0, MaxTop);

        // A "fill height" child resolves to max(content, viewport).
        var childHeight = Math.Max(ContentHeight, bounds.Height);
        Child.Arrange(context, new LayoutRect(bounds.X, bounds.Y - Top, bounds.Width, childHeight));
    }

    protected override void PaintCore(PaintContext context)
    {
        if (Bounds.IsEmpty)
        {
            return;
        }

        using var _ = context.Canvas.PushClip(Bounds);
        Child.Paint(context);
    }

    /// <summary>Gets the rectangle that clips descendants, in document coordinates.</summary>
    internal LayoutRect ClipBounds => Bounds;
}
