// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

public sealed class BoxWidget : Widget
{
    public BoxWidget(
        string vnodeId,
        Widget child,
        int paddingLeft = 0,
        int paddingTop = 0,
        int paddingRight = 0,
        int paddingBottom = 0,
        int? width = null,
        int? height = null,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, [child], zIndex)
    {
        if (paddingLeft < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingLeft), "Padding cannot be negative.");
        }

        if (paddingTop < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingTop), "Padding cannot be negative.");
        }

        if (paddingRight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingRight), "Padding cannot be negative.");
        }

        if (paddingBottom < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingBottom), "Padding cannot be negative.");
        }

        if (width is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive when specified.");
        }

        if (height is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive when specified.");
        }

        PaddingLeft = paddingLeft;
        PaddingTop = paddingTop;
        PaddingRight = paddingRight;
        PaddingBottom = paddingBottom;
        Width = width;
        Height = height;
    }

    public Widget Child => Children[0];

    public int PaddingLeft { get; }

    public int PaddingTop { get; }

    public int PaddingRight { get; }

    public int PaddingBottom { get; }

    public int? Width { get; }

    public int? Height { get; }

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        var childConstraints = constraints.Deflate(PaddingLeft, PaddingTop, PaddingRight, PaddingBottom);
        var childSize = Child.Measure(context, childConstraints);
        var width = Width ?? childSize.Width + PaddingLeft + PaddingRight;
        var height = Height ?? childSize.Height + PaddingTop + PaddingBottom;
        return constraints.Constrain(new LayoutSize(width, height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var childX = bounds.X + PaddingLeft;
        var childY = bounds.Y + PaddingTop;
        var childWidth = Math.Max(0, bounds.Width - PaddingLeft - PaddingRight);
        var childHeight = Math.Max(0, bounds.Height - PaddingTop - PaddingBottom);
        Child.Arrange(context, new LayoutRect(childX, childY, childWidth, childHeight));
    }

    protected override void PaintCore(PaintContext context)
        => Child.Paint(context);
}