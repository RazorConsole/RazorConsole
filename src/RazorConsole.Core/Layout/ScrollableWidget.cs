// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;

namespace RazorConsole.Core.Layout;

public sealed class ScrollableWidget : Widget
{
    public ScrollableWidget(
        string vnodeId,
        Widget child,
        int itemsCount,
        int offset,
        int pageSize,
        bool enableEmbedded,
        char trackChar = '│',
        char thumbChar = '█',
        Style? trackStyle = null,
        Style? thumbStyle = null,
        int minThumbHeight = 1,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, [child], zIndex)
    {
        if (itemsCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemsCount), "Items count cannot be negative.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be positive.");
        }

        if (minThumbHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minThumbHeight), "Minimum thumb height must be positive.");
        }

        ItemsCount = itemsCount;
        Offset = offset;
        PageSize = pageSize;
        EnableEmbedded = enableEmbedded;
        TrackChar = trackChar;
        ThumbChar = thumbChar;
        TrackStyle = trackStyle;
        ThumbStyle = thumbStyle;
        MinThumbHeight = minThumbHeight;
    }

    public Widget Child => Children[0];

    public int ItemsCount { get; }

    public int Offset { get; }

    public int PageSize { get; }

    public bool EnableEmbedded { get; }

    public char TrackChar { get; }

    public char ThumbChar { get; }

    public Style? TrackStyle { get; }

    public Style? ThumbStyle { get; }

    public int MinThumbHeight { get; }

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        if (constraints.MaxWidth == 0 || constraints.MaxHeight == 0)
        {
            return constraints.Constrain(LayoutSize.Empty);
        }

        var scrollbarWidth = HasScrollbar ? 2 : 0;
        var childConstraints = constraints.Deflate(0, 0, scrollbarWidth, 0);
        var childSize = Child.Measure(context, childConstraints);
        return constraints.Constrain(new LayoutSize(childSize.Width + scrollbarWidth, childSize.Height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var childWidth = Math.Max(0, bounds.Width - (HasScrollbar ? 2 : 0));
        Child.Arrange(context, new LayoutRect(bounds.X, bounds.Y, childWidth, bounds.Height));
    }

    protected override void PaintCore(PaintContext context)
    {
        Child.Paint(context);

        if (!HasScrollbar || Bounds.IsEmpty)
        {
            return;
        }

        var scrollbarX = Bounds.Right - 1;
        context.Canvas.Fill(new LayoutRect(scrollbarX, Bounds.Y, 1, Bounds.Height), TrackChar, TrackStyle);

        var thumb = CalculateThumb(Bounds.Height);
        context.Canvas.Fill(new LayoutRect(scrollbarX, Bounds.Y + thumb.Top, 1, thumb.Height), ThumbChar, ThumbStyle);
    }

    private bool HasScrollbar => !EnableEmbedded && ItemsCount > PageSize;

    private (int Top, int Height) CalculateThumb(int trackHeight)
    {
        if (trackHeight <= 0 || ItemsCount <= 0)
        {
            return (0, 0);
        }

        var thumbHeight = Math.Clamp(
            (int)Math.Ceiling(trackHeight * (PageSize / (double)ItemsCount)),
            Math.Min(MinThumbHeight, trackHeight),
            trackHeight);
        var maxOffset = Math.Max(0, ItemsCount - PageSize);
        var maxTop = Math.Max(0, trackHeight - thumbHeight);
        var top = maxOffset == 0 ? 0 : (int)Math.Round(maxTop * (Offset / (double)maxOffset));
        return (top, thumbHeight);
    }
}
