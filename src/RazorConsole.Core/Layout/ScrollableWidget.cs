// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering;
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
        bool showScrollbar = true,
        bool cropLines = false,
        bool autoPageSize = false,
        ScrollableLayoutCoordinator? layoutCoordinator = null,
        string? scrollId = null,
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
        ShowScrollbar = showScrollbar;
        CropLines = cropLines;
        AutoPageSize = autoPageSize;
        LayoutCoordinator = layoutCoordinator;
        ScrollId = scrollId;
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

    public bool ShowScrollbar { get; }

    public bool CropLines { get; }

    public bool AutoPageSize { get; }

    public ScrollableLayoutCoordinator? LayoutCoordinator { get; }

    public string? ScrollId { get; }

    public char TrackChar { get; }

    public char ThumbChar { get; }

    public Style? TrackStyle { get; }

    public Style? ThumbStyle { get; }

    public int MinThumbHeight { get; }

    private int VisiblePageSize { get; set; }

    private int ContentHeight { get; set; }

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        if (constraints.MaxWidth == 0 || constraints.MaxHeight == 0)
        {
            return constraints.Constrain(LayoutSize.Empty);
        }

        var reserveScrollbar = ShowScrollbar && !EnableEmbedded;
        var scrollbarWidth = reserveScrollbar ? 2 : 0;
        var childConstraints = CropLines
            ? new BoxConstraints(0, Math.Max(0, constraints.MaxWidth - scrollbarWidth), 0, int.MaxValue / 4)
            : constraints.Deflate(0, 0, scrollbarWidth, 0);
        var childSize = Child.Measure(context, childConstraints);
        ContentHeight = childSize.Height;
        VisiblePageSize = ResolvePageSize(constraints.MaxHeight);
        var height = CropLines ? Math.Min(ContentHeight, VisiblePageSize) : childSize.Height;
        return constraints.Constrain(new LayoutSize(childSize.Width + scrollbarWidth, height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        VisiblePageSize = ResolvePageSize(bounds.Height);
        var actualOffset = Math.Clamp(Offset, 0, MaxOffset);
        LayoutCoordinator?.ReportMaxOffset(ScrollId ?? string.Empty, MaxOffset);
        var childWidth = Math.Max(0, bounds.Width - (HasScrollbar ? 2 : 0));
        var childY = CropLines ? bounds.Y - actualOffset : bounds.Y;
        var childHeight = CropLines ? Math.Max(ContentHeight, bounds.Height + actualOffset) : bounds.Height;
        Child.Arrange(context, new LayoutRect(bounds.X, childY, childWidth, childHeight));
    }

    protected override void PaintCore(PaintContext context)
    {
        if (CropLines)
        {
            using var _ = context.Canvas.PushClip(Bounds);
            Child.Paint(context);
        }
        else
        {
            Child.Paint(context);
        }

        if (!HasScrollbar || Bounds.IsEmpty)
        {
            return;
        }

        var scrollbarX = Bounds.Right - 1;
        context.Canvas.Fill(new LayoutRect(scrollbarX, Bounds.Y, 1, Bounds.Height), TrackChar, TrackStyle);

        var thumb = CalculateThumb(Bounds.Height);
        context.Canvas.Fill(new LayoutRect(scrollbarX, Bounds.Y + thumb.Top, 1, thumb.Height), ThumbChar, ThumbStyle);
    }

    private bool HasScrollbar => ShowScrollbar && !EnableEmbedded && (CropLines ? ContentHeight > VisiblePageSize : ItemsCount > PageSize);

    private int MaxOffset => Math.Max(0, ContentHeight - VisiblePageSize);

    private int ResolvePageSize(int availableHeight)
        => Math.Max(1, AutoPageSize ? availableHeight : PageSize);

    private (int Top, int Height) CalculateThumb(int trackHeight)
    {
        var totalItems = CropLines ? ContentHeight : ItemsCount;
        var pageSize = CropLines ? VisiblePageSize : PageSize;

        if (trackHeight <= 0 || totalItems <= 0)
        {
            return (0, 0);
        }

        var thumbHeight = Math.Clamp(
            (int)Math.Ceiling(trackHeight * (pageSize / (double)totalItems)),
            Math.Min(MinThumbHeight, trackHeight),
            trackHeight);
        var maxOffset = CropLines ? MaxOffset : Math.Max(0, ItemsCount - PageSize);
        var maxTop = Math.Max(0, trackHeight - thumbHeight);
        var top = maxOffset == 0 ? 0 : (int)Math.Round(maxTop * (Offset / (double)maxOffset));
        return (top, thumbHeight);
    }
}
