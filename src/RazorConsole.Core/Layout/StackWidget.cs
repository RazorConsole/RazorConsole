// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

public sealed class StackWidget : Widget
{
    public StackWidget(
        string vnodeId,
        IReadOnlyList<Widget> children,
        int gap = 0,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, children, zIndex)
    {
        if (gap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gap), "Gap cannot be negative.");
        }

        Gap = gap;
    }

    public int Gap { get; }

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        if (Children.Count == 0)
        {
            return constraints.Constrain(LayoutSize.Empty);
        }

        var width = 0;
        var height = 0;
        var childConstraints = new BoxConstraints(0, constraints.MaxWidth, 0, constraints.MaxHeight);
        var flowIndex = 0;

        for (var i = 0; i < Children.Count; i++)
        {
            var childSize = Children[i].Measure(context, childConstraints);
            if (IsAbsolutePositioned(Children[i]))
            {
                continue;
            }

            width = Math.Max(width, childSize.Width);
            height += childSize.Height;

            if (flowIndex > 0)
            {
                height += Gap;
            }

            flowIndex++;
        }

        return constraints.Constrain(new LayoutSize(width, height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var y = bounds.Y;
        foreach (var child in Children)
        {
            if (IsAbsolutePositioned(child))
            {
                ArrangeAbsoluteChild(context, child, bounds);
                continue;
            }

            var childHeight = Math.Min(child.DesiredSize.Height, Math.Max(0, bounds.Bottom - y));
            var childWidth = Math.Min(child.DesiredSize.Width, bounds.Width);
            child.Arrange(context, new LayoutRect(bounds.X, y, childWidth, childHeight));
            y += childHeight + Gap;
        }
    }

    protected override void PaintCore(PaintContext context)
    {
        foreach (var child in Children.OrderBy(child => child.ZIndex))
        {
            child.Paint(context);
        }
    }

    private static void ArrangeAbsoluteChild(LayoutContext context, Widget child, LayoutRect bounds)
    {
        var childWidth = Math.Min(child.DesiredSize.Width, bounds.Width);
        var childHeight = Math.Min(child.DesiredSize.Height, bounds.Height);
        var left = TryGetIntAttribute(child, "left");
        var top = TryGetIntAttribute(child, "top");
        var right = TryGetIntAttribute(child, "right");
        var bottom = TryGetIntAttribute(child, "bottom");

        var x = left.HasValue
            ? bounds.X + left.Value
            : right.HasValue
                ? bounds.Right - right.Value - childWidth
                : bounds.X;
        var y = top.HasValue
            ? bounds.Y + top.Value
            : bottom.HasValue
                ? bounds.Bottom - bottom.Value - childHeight
                : bounds.Y;

        child.Arrange(context, new LayoutRect(x, y, childWidth, childHeight));
    }

    private static bool IsAbsolutePositioned(Widget child)
        => child.Attributes.TryGetValue("position", out var value)
            && string.Equals(value, "absolute", StringComparison.OrdinalIgnoreCase);

    private static int? TryGetIntAttribute(Widget child, string name)
    {
        if (!child.Attributes.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, out var value) ? value : null;
    }
}
