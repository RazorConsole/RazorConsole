// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

public sealed class StackWidget : Widget
{
    public StackWidget(
        string vnodeId,
        IReadOnlyList<Widget> children,
        int gap = 0,
        bool expand = false,
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
        Expand = expand;
    }

    public int Gap { get; }

    public bool Expand { get; }

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

        return constraints.Constrain(new LayoutSize(width, Expand ? constraints.MaxHeight : height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var flowChildren = Children.Where(child => !IsAbsolutePositioned(child)).ToArray();
        var expandingChildren = flowChildren.Where(IsExpanding).ToArray();
        var totalGaps = Math.Max(0, flowChildren.Length - 1) * Gap;
        var fixedHeight = flowChildren
            .Where(child => !IsExpanding(child))
            .Sum(child => child.DesiredSize.Height);
        var remainingHeight = Math.Max(0, bounds.Height - fixedHeight - totalGaps);
        var expandHeight = expandingChildren.Length == 0 ? 0 : remainingHeight / expandingChildren.Length;
        var expandRemainder = expandingChildren.Length == 0 ? 0 : remainingHeight % expandingChildren.Length;
        var y = bounds.Y;
        foreach (var child in Children)
        {
            if (IsAbsolutePositioned(child))
            {
                ArrangeAbsoluteChild(context, child, bounds);
                continue;
            }

            var expands = IsExpanding(child);
            var allocatedHeight = expands
                ? expandHeight + (expandRemainder-- > 0 ? 1 : 0)
                : child.DesiredSize.Height;
            var childHeight = Math.Min(allocatedHeight, Math.Max(0, bounds.Bottom - y));
            var childWidth = expands ? bounds.Width : Math.Min(child.DesiredSize.Width, bounds.Width);
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

    /// <summary>
    /// Resolves whether a child should claim a share of the Stack's (always vertical) remaining space.
    /// <c>data-expand</c> is overloaded: Box/Panel's <c>Expand</c> parameter sets it to mean "fill
    /// WIDTH" (the Spectre <c>Panel.Expand</c> convention - see Box.razor/Panel.razor), which has
    /// nothing to do with claiming vertical space in a Stack. Box/Panel/Flex nodes always emit an
    /// explicit <c>data-fill-height</c> value (true OR false), so when that attribute is present we
    /// trust it outright and ignore <c>data-expand</c>. Only plain elements that never emit
    /// <c>data-fill-height</c> (e.g. a raw div/text widget with a hand-set <c>data-expand</c>) fall back
    /// to the generic <c>data-expand</c> interpretation, preserving the existing "expand to fill the
    /// stack's remaining height" contract for those cases.
    /// </summary>
    private static bool IsExpanding(Widget child)
        => child.Attributes.TryGetValue("data-fill-height", out var fillHeight)
            ? string.Equals(fillHeight, "true", StringComparison.OrdinalIgnoreCase)
            : child.Attributes.TryGetValue("data-expand", out var expand)
                && string.Equals(expand, "true", StringComparison.OrdinalIgnoreCase);

    private static int? TryGetIntAttribute(Widget child, string name)
    {
        if (!child.Attributes.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, out var value) ? value : null;
    }
}
