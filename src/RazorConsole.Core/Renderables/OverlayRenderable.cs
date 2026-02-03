// Copyright (c) RazorConsole. All rights reserved.

using System.Buffers;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Renderables;

public sealed class OverlayRenderable(IRenderable background, IEnumerable<OverlayItem> overlays) : IRenderable
{
    private readonly List<OverlayItem> _sortedOverlays = overlays.OrderBy(o => o.ZIndex).ToList();
    private readonly Dictionary<int, List<OverlayPosition>> _overlayMapCache = new();

    public Measurement Measure(RenderOptions options, int maxWidth) => background.Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var bgSegments = background.Render(options, maxWidth);
        var canvas = Segment.SplitLines(bgSegments);

        _overlayMapCache.Clear();

        foreach (var overlay in _sortedOverlays)
        {
            var (widthToRender, finalLeft) = overlay switch
            {
                { IsCentered: true } => CalculateCenteredPosition(overlay, options, maxWidth),
                // CSS-like stretching
                { Left: { } l, Right: { } r } => (maxWidth - l - r, l),
                { Right: { } r } => CalculateRightPosition(overlay, r, options, maxWidth),
                _ => (maxWidth - (overlay.Left ?? 0), overlay.Left ?? 0)
            };

            (int Width, int Left) CalculateCenteredPosition(OverlayItem item, RenderOptions opt, int maxW)
            {
                var measurement = item.Renderable.Measure(opt, maxW);
                int desiredWidth = Math.Min(measurement.Max, maxW);

                int left = (maxW - desiredWidth) / 2;
                return (desiredWidth, left);
            }

            (int Width, int Left) CalculateRightPosition(OverlayItem item, int r, RenderOptions opt, int maxW)
            {
                var constraint = Math.Max(0, maxW - r);
                var width = item.Renderable.Measure(opt, constraint).Max;
                return (width, maxW - r - width);
            }

            var lines = Segment.SplitLines(
                overlay.Renderable.Render(options, Math.Max(0, widthToRender))
            );
            int finalTop = overlay switch
            {
                { IsCentered: true } => Math.Max(0, (canvas.Count - lines.Count) / 2),
                { Top: { } t } => t,
                { Bottom: { } b } => Math.Max(0, canvas.Count - b - lines.Count),
                _ => 0
            };

            for (int i = 0; i < lines.Count; i++)
            {
                int targetY = finalTop + i;
                if (targetY < 0)
                {
                    continue;
                }

                if (!_overlayMapCache.TryGetValue(targetY, out var positions))
                {
                    positions = new List<OverlayPosition>(4);
                    _overlayMapCache[targetY] = positions;
                }

                positions.Add(new OverlayPosition(lines[i], finalLeft));
            }
        }

        var cellPool = ArrayPool<Cell>.Shared;
        Cell[]? lineBuffer = null;

        try
        {
            lineBuffer = cellPool.Rent(maxWidth);

            foreach (var (y, overlayPositions) in _overlayMapCache)
            {
                // If overlay is over the canvas
                while (canvas.Count <= y)
                {
                    canvas.Add(new SegmentLine());
                }

                // Clean up array
                Array.Fill(lineBuffer, new Cell(' ', Style.Plain), 0, maxWidth);

                int backgroundWidth = ToBuffer(canvas[y], lineBuffer, maxWidth);
                int actualWidth = backgroundWidth;

                foreach (var overlayPos in overlayPositions)
                {
                    int endX = MergeToBuffer(overlayPos.Line, lineBuffer, overlayPos.Left, maxWidth);
                    if (endX > actualWidth)
                    {
                        actualWidth = endX;
                    }
                }

                canvas[y] = FromBuffer(lineBuffer, actualWidth);
            }
        }
        finally
        {
            if (lineBuffer != null)
            {
                cellPool.Return(lineBuffer);
            }
        }

        for (int i = 0; i < canvas.Count; i++)
        {
            foreach (var segment in canvas[i])
            {
                yield return segment;
            }

            if (i < canvas.Count - 1)
            {
                yield return Segment.LineBreak;
            }
        }
    }

    private static int ToBuffer(IReadOnlyList<Segment> segments, Cell[] buffer, int maxWidth)
    {
        int cursor = 0;
        foreach (var segment in segments)
        {
            var text = segment.Text;
            var style = segment.Style;
            for (int i = 0; i < text.Length && cursor < maxWidth; i++)
            {
                buffer[cursor++] = new Cell(text[i], style);
            }
        }

        return cursor;
    }

    private static int MergeToBuffer(IReadOnlyList<Segment> overlaySegments, Cell[] buffer, int left, int maxWidth)
    {
        int cursor = left;
        foreach (var segment in overlaySegments)
        {
            var text = segment.Text;
            var style = segment.Style;
            for (int i = 0; i < text.Length && cursor < maxWidth; i++)
            {
                if (cursor >= 0)
                {
                    buffer[cursor] = new Cell(text[i], style);
                }

                cursor++;
            }
        }

        return cursor;
    }

    private static SegmentLine FromBuffer(Cell[] buffer, int length)
    {
        if (length <= 0)
        {
            return new SegmentLine();
        }

        var result = new SegmentLine();
        var currentStyle = buffer[0].Style;
        int start = 0;

        for (int i = 1; i < length; i++)
        {
            if (!buffer[i].Style.Equals(currentStyle))
            {
                result.Add(new Segment(CreateStringFromBuffer(buffer, start, i - start), currentStyle));
                currentStyle = buffer[i].Style;
                start = i;
            }
        }

        result.Add(new Segment(CreateStringFromBuffer(buffer, start, length - start), currentStyle));
        return result;
    }

    private static string CreateStringFromBuffer(Cell[] buffer, int start, int count)
    {
        return string.Create(count, (buffer, start), static (span, state) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = state.buffer[state.start + i].Char;
            }
        });
    }

    private record struct Cell(char Char, Style Style);

    private record struct OverlayPosition(SegmentLine Line, int Left);
}
