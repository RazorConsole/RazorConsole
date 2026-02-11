// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Renderables;

/// <summary>
/// Specifies the main axis direction for a flex layout.
/// </summary>
public enum FlexDirection
{
    /// <summary>Items are laid out horizontally, left to right.</summary>
    Row,

    /// <summary>Items are laid out vertically, top to bottom.</summary>
    Column,
}

/// <summary>
/// Specifies how free space is distributed along the main axis.
/// </summary>
public enum FlexJustify
{
    /// <summary>Items are packed toward the start of the main axis.</summary>
    Start,

    /// <summary>Items are packed toward the end of the main axis.</summary>
    End,

    /// <summary>Items are centered along the main axis.</summary>
    Center,

    /// <summary>Items are evenly distributed; first item is at the start, last item is at the end.</summary>
    SpaceBetween,

    /// <summary>Items are evenly distributed with equal space around each item.</summary>
    SpaceAround,

    /// <summary>Items are evenly distributed with equal space between and at the edges.</summary>
    SpaceEvenly,
}

/// <summary>
/// Specifies how items are aligned along the cross axis.
/// </summary>
public enum FlexAlign
{
    /// <summary>Items are aligned to the start of the cross axis.</summary>
    Start,

    /// <summary>Items are aligned to the end of the cross axis.</summary>
    End,

    /// <summary>Items are centered along the cross axis.</summary>
    Center,

    /// <summary>Items are stretched to fill the cross axis.</summary>
    Stretch,
}

/// <summary>
/// Specifies whether flex items are forced onto one line or can wrap.
/// </summary>
public enum FlexWrap
{
    /// <summary>All items are laid out in a single line.</summary>
    NoWrap,

    /// <summary>Items wrap onto additional lines when they exceed the available space.</summary>
    Wrap,
}

/// <summary>
/// A renderable that lays out child items using a CSS-like flexbox model.
/// Supports configurable direction, justification, alignment, wrapping, and gap.
/// </summary>
public sealed class FlexBoxRenderable : IRenderable
{
    private readonly IReadOnlyList<IRenderable> _items;

    /// <summary>
    /// Gets the main axis direction.
    /// </summary>
    public FlexDirection Direction { get; }

    /// <summary>
    /// Gets the main-axis justification strategy.
    /// </summary>
    public FlexJustify Justify { get; }

    /// <summary>
    /// Gets the cross-axis alignment strategy.
    /// </summary>
    public FlexAlign Align { get; }

    /// <summary>
    /// Gets the wrapping behavior.
    /// </summary>
    public FlexWrap Wrap { get; }

    /// <summary>
    /// Gets the gap (in characters/rows) between items along the main axis.
    /// </summary>
    public int Gap { get; }

    /// <summary>
    /// Gets the explicit width constraint, or null to use the available width.
    /// </summary>
    public int? Width { get; }

    /// <summary>
    /// Gets the explicit height constraint, or null to use the available height.
    /// </summary>
    public int? Height { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlexBoxRenderable"/> class.
    /// </summary>
    /// <param name="items">The child renderables to lay out.</param>
    /// <param name="direction">Main axis direction.</param>
    /// <param name="justify">Main-axis distribution.</param>
    /// <param name="align">Cross-axis alignment.</param>
    /// <param name="wrap">Whether items wrap.</param>
    /// <param name="gap">Spacing between items (characters for Row, lines for Column).</param>
    /// <param name="width">Explicit width constraint.</param>
    /// <param name="height">Explicit height constraint.</param>
    public FlexBoxRenderable(
        IReadOnlyList<IRenderable> items,
        FlexDirection direction = FlexDirection.Row,
        FlexJustify justify = FlexJustify.Start,
        FlexAlign align = FlexAlign.Start,
        FlexWrap wrap = FlexWrap.NoWrap,
        int gap = 0,
        int? width = null,
        int? height = null)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        Direction = direction;
        Justify = justify;
        Align = align;
        Wrap = wrap;
        Gap = Math.Max(0, gap);
        Width = width;
        Height = height;
    }

    /// <inheritdoc/>
    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        var effectiveWidth = Width.HasValue ? Math.Min(Width.Value, maxWidth) : maxWidth;

        if (_items.Count == 0)
        {
            return new Measurement(0, 0);
        }

        if (Direction == FlexDirection.Row)
        {
            return MeasureRow(options, effectiveWidth);
        }

