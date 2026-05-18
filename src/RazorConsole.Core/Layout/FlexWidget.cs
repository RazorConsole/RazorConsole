// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Renderables;

namespace RazorConsole.Core.Layout;

public sealed class FlexWidget : Widget
{
    public FlexWidget(
        string vnodeId,
        IReadOnlyList<Widget> children,
        FlexDirection direction = FlexDirection.Row,
        FlexJustify justify = FlexJustify.Start,
        FlexAlign align = FlexAlign.Start,
        FlexWrap wrap = FlexWrap.NoWrap,
        int gap = 0,
        bool expand = false,
        bool fillWidth = false,
        bool fillHeight = false,
        int? width = null,
        int? height = null,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, children, zIndex)
    {
        if (gap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gap), "Gap cannot be negative.");
        }

        if (width is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive when specified.");
        }

        if (height is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive when specified.");
        }

        Direction = direction;
        Justify = justify;
        Align = align;
        Wrap = wrap;
        Gap = gap;
        Expand = expand;
        FillWidth = fillWidth || (expand && direction == FlexDirection.Row);
        FillHeight = fillHeight || (expand && direction == FlexDirection.Column);
        Width = width;
        Height = height;
    }

    public FlexDirection Direction { get; }

    public FlexJustify Justify { get; }

    public FlexAlign Align { get; }

    public FlexWrap Wrap { get; }

    public int Gap { get; }

    public bool Expand { get; }

    public bool FillWidth { get; }

    public bool FillHeight { get; }

    public int? Width { get; }

    public int? Height { get; }

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        if (Children.Count == 0)
        {
            return constraints.Constrain(LayoutSize.Empty);
        }

        var flowChildren = Children.Where(child => !IsAbsolutePositioned(child)).ToArray();
        var childConstraints = new BoxConstraints(0, constraints.MaxWidth, 0, constraints.MaxHeight);
        var width = 0;
        var height = 0;

        foreach (var child in Children)
        {
            var childSize = child.Measure(context, childConstraints);
            if (IsAbsolutePositioned(child))
            {
                continue;
            }

            if (Direction == FlexDirection.Row)
            {
                width += childSize.Width;
                height = Math.Max(height, childSize.Height);
            }
            else
            {
                width = Math.Max(width, childSize.Width);
                height += childSize.Height;
            }
        }

        var totalGap = Math.Max(0, flowChildren.Length - 1) * Gap;
        if (Direction == FlexDirection.Row)
        {
            width += totalGap;
        }
        else
        {
            height += totalGap;
        }

        width = Width ?? (FillWidth ? constraints.MaxWidth : width);
        height = Height ?? (FillHeight ? constraints.MaxHeight : height);

        return constraints.Constrain(new LayoutSize(width, height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        if (Direction == FlexDirection.Row)
        {
            ArrangeRow(context, bounds);
        }
        else
        {
            ArrangeColumn(context, bounds);
        }
    }

    protected override void PaintCore(PaintContext context)
    {
        foreach (var child in Children.OrderBy(child => child.ZIndex))
        {
            child.Paint(context);
        }
    }

    private void ArrangeRow(LayoutContext context, LayoutRect bounds)
    {
        var flowChildren = Children.Where(child => !IsAbsolutePositioned(child)).ToArray();
        var mainSizes = ResolveMainSizes(flowChildren, bounds.Width, child => child.DesiredSize.Width, FillsMainAxis);
        var occupiedWidth = mainSizes.Sum() + Math.Max(0, flowChildren.Length - 1) * Gap;
        var x = bounds.X + ResolveJustifyOffset(bounds.Width, occupiedWidth);
        var spacing = ResolveGap(flowChildren.Length, bounds.Width, occupiedWidth);
        var flowIndex = 0;

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (IsAbsolutePositioned(child))
            {
                ArrangeAbsoluteChild(context, child, bounds);
                continue;
            }

            var childWidth = Math.Min(mainSizes[flowIndex++], Math.Max(0, bounds.Right - x));
            var childHeight = FillsCrossAxis(child) || Align == FlexAlign.Stretch ? bounds.Height : Math.Min(child.DesiredSize.Height, bounds.Height);
            var y = bounds.Y + ResolveCrossOffset(bounds.Height, childHeight);
            child.Arrange(context, new LayoutRect(x, y, childWidth, childHeight));
            x += childWidth + spacing;
        }
    }

    private void ArrangeColumn(LayoutContext context, LayoutRect bounds)
    {
        var flowChildren = Children.Where(child => !IsAbsolutePositioned(child)).ToArray();
        var mainSizes = ResolveMainSizes(flowChildren, bounds.Height, child => child.DesiredSize.Height, FillsMainAxis);
        var occupiedHeight = mainSizes.Sum() + Math.Max(0, flowChildren.Length - 1) * Gap;
        var y = bounds.Y + ResolveJustifyOffset(bounds.Height, occupiedHeight);
        var spacing = ResolveGap(flowChildren.Length, bounds.Height, occupiedHeight);
        var flowIndex = 0;

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (IsAbsolutePositioned(child))
            {
                ArrangeAbsoluteChild(context, child, bounds);
                continue;
            }

            var childHeight = Math.Min(mainSizes[flowIndex++], Math.Max(0, bounds.Bottom - y));
            var childWidth = FillsCrossAxis(child) || Align == FlexAlign.Stretch ? bounds.Width : Math.Min(child.DesiredSize.Width, bounds.Width);
            var x = bounds.X + ResolveCrossOffset(bounds.Width, childWidth);
            child.Arrange(context, new LayoutRect(x, y, childWidth, childHeight));
            y += childHeight + spacing;
        }
    }

    private int[] ResolveMainSizes(
        IReadOnlyList<Widget> children,
        int availableMainSize,
        Func<Widget, int> getDesiredMainSize,
        Func<Widget, bool> fillsMainAxis)
    {
        var expandingChildren = children.Where(fillsMainAxis).ToArray();
        var totalGap = Math.Max(0, children.Count - 1) * Gap;
        var fixedSize = children
            .Where(child => !fillsMainAxis(child))
            .Sum(getDesiredMainSize);
        var remainingSize = Math.Max(0, availableMainSize - fixedSize - totalGap);
        var expandSize = expandingChildren.Length == 0 ? 0 : remainingSize / expandingChildren.Length;
        var expandRemainder = expandingChildren.Length == 0 ? 0 : remainingSize % expandingChildren.Length;

        return children
            .Select(child => fillsMainAxis(child)
                ? expandSize + (expandRemainder-- > 0 ? 1 : 0)
                : getDesiredMainSize(child))
            .ToArray();
    }

    private int ResolveJustifyOffset(int available, int occupied)
        => Justify switch
        {
            FlexJustify.Center => Math.Max(0, (available - occupied) / 2),
            FlexJustify.End => Math.Max(0, available - occupied),
            _ => 0,
        };

    private int ResolveGap(int childCount, int available, int occupied)
    {
        if (childCount <= 1)
        {
            return Gap;
        }

        return Justify switch
        {
            FlexJustify.SpaceBetween => Gap + Math.Max(0, available - occupied) / (childCount - 1),
            FlexJustify.SpaceAround => Gap + Math.Max(0, available - occupied) / childCount,
            FlexJustify.SpaceEvenly => Gap + Math.Max(0, available - occupied) / (childCount + 1),
            _ => Gap,
        };
    }

    private int ResolveCrossOffset(int available, int childSize)
        => Align switch
        {
            FlexAlign.Center => Math.Max(0, (available - childSize) / 2),
            FlexAlign.End => Math.Max(0, available - childSize),
            _ => 0,
        };

    private bool FillsMainAxis(Widget child)
        => Direction == FlexDirection.Row
            ? IsTruthy(child, "data-expand") || IsTruthy(child, "data-fill-width")
            : IsTruthy(child, "data-expand") || IsTruthy(child, "data-fill-height");

    private bool FillsCrossAxis(Widget child)
        => Direction == FlexDirection.Row
            ? IsTruthy(child, "data-fill-height")
            : IsTruthy(child, "data-expand") || IsTruthy(child, "data-fill-width");

    private static bool IsTruthy(Widget child, string name)
        => child.Attributes.TryGetValue(name, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsAbsolutePositioned(Widget child)
        => child.Attributes.TryGetValue("position", out var value)
            && string.Equals(value, "absolute", StringComparison.OrdinalIgnoreCase);

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

    private static int? TryGetIntAttribute(Widget child, string name)
    {
        if (!child.Attributes.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, out var value) ? value : null;
    }
}
