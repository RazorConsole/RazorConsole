// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

public sealed class PaddingWidget : Widget
{
    public PaddingWidget(
        string vnodeId,
        Widget child,
        int left = 0,
        int top = 0,
        int right = 0,
        int bottom = 0,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, [child], zIndex)
    {
        if (left < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(left), "Padding cannot be negative.");
        }

        if (top < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "Padding cannot be negative.");
        }

        if (right < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(right), "Padding cannot be negative.");
        }

        if (bottom < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bottom), "Padding cannot be negative.");
        }

        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left { get; }

    public int Top { get; }

    public int Right { get; }

    public int Bottom { get; }

    public Widget Child => Children[0];

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        var childConstraints = constraints.Deflate(Left, Top, Right, Bottom);
        var childSize = Child.Measure(context, childConstraints);
        return constraints.Constrain(new LayoutSize(childSize.Width + Left + Right, childSize.Height + Top + Bottom));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var childX = bounds.X + Left;
        var childY = bounds.Y + Top;
        var childWidth = Math.Max(0, bounds.Width - Left - Right);
        var childHeight = Math.Max(0, bounds.Height - Top - Bottom);
        Child.Arrange(context, new LayoutRect(childX, childY, childWidth, childHeight));
    }

    protected override void PaintCore(PaintContext context)
        => Child.Paint(context);
}
