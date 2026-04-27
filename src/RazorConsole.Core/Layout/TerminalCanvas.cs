// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

public sealed class TerminalCanvas
{
    private readonly TerminalCell[,] _cells;

    public TerminalCanvas(int width, int height)
    {
        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width cannot be negative.");
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height cannot be negative.");
        }

        Width = width;
        Height = height;
        _cells = new TerminalCell[height, width];
        Clear();
    }

    public int Width { get; }

    public int Height { get; }

    public TerminalCell this[int x, int y]
    {
        get
        {
            ThrowIfOutOfRange(x, y);
            return _cells[y, x];
        }
    }

    public void Clear()
        => Fill(new LayoutRect(0, 0, Width, Height), ' ', style: null);

    public void Fill(LayoutRect rect, char ch, Style? style = null)
    {
        var clipped = rect.Intersect(new LayoutRect(0, 0, Width, Height));
        if (clipped.IsEmpty)
        {
            return;
        }

        var cell = new TerminalCell(ch.ToString(), style);
        for (var y = clipped.Y; y < clipped.Bottom; y++)
        {
            for (var x = clipped.X; x < clipped.Right; x++)
            {
                _cells[y, x] = cell;
            }
        }
    }

    public void Write(int x, int y, string? text, Style? style = null)
        => Write(x, y, text, maxWidth: null, style);

    public void WriteSegments(int x, int y, IEnumerable<Segment> segments, int? maxWidth = null)
    {
        if (segments is null)
        {
            throw new ArgumentNullException(nameof(segments));
        }

        if (y < 0 || y >= Height || maxWidth == 0)
        {
            return;
        }

        if (maxWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Maximum width cannot be negative.");
        }

        var cursor = x;
        var written = 0;
        foreach (var segment in segments)
        {
            if (segment.IsLineBreak || string.IsNullOrEmpty(segment.Text))
            {
                continue;
            }

            foreach (var rune in segment.Text.EnumerateRunes())
            {
                if (maxWidth.HasValue && written >= maxWidth.Value)
                {
                    return;
                }

                if (cursor >= Width)
                {
                    return;
                }

                if (cursor >= 0)
                {
                    _cells[y, cursor] = new TerminalCell(rune.ToString(), segment.Style);
                }

                cursor++;
                written++;
            }
        }
    }

    public void Write(int x, int y, string? text, int? maxWidth, Style? style = null)
    {
        if (string.IsNullOrEmpty(text) || y < 0 || y >= Height || maxWidth == 0)
        {
            return;
        }

        if (maxWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Maximum width cannot be negative.");
        }

        var cursor = x;
        var written = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (maxWidth.HasValue && written >= maxWidth.Value)
            {
                return;
            }

            if (cursor >= Width)
            {
                return;
            }

            if (cursor >= 0)
            {
                _cells[y, cursor] = new TerminalCell(rune.ToString(), style);
            }

            cursor++;
            written++;
        }
    }

    public IRenderable ToRenderable()
        => new CanvasRenderable(this);

    internal IEnumerable<Segment> RenderSegments(int maxWidth)
    {
        var width = Math.Min(Width, Math.Max(0, maxWidth));
        for (var y = 0; y < Height; y++)
        {
            foreach (var segment in RenderLine(y, width))
            {
                yield return segment;
            }

            if (y < Height - 1)
            {
                yield return Segment.LineBreak;
            }
        }
    }

    private IEnumerable<Segment> RenderLine(int y, int width)
    {
        if (width == 0)
        {
            yield break;
        }

        var builder = new StringBuilder();
        Style? currentStyle = null;

        for (var x = 0; x < width; x++)
        {
            var cell = _cells[y, x];
            if (x == 0)
            {
                currentStyle = cell.Style;
                builder.Append(cell.Text);
                continue;
            }

            if (!Equals(currentStyle, cell.Style))
            {
                yield return CreateSegment(builder.ToString(), currentStyle);
                builder.Clear();
                currentStyle = cell.Style;
            }

            builder.Append(cell.Text);
        }

        if (builder.Length > 0)
        {
            yield return CreateSegment(builder.ToString(), currentStyle);
        }
    }

    private static Segment CreateSegment(string text, Style? style)
        => style is null ? new Segment(text) : new Segment(text, style);

    private void ThrowIfOutOfRange(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }
}
