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
        int zIndex = 0,
        bool opaque = false)
        : base(vnodeId, key, attributes, children, zIndex)
    {
        if (gap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gap), "Gap cannot be negative.");
        }

        Gap = gap;
        Expand = expand;
        Opaque = opaque;
    }

    public int Gap { get; }

    public bool Expand { get; }

    /// <summary>Clears its bounds before painting, so overlays hide what lies beneath.</summary>
    public bool Opaque { get; }

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
            if (Children[i].IsAbsolutePositioned)
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
        var flowChildren = Children.Where(child => !child.IsAbsolutePositioned).ToArray();
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
            if (child.IsAbsolutePositioned)
            {
                child.Arrange(context, bounds);
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
        if (Opaque)
        {
            context.Canvas.Fill(Bounds, ' ');
        }

        foreach (var child in Children.OrderBy(child => child.ZIndex))
        {
            child.Paint(context);
        }
    }

    private static bool IsExpanding(Widget child)
        => child.Attributes.TryGetValue("data-expand", out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
