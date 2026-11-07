// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Renderables;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Renderables;

internal sealed class ScrollableRenderable : IRenderable
{
    private readonly List<IRenderable> _items;
    private readonly IRenderable _compositeContent;
    private readonly int _totalItems;
    private readonly int _offset;
    private readonly int _pageSize;
    private readonly ScrollbarSettings? _scrollbarSettings;
    private readonly bool _isEmbeddedScrollbarMode;
    private readonly bool _cropLines;
    private readonly string _scrollId;
    private readonly ScrollableLayoutCoordinator _scrollableLayoutCoordinator;

    public ScrollableRenderable(
        IEnumerable<IRenderable> items,
        int totalItems,
        int offset,
        int pageSize,
        bool enableEmbeddedScrollbar,
        ScrollableLayoutCoordinator scrollableLayoutCoordinator,
        ScrollbarSettings? scrollbarSettings,
        bool cropLines = false,
        string scrollId = "")
    {
        _items = items.ToList();
        _compositeContent = new Rows(_items);
        _totalItems = totalItems;
        _offset = offset;
        _pageSize = pageSize;
        _scrollbarSettings = scrollbarSettings;

        var tables = _items.OfType<Table>();
        var panels = _items.OfType<Panel>();
        _isEmbeddedScrollbarMode = tables.Count() + panels.Count() == 1 && enableEmbeddedScrollbar;
        _scrollableLayoutCoordinator = scrollableLayoutCoordinator;
        _cropLines = cropLines;
        _scrollId = scrollId;
    }

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        var hasScrollbar = _scrollbarSettings != null;

