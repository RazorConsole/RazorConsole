// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

public sealed class AlignWidget : Widget
{
    public AlignWidget(
        string vnodeId,
        Widget child,
        HorizontalAlignment horizontal = HorizontalAlignment.Left,
        VerticalAlignment vertical = VerticalAlignment.Top,
        int? width = null,
        int? height = null,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, [child], zIndex)
    {
        if (width is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive when specified.");
        }

        if (height is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive when specified.");
        }

        Horizontal = horizontal;
        Vertical = vertical;
        Width = width;
        Height = height;
    }

    public HorizontalAlignment Horizontal { get; }

    public VerticalAlignment Vertical { get; }

    public int? Width { get; }

    public int? Height { get; }

    public Widget Child => Children[0];

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        var childSize = Child.Measure(context, new BoxConstraints(0, constraints.MaxWidth, 0, constraints.MaxHeight));
        var width = Width ?? (Horizontal == HorizontalAlignment.Left ? childSize.Width : constraints.MaxWidth);
        var height = Height ?? childSize.Height;
        return constraints.Constrain(new LayoutSize(width, height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var childWidth = Math.Min(Child.DesiredSize.Width, bounds.Width);
        var childHeight = Math.Min(Child.DesiredSize.Height, bounds.Height);
        var left = bounds.X + ResolveHorizontalOffset(bounds.Width, childWidth);
        var top = bounds.Y + ResolveVerticalOffset(bounds.Height, childHeight);

        Child.Arrange(context, new LayoutRect(left, top, childWidth, childHeight));
    }

    protected override void PaintCore(PaintContext context)
    {
        if (Child is TextWidget textWidget && Horizontal != HorizontalAlignment.Left)
        {
            PaintAlignedText(context, textWidget);
            return;
        }

        Child.Paint(context);
    }

    private void PaintAlignedText(PaintContext context, TextWidget textWidget)
    {
        var lines = textWidget.GetWrappedLines(textWidget.Bounds.Width);
        var maxLines = Math.Min(textWidget.Bounds.Height, lines.Length);
        for (var row = 0; row < maxLines; row++)
        {
            var line = lines[row];
            var lineWidth = Math.Min(Segment.CellCount([new Segment(line)]), textWidget.Bounds.Width);
            var x = textWidget.Bounds.X + ResolveHorizontalOffset(textWidget.Bounds.Width, lineWidth);
            context.Canvas.Write(x, textWidget.Bounds.Y + row, line, Math.Max(0, textWidget.Bounds.Right - x), textWidget.Style);
        }
    }

    private int ResolveHorizontalOffset(int width, int childWidth)
        => Horizontal switch
        {
            HorizontalAlignment.Center => (width - childWidth) / 2,
            HorizontalAlignment.Right => width - childWidth,
            _ => 0,
        };

    private int ResolveVerticalOffset(int height, int childHeight)
        => Vertical switch
        {
            VerticalAlignment.Middle => (height - childHeight) / 2,
            VerticalAlignment.Bottom => height - childHeight,
            _ => 0,
        };
}