        return MeasureColumn(options, effectiveWidth);
    }

    /// <inheritdoc/>
    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var effectiveWidth = Width.HasValue ? Math.Min(Width.Value, maxWidth) : maxWidth;

        if (_items.Count == 0)
        {
            yield break;
        }

        if (Direction == FlexDirection.Row)
        {
            foreach (var segment in RenderRow(options, effectiveWidth))
            {
                yield return segment;
            }
        }
        else
        {
            foreach (var segment in RenderColumn(options, effectiveWidth))
            {
                yield return segment;
            }
        }
    }

    #region Row layout

    private Measurement MeasureRow(RenderOptions options, int maxWidth)
    {
        var minWidth = 0;
        var totalMax = 0;

        for (var i = 0; i < _items.Count; i++)
        {
            var m = _items[i].Measure(options, maxWidth);
            minWidth = Math.Max(minWidth, m.Min);
            totalMax += m.Max;
        }

        var gapTotal = Gap * Math.Max(0, _items.Count - 1);
        totalMax += gapTotal;

        return new Measurement(Math.Min(minWidth, maxWidth), Math.Min(totalMax, maxWidth));
    }

    private IEnumerable<Segment> RenderRow(RenderOptions options, int maxWidth)
    {
        var lines = PartitionIntoFlexLines(options, maxWidth);
        var isFirst = true;

        foreach (var line in lines)
        {
            if (!isFirst)
            {
                yield return Segment.LineBreak;
            }

            isFirst = false;

            foreach (var segment in RenderRowLine(line, options, maxWidth))
            {
                yield return segment;
            }
        }
    }

    private List<List<IRenderable>> PartitionIntoFlexLines(RenderOptions options, int maxWidth)
    {
        if (Wrap == FlexWrap.NoWrap)
        {
            return new List<List<IRenderable>> { new List<IRenderable>(_items) };
        }

        var lines = new List<List<IRenderable>>();
        var currentLine = new List<IRenderable>();
        var currentWidth = 0;

        for (var i = 0; i < _items.Count; i++)
        {
            var itemWidth = _items[i].Measure(options, maxWidth).Max;
            var gapBefore = currentLine.Count > 0 ? Gap : 0;

            if (currentLine.Count > 0 && currentWidth + gapBefore + itemWidth > maxWidth)
            {
                lines.Add(currentLine);
                currentLine = new List<IRenderable> { _items[i] };
                currentWidth = itemWidth;
            }
            else
            {
                currentWidth += gapBefore + itemWidth;
                currentLine.Add(_items[i]);
            }
        }

        if (currentLine.Count > 0)
        {
            lines.Add(currentLine);
        }

        return lines;
    }

    private IEnumerable<Segment> RenderRowLine(List<IRenderable> lineItems, RenderOptions options, int maxWidth)
    {
        if (lineItems.Count == 0)
        {
            yield break;
        }

        // 1. Render each item and collect their segment grids
        var itemGrids = new List<List<SegmentLine>>(lineItems.Count);
        var itemWidths = new List<int>(lineItems.Count);

        for (var i = 0; i < lineItems.Count; i++)
        {
            var segments = lineItems[i].Render(options, maxWidth);
            var grid = Segment.SplitLines(segments);
            var width = grid.Count > 0 ? grid.Max(line => Segment.CellCount(line)) : 0;
            itemGrids.Add(grid);
            itemWidths.Add(width);
        }

        // 2. Determine line height (max across items)
        var lineHeight = itemGrids.Max(g => g.Count);
        if (lineHeight == 0)
        {
            yield break;
        }

        // 3. Calculate spacing based on Justify
        var totalContentWidth = 0;
        for (var i = 0; i < itemWidths.Count; i++)
        {
            totalContentWidth += itemWidths[i];
        }

        var totalGaps = Gap * Math.Max(0, lineItems.Count - 1);
        totalContentWidth += totalGaps;

        var freeSpace = Math.Max(0, maxWidth - totalContentWidth);
        var spacings = CalculateSpacings(freeSpace, lineItems.Count);

        // 4. Normalize each item grid to have uniform height, applying cross-axis alignment
        for (var i = 0; i < itemGrids.Count; i++)
        {
            NormalizeCrossAxis(itemGrids[i], itemWidths[i], lineHeight);
        }

        // 5. Compose rows by interleaving item columns
        for (var row = 0; row < lineHeight; row++)
        {
            // Leading space (from spacing[0] — before first item)
            if (spacings.LeadingSpace > 0)
            {
                yield return Segment.Padding(spacings.LeadingSpace);
            }

            for (var itemIdx = 0; itemIdx < itemGrids.Count; itemIdx++)
            {
                // Emit item's row
                var itemGrid = itemGrids[itemIdx];
                if (row < itemGrid.Count)
                {
                    foreach (var seg in itemGrid[row])
                    {
                        yield return seg;
                    }

                    // Pad to item width if the line is shorter
                    var lineWidth = Segment.CellCount(itemGrid[row]);
                    if (lineWidth < itemWidths[itemIdx])
                    {
                        yield return Segment.Padding(itemWidths[itemIdx] - lineWidth);
                    }
                }
                else
                {
                    yield return Segment.Padding(itemWidths[itemIdx]);
                }

                // Gap between items
                if (itemIdx < itemGrids.Count - 1)
                {
                    var spaceBetween = Gap + spacings.BetweenSpace;
                    if (spaceBetween > 0)
                    {
                        yield return Segment.Padding(spaceBetween);
                    }
                }
            }

            // Trailing space
            if (spacings.TrailingSpace > 0)
            {
                yield return Segment.Padding(spacings.TrailingSpace);
            }

            if (row < lineHeight - 1)
            {
                yield return Segment.LineBreak;
            }
        }
    }

    private void NormalizeCrossAxis(List<SegmentLine> grid, int itemWidth, int lineHeight)
    {
        // Pad lines to itemWidth
        foreach (var line in grid)
        {
            var w = Segment.CellCount(line);
            if (w < itemWidth)
            {
                line.Add(Segment.Padding(itemWidth - w));
            }
        }

        var blankLine = new SegmentLine { Segment.Padding(itemWidth) };

        var missingRows = lineHeight - grid.Count;
        if (missingRows <= 0)
        {
            return;
        }

        switch (Align)
        {
            case FlexAlign.Start:
            case FlexAlign.Stretch:
                // Pad at the bottom
                for (var i = 0; i < missingRows; i++)
                {
                    grid.Add(new SegmentLine { Segment.Padding(itemWidth) });
                }

                break;

            case FlexAlign.End:
                // Pad at the top
                for (var i = 0; i < missingRows; i++)
                {
                    grid.Insert(0, new SegmentLine { Segment.Padding(itemWidth) });
                }

                break;

            case FlexAlign.Center:
                var top = missingRows / 2;
                var bottom = missingRows - top;
                for (var i = 0; i < top; i++)
                {
                    grid.Insert(0, new SegmentLine { Segment.Padding(itemWidth) });
                }

                for (var i = 0; i < bottom; i++)
                {
                    grid.Add(new SegmentLine { Segment.Padding(itemWidth) });
                }

                break;

            default:
                throw new NotSupportedException($"Unknown FlexAlign: {Align}");
        }
    }

    #endregion

    #region Column layout

    private Measurement MeasureColumn(RenderOptions options, int maxWidth)
    {
        var minWidth = 0;
        var maxNeeded = 0;

        for (var i = 0; i < _items.Count; i++)
        {
            var m = _items[i].Measure(options, maxWidth);
            minWidth = Math.Max(minWidth, m.Min);
            maxNeeded = Math.Max(maxNeeded, m.Max);
        }

        return new Measurement(Math.Min(minWidth, maxWidth), Math.Min(maxNeeded, maxWidth));
    }

    private IEnumerable<Segment> RenderColumn(RenderOptions options, int maxWidth)
    {
        // 1. Render each item into segment grids
        var itemGrids = new List<List<SegmentLine>>(_items.Count);
        var itemHeights = new List<int>(_items.Count);

        for (var i = 0; i < _items.Count; i++)
        {
            var segments = _items[i].Render(options, maxWidth);
            var grid = Segment.SplitLines(segments);
            itemGrids.Add(grid);
            itemHeights.Add(grid.Count);
        }

        // 2. Calculate total content height including gaps
        var totalContentHeight = 0;
        for (var i = 0; i < itemHeights.Count; i++)
        {
            totalContentHeight += itemHeights[i];
        }

        totalContentHeight += Gap * Math.Max(0, _items.Count - 1);

        // 3. Calculate vertical spacing based on Justify
        var totalHeight = Height ?? totalContentHeight;
        var freeSpace = Math.Max(0, totalHeight - totalContentHeight);
        var spacings = CalculateSpacings(freeSpace, _items.Count);

        // 4. Emit rows with vertical spacing
        var isFirstItem = true;

        // Leading blank lines
        for (var i = 0; i < spacings.LeadingSpace; i++)
        {
            if (!isFirstItem)
            {
                yield return Segment.LineBreak;
            }

            isFirstItem = false;
            yield return Segment.Padding(maxWidth);
        }

        for (var itemIdx = 0; itemIdx < itemGrids.Count; itemIdx++)
        {
            // Gap / between-space before this item (except the first)
            if (itemIdx > 0)
            {
                var verticalGap = Gap + spacings.BetweenSpace;
                for (var g = 0; g < verticalGap; g++)
                {
                    yield return Segment.LineBreak;
                    yield return Segment.Padding(maxWidth);
                }
            }

            var grid = itemGrids[itemIdx];

            for (var row = 0; row < grid.Count; row++)
            {
                if (!isFirstItem || row > 0)
                {
                    yield return Segment.LineBreak;
                }

                isFirstItem = false;

                // Apply horizontal alignment on cross axis
                var lineWidth = Segment.CellCount(grid[row]);
                var hPad = Math.Max(0, maxWidth - lineWidth);

                switch (Align)
                {
                    case FlexAlign.Start:
                    case FlexAlign.Stretch:
                        foreach (var seg in grid[row])
                        {
                            yield return seg;
                        }

                        if (hPad > 0)
                        {
                            yield return Segment.Padding(hPad);
                        }

                        break;

                    case FlexAlign.End:
                        if (hPad > 0)
                        {
                            yield return Segment.Padding(hPad);
                        }

                        foreach (var seg in grid[row])
                        {
                            yield return seg;
                        }

                        break;

                    case FlexAlign.Center:
                        var left = hPad / 2;
                        var right = hPad - left;
                        if (left > 0)
                        {
                            yield return Segment.Padding(left);
                        }

                        foreach (var seg in grid[row])
                        {
                            yield return seg;
                        }

                        if (right > 0)
                        {
                            yield return Segment.Padding(right);
                        }

                        break;

                    default:
                        throw new NotSupportedException($"Unknown FlexAlign: {Align}");
                }
            }
        }

        // Trailing blank lines
        for (var i = 0; i < spacings.TrailingSpace; i++)
        {
            yield return Segment.LineBreak;
            yield return Segment.Padding(maxWidth);
        }
    }

    #endregion

    #region Spacing calculation

    private readonly record struct FlexSpacing(int LeadingSpace, int BetweenSpace, int TrailingSpace);

    private FlexSpacing CalculateSpacings(int freeSpace, int itemCount)
    {
        if (freeSpace <= 0 || itemCount == 0)
        {
            return new FlexSpacing(0, 0, 0);
        }

        return Justify switch
        {
            FlexJustify.Start => new FlexSpacing(0, 0, freeSpace),
            FlexJustify.End => new FlexSpacing(freeSpace, 0, 0),
            FlexJustify.Center => new FlexSpacing(freeSpace / 2, 0, freeSpace - freeSpace / 2),
            FlexJustify.SpaceBetween => itemCount > 1
                ? new FlexSpacing(0, freeSpace / (itemCount - 1), 0)
                : new FlexSpacing(freeSpace / 2, 0, freeSpace - freeSpace / 2),
            FlexJustify.SpaceAround => CalculateSpaceAround(freeSpace, itemCount),
            FlexJustify.SpaceEvenly => CalculateSpaceEvenly(freeSpace, itemCount),
            _ => new FlexSpacing(0, 0, 0),
        };
    }

    private static FlexSpacing CalculateSpaceAround(int freeSpace, int itemCount)
    {
        // Each item gets equal space on both sides. The edge space is half of the between space.
        var totalSlots = itemCount * 2; // each item has space on left and right
        var slotSize = freeSpace / totalSlots;
        var edgeSpace = slotSize;
        var betweenSpace = slotSize * 2 / Math.Max(1, itemCount - 1);

        if (itemCount <= 1)
        {
            return new FlexSpacing(freeSpace / 2, 0, freeSpace - freeSpace / 2);
        }

        // Recalculate: edge = freeSpace / (2 * itemCount), between = freeSpace / itemCount
        edgeSpace = freeSpace / (2 * itemCount);
        betweenSpace = freeSpace / itemCount;

        // Distribute any remainder
        return new FlexSpacing(edgeSpace, betweenSpace / Math.Max(1, itemCount - 1) + (betweenSpace > 0 ? 0 : 0), edgeSpace);
    }

    private static FlexSpacing CalculateSpaceEvenly(int freeSpace, int itemCount)
    {
        // Equal space between all items and at the edges
        var slots = itemCount + 1;
        var slotSize = freeSpace / slots;
        return new FlexSpacing(slotSize, slotSize, slotSize);
    }

    #endregion
}
