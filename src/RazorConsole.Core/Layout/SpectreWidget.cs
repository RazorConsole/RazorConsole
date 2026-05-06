// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

public sealed class SpectreWidget : Widget
{
    private readonly IRenderable _renderable;
    private int _lastMeasuredMaxWidth;

    public SpectreWidget(
        string vnodeId,
        IRenderable renderable,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, zIndex: zIndex)
    {
        _renderable = renderable ?? throw new ArgumentNullException(nameof(renderable));
    }

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        if (constraints.MaxWidth == 0 || constraints.MaxHeight == 0)
        {
            return constraints.Constrain(LayoutSize.Empty);
        }

        _lastMeasuredMaxWidth = constraints.MaxWidth;
        var lines = RenderLines(constraints.MaxWidth, constraints.MaxHeight);
        var width = lines
            .Select(line => Segment.CellCount(line.Segments))
            .DefaultIfEmpty(0)
            .Max();
        var height = Math.Min(lines.Count, constraints.MaxHeight);

        return constraints.Constrain(new LayoutSize(Math.Min(width, constraints.MaxWidth), height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
    }

    protected override void PaintCore(PaintContext context)
    {
        if (Bounds.IsEmpty)
        {
            return;
        }

        var renderWidth = Math.Max(Bounds.Width, _lastMeasuredMaxWidth);
        var lines = RenderLines(renderWidth, Bounds.Height);
        var maxLines = Math.Min(Bounds.Height, lines.Count);
        for (var row = 0; row < maxLines; row++)
        {
            var line = lines[row];
            context.Canvas.WriteSegments(Bounds.X, Bounds.Y + row, line.Segments, Bounds.Width);
        }
    }

    private IReadOnlyList<RenderedLine> RenderLines(int maxWidth, int maxHeight)
    {
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            return [];
        }

        var options = CreateRenderOptions(maxWidth, maxHeight);
        var lines = new List<RenderedLine>();
        var current = new List<Segment>();
        var currentWidth = 0;

        foreach (var segment in _renderable.Render(options, maxWidth))
        {
            if (segment.IsLineBreak)
            {
                lines.Add(new RenderedLine(current.ToArray()));
                current.Clear();
                currentWidth = 0;

                if (lines.Count >= maxHeight)
                {
                    break;
                }

                continue;
            }

            AppendWrappedSegment(segment, maxWidth, maxHeight, lines, current, ref currentWidth);
            if (lines.Count >= maxHeight)
            {
                break;
            }
        }

        if (lines.Count < maxHeight && current.Count > 0)
        {
            lines.Add(new RenderedLine(current.ToArray()));
        }

        return lines;
    }

    private static void AppendWrappedSegment(
        Segment segment,
        int maxWidth,
        int maxHeight,
        List<RenderedLine> lines,
        List<Segment> current,
        ref int currentWidth)
    {
        if (string.IsNullOrEmpty(segment.Text))
        {
            current.Add(segment);
            return;
        }

        foreach (var rune in segment.Text.EnumerateRunes())
        {
            var text = rune.ToString();
            var width = Math.Max(1, Segment.CellCount([new Segment(text)]));
            if (currentWidth > 0 && currentWidth + width > maxWidth)
            {
                lines.Add(new RenderedLine(current.ToArray()));
                current.Clear();
                currentWidth = 0;
                if (lines.Count >= maxHeight)
                {
                    return;
                }
            }

            current.Add(new Segment(text, segment.Style));
            currentWidth += width;
        }
    }

    private static RenderOptions CreateRenderOptions(int maxWidth, int maxHeight)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        return new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(maxWidth, Math.Max(1, maxHeight)));
    }

    private sealed record RenderedLine(IReadOnlyList<Segment> Segments);
}