        if (_isEmbeddedScrollbarMode)
        {
            var reserve = hasScrollbar ? 1 : 0; // For scrollbar
            var contentMeasure = _compositeContent.Measure(options, maxWidth - reserve);

            return new Measurement(contentMeasure.Min + reserve, contentMeasure.Max + reserve);
        }
        else
        {
            var reserve = hasScrollbar ? 2 : 0; // For scrollbar and space before
            var contentMeasure = _compositeContent.Measure(options, maxWidth - reserve);

            return new Measurement(contentMeasure.Min + reserve, contentMeasure.Max + reserve);
        }
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        if (_isEmbeddedScrollbarMode)
        {
            var tables = _items.OfType<Table>().ToList();
            var panels = _items.OfType<Panel>().ToList();
            var targetTable = tables.FirstOrDefault();
            var targetPanel = panels.FirstOrDefault();
            var isFirst = true;

            foreach (var item in _items)
            {
                if (!isFirst)
                {
                    yield return Segment.LineBreak;
                }

                isFirst = false;

                if (targetTable != null && ReferenceEquals(item, targetTable))
                {
                    foreach (var segment in RenderTableWithScrollBar(targetTable, options, maxWidth))
                    {
                        yield return segment;
                    }
                }
                else if (targetPanel != null && ReferenceEquals(item, targetPanel))
                {
                    foreach (var segment in RenderPanelWithScrollBar(targetPanel, options, maxWidth))
                    {
                        yield return segment;
                    }
                }
                else
                {
                    foreach (var segment in item.Render(options, maxWidth))
                    {
                        yield return segment;
                    }
                }
            }
        }
        else
        {
            foreach (var segment in RenderSideScrollBar(options, maxWidth))
            {
                yield return segment;
            }
        }
    }

    private IEnumerable<Segment> RenderPanelWithScrollBar(Panel originalPanel, RenderOptions options, int maxWidth)
    {
        var reserve = _scrollbarSettings != null ? 1 : 0;
        var tempLines = GetRenderedLines(originalPanel, options, maxWidth - reserve);
        if (tempLines.Count == 0)
        {
            yield break;
        }

        var hasBorder = originalPanel.Border != BoxBorder.None;
        var hasTitle = originalPanel.Header is not null;
        var dataStart = hasTitle || hasBorder ? 1 : 0;
        var dataEnd = hasBorder ? tempLines.Count - 1 : tempLines.Count;

        // Insert bottom border of Panel
        foreach (var line in tempLines)
        {
            var lineSegments = line.ToList();
            if (hasBorder && lineSegments.Count > 0)
            {
                lineSegments.Insert(lineSegments.Count - 1,
                    new Segment(originalPanel.Border.GetPart(BoxBorderPart.Bottom)));
                line.Clear();
                line.AddRange(lineSegments);
            }
        }

        foreach (var segment in ProcessAndRenderEmbedded(tempLines, dataStart, dataEnd, options, hasBorder))
        {
            yield return segment;
        }
    }


    private IEnumerable<Segment> RenderTableWithScrollBar(Table originalTable, RenderOptions options, int maxWidth)
    {
        var tempLines = GetRenderedLines(originalTable, options, maxWidth);
        if (tempLines.Count == 0)
        {
            yield break;
        }

        var (dataStart, dataEnd) = FindTableContentRange(tempLines, originalTable);

        foreach (var segment in ProcessAndRenderEmbedded(tempLines, dataStart, dataEnd, options, hasBorder: true))
        {
            yield return segment;
        }
    }

    private IEnumerable<Segment> RenderSideScrollBar(RenderOptions options, int maxWidth)
    {
        var reserve = _scrollbarSettings != null ? 2 : 0;
        var tempLines = GetRenderedLines(_compositeContent, options, maxWidth - reserve);
        if (tempLines.Count == 0)
        {
            yield break;
        }

        var totalContentLines = tempLines.Count;
        var actualOffset = _offset;

        if (_cropLines)
        {
            var calculatedMaxOffset = Math.Max(0, totalContentLines - _pageSize);
            actualOffset = Math.Clamp(_offset, 0, calculatedMaxOffset);
            _scrollableLayoutCoordinator.ReportMaxOffset(_scrollId, calculatedMaxOffset);

            tempLines = tempLines.Skip(actualOffset).Take(_pageSize).ToList();
        }

        if (_scrollbarSettings == null)
        {
            foreach (var segment in RenderRawLines(tempLines))
            {
                yield return segment;
            }

            yield break;
        }

        var totalItemsForScrollbar = _cropLines ? totalContentLines : _totalItems;
        var maxContentLineWidth = tempLines.Max(line => line.Sum(s => s.CellCount()));
        var scrollBarLines = CreateScrollBarLines(options, tempLines.Count, totalItemsForScrollbar, actualOffset, _pageSize);

        for (var i = 0; i < tempLines.Count; i++)
        {
            foreach (var segment in tempLines[i])
            {
                yield return segment;
            }

            var padding = maxContentLineWidth + 1 - tempLines[i].Sum(s => s.CellCount());
            if (padding > 0)
            {
                yield return new Segment(new string(' ', padding));
            }

            if (i < scrollBarLines.Count)
            {
                foreach (var sbSegment in scrollBarLines[i])
                {
                    yield return sbSegment;
                }
            }
            else
            {
                yield return new Segment(" ");
            }

            if (i < tempLines.Count - 1)
            {
                yield return Segment.LineBreak;
            }
        }
    }

    private IEnumerable<Segment> ProcessAndRenderEmbedded(
        List<SegmentLine> tempLines,
        int dataStart,
        int dataEnd,
        RenderOptions options,
        bool hasBorder)
    {
        var totalContentLines = dataEnd - dataStart;
        var actualOffset = _offset;

        if (_cropLines && totalContentLines > 0)
        {
            var calculatedMaxOffset = Math.Max(0, totalContentLines - _pageSize);
            actualOffset = Math.Clamp(_offset, 0, calculatedMaxOffset);
            _scrollableLayoutCoordinator.ReportMaxOffset(_scrollId, calculatedMaxOffset);

            var headerLines = tempLines.Take(dataStart).ToList();
            var footerLines = tempLines.Skip(dataEnd).ToList();

            var linesToTake = Math.Min(_pageSize, totalContentLines - actualOffset);
            var visibleContent = tempLines.Skip(dataStart + actualOffset).Take(linesToTake).ToList();

            tempLines = headerLines.Concat(visibleContent).Concat(footerLines).ToList();
            dataEnd = dataStart + visibleContent.Count;
        }

        if (_scrollbarSettings == null)
        {
            return RenderRawLines(tempLines);
        }

        var totalItemsForScrollbar = _cropLines ? totalContentLines : _totalItems;

        if (dataStart >= dataEnd || totalItemsForScrollbar == 0)
        {
            return RenderRawLines(tempLines);
        }

        var scrollBarHeight = Math.Max(1, dataEnd - dataStart);
        var scrollBarLines = CreateScrollBarLines(options, scrollBarHeight, totalItemsForScrollbar, actualOffset, _pageSize);

        return InjectScrollBarIntoLines(tempLines, scrollBarLines, dataStart, dataEnd, hasBorder);
    }

    private static IEnumerable<Segment> InjectScrollBarIntoLines(
        List<SegmentLine> tempLines,
        List<SegmentLine> scrollBarLines,
        int dataStart,
        int dataEnd,
        bool hasBorder)
    {
        for (var i = 0; i < tempLines.Count; i++)
        {
            var lineSegments = tempLines[i].ToList();

            if (i < dataStart || i >= dataEnd)
            {
                foreach (var seg in lineSegments)
                {
                    yield return seg;
                }
            }
            else
            {
                var scrollBarIndex = i - dataStart;
                var scrollBarSeg = GetScrollBarSegment(scrollBarLines, scrollBarIndex);

                foreach (var s in InjectScrollBar(lineSegments, scrollBarSeg, hasBorder))
                {
                    yield return s;
                }
            }

            if (i < tempLines.Count - 1)
            {
                yield return Segment.LineBreak;
            }
        }
    }

    private static IEnumerable<Segment> InjectScrollBar(List<Segment> lineSegments, Segment? scrollBarSeg,
        bool hasBorder)
    {
        var targetIndex = hasBorder ? lineSegments.Count - 2 : lineSegments.Count - 1;
        targetIndex = Math.Clamp(targetIndex, 0, lineSegments.Count - 1);

        for (var j = 0; j < targetIndex; j++)
        {
            yield return lineSegments[j];
        }

        var targetSeg = lineSegments[targetIndex];

        if (targetSeg.Text.Length > 1)
        {
            yield return new Segment(targetSeg.Text[..^1], targetSeg.Style, targetSeg.Link);
        }

        if (scrollBarSeg != null)
        {
            yield return scrollBarSeg;
        }
        else if (targetSeg.Text.Length > 0)
        {
            yield return new Segment(targetSeg.Text[^1..], targetSeg.Style, targetSeg.Link);
        }
        else
        {
            yield return new Segment(" ");
        }

        if (hasBorder && lineSegments.Count > 0)
        {
            yield return lineSegments[^1];
        }
    }

    private static Segment? GetScrollBarSegment(List<SegmentLine> scrollBarLines, int index)
    {
        if (index >= 0 && index < scrollBarLines.Count && scrollBarLines[index].Count > 0)
        {
            return scrollBarLines[index][0];
        }

        return null;
    }

    private (int start, int end) FindTableContentRange(List<SegmentLine> lines, Table table)
    {
        if (lines.Count == 0)
        {
            return (0, 0);
        }

        string? searchMarker = null;
        var isTitleSearch = false;
        var lineOffset = 1;

        if (table.Border != TableBorder.None)
        {
            if (table.ShowHeaders)
            {
                searchMarker = table.Border.GetPart(TableBorderPart.HeaderBottomLeft) +
                               table.Border.GetPart(TableBorderPart.HeaderBottom);
            }
            else
            {
                searchMarker = table.Border.GetPart(TableBorderPart.HeaderTopLeft);
                lineOffset = 2;
            }
        }
        else if (table.Title != null)
        {
            searchMarker = table.Title.Text;
            isTitleSearch = true;
            lineOffset = 2;
        }

        var top = 0;
        if (!string.IsNullOrEmpty(searchMarker))
        {
            for (var i = 0; i < lines.Count - 1; i++)
            {
                var lineText = GetLineText(lines[i]);
                if (string.IsNullOrEmpty(lineText))
                {
                    continue;
                }

                if (isTitleSearch)
                {
                    var endOfTitleIndex = FindLastLineOfPhrase(lines, i, searchMarker);
                    if (endOfTitleIndex != i)
                    {
                        top = endOfTitleIndex + lineOffset;
                        break;
                    }
                }
                else if (lineText.StartsWith(searchMarker))
                {
                    top = i + lineOffset;
                    break;
                }
            }
        }

        var bottom = table.Border != TableBorder.None ? lines.Count - 1 : lines.Count;
        return top >= bottom ? (0, 0) : (top, bottom);
    }

    private static int FindLastLineOfPhrase(List<SegmentLine> textLines, int startIndex, string phrase)
    {
        var cleanPhrase = Markup.Remove(phrase);
        var targetSignature = cleanPhrase.Replace(" ", "").Replace("\t", "");
        var accumulatedText = new StringBuilder();

        for (var i = startIndex; i < textLines.Count; i++)
        {
            var lineText = GetLineText(textLines[i]);
            accumulatedText.Append(lineText.Replace(" ", "").Replace("\t", ""));

            if (accumulatedText.ToString().Contains(targetSignature))
            {
                return i;
            }
        }

        return startIndex;
    }

    private static string GetLineText(SegmentLine line) => string.Concat(line.Select(s => s.Text));

    private static List<SegmentLine> GetRenderedLines(IRenderable renderable, RenderOptions options, int maxWidth) =>
        Segment.SplitLines(renderable.Render(options, maxWidth).ToList());

    private static IEnumerable<Segment> RenderRawLines(List<SegmentLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            foreach (var segment in lines[i])
            {
                yield return segment;
            }

            if (i < lines.Count - 1)
            {
                yield return Segment.LineBreak;
            }
        }
    }

    private List<SegmentLine> CreateScrollBarLines(RenderOptions options, int height, int totalItems, int offset,
        int pageSize)
    {
        var scrollBar = new ScrollBarRenderable(
            totalItems, offset, pageSize, height,
            _scrollbarSettings!.TrackChar,
            _scrollbarSettings.ThumbChar,
            _scrollbarSettings.TrackColor,
            _scrollbarSettings.ThumbColor,
            _scrollbarSettings.MinThumbHeight
        );
        return Segment.SplitLines(scrollBar.Render(options, 1).ToList());
    }
}
