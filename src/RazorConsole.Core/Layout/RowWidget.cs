// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

public sealed class RowWidget : Widget
{
    public RowWidget(
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

        for (var i = 0; i < Children.Count; i++)
        {
            var childSize = Children[i].Measure(context, childConstraints);
            width += childSize.Width;
            height = Math.Max(height, childSize.Height);

            if (i < Children.Count - 1)
            {
                width += Gap;
            }
        }

        return constraints.Constrain(new LayoutSize(Expand ? constraints.MaxWidth : width, height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var expandingChildren = Children.Where(IsExpanding).ToArray();
        var totalGaps = Math.Max(0, Children.Count - 1) * Gap;
        var fixedWidth = Children
            .Where(child => !IsExpanding(child))
            .Sum(child => child.DesiredSize.Width);
        var remainingWidth = Math.Max(0, bounds.Width - fixedWidth - totalGaps);
        var expandWidth = expandingChildren.Length == 0 ? 0 : remainingWidth / expandingChildren.Length;
        var expandRemainder = expandingChildren.Length == 0 ? 0 : remainingWidth % expandingChildren.Length;
        var x = bounds.X;
        foreach (var child in Children)
        {
            var expands = IsExpanding(child);
            var allocatedWidth = expands
                ? expandWidth + (expandRemainder-- > 0 ? 1 : 0)
                : child.DesiredSize.Width;
            var childWidth = Math.Min(allocatedWidth, Math.Max(0, bounds.Right - x));
            child.Arrange(context, new LayoutRect(x, bounds.Y, childWidth, bounds.Height));
            x += childWidth + Gap;
        }
    }

    protected override void PaintCore(PaintContext context)
    {
        foreach (var child in Children.OrderBy(child => child.ZIndex))
        {
            child.Paint(context);
        }
    }

    private static bool IsExpanding(Widget child)
        => child.Attributes.TryGetValue("data-expand", out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
