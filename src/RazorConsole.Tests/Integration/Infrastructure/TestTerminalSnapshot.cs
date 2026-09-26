// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.ObjectModel;
using System.Text;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Integration.Infrastructure;

internal sealed class TestTerminalSnapshot
{
    private readonly TerminalCell[,] _cells;

    public TestTerminalSnapshot(
        long frameNumber,
        DateTimeOffset observedAt,
        TerminalCanvas canvas,
        int terminalWidth,
        int terminalHeight,
        string? focusKey,
        IReadOnlyDictionary<string, VNodeLayoutInfo> layouts)
    {
        FrameNumber = frameNumber;
        ObservedAt = observedAt;
        Width = terminalWidth;
        Height = terminalHeight;
        FocusKey = focusKey;
        _cells = new TerminalCell[Height, Width];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                _cells[y, x] = x < canvas.Width && y < canvas.Height
                    ? canvas[x, y]
                    : TerminalCell.Empty;
            }
        }

        Layouts = new ReadOnlyDictionary<string, VNodeLayoutInfo>(
            new Dictionary<string, VNodeLayoutInfo>(layouts, StringComparer.Ordinal));
    }

    public long FrameNumber { get; }

    public DateTimeOffset ObservedAt { get; }

    public int Width { get; }

    public int Height { get; }

    public string? FocusKey { get; }

    public IReadOnlyDictionary<string, VNodeLayoutInfo> Layouts { get; }

    public TerminalCell this[int x, int y]
    {
        get
        {
            if (x < 0 || x >= Width)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if (y < 0 || y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            return _cells[y, x];
        }
    }

    public bool ContainsText(string text)
        => ScreenText.Contains(text, StringComparison.Ordinal);

    public bool HasSameCells(TestTerminalSnapshot other)
    {
        if (Width != other.Width || Height != other.Height)
        {
            return false;
        }
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (this[x, y] != other[x, y])
                {
                    return false;
                }
            }
        }
        return true;
    }

    public string ScreenText
    {
        get
        {
            var builder = new StringBuilder();
            for (var y = 0; y < Height; y++)
            {
                if (y > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(GetLine(y));
            }

            return builder.ToString();
        }
    }

    public string GetLine(int row)
    {
        if (row < 0 || row >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        var builder = new StringBuilder();
        for (var x = 0; x < Width; x++)
        {
            builder.Append(_cells[row, x].Text);
        }

        return builder.ToString();
    }

    public VNodeLayoutInfo GetLayout(string hookKey)
        => Layouts.TryGetValue(hookKey, out var layout)
            ? layout
            : throw new KeyNotFoundException($"No layout was captured for hook '{hookKey}'.");

    public string ToDiagnosticString()
    {
        var builder = new StringBuilder();
        builder.Append("Frame ").Append(FrameNumber)
            .Append(" (").Append(Width).Append('x').Append(Height).Append(")")
            .Append(", focus: ").AppendLine(FocusKey ?? "<none>");

        for (var y = 0; y < Height; y++)
        {
            builder.Append(GetLine(y).TrimEnd()).AppendLine();
        }

        if (Layouts.Count > 0)
        {
            builder.AppendLine("Layouts:");
            foreach (var pair in Layouts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var layout = pair.Value;
                builder.Append("  ").Append(pair.Key).Append(": ")
                    .Append("left=").Append(layout.Left)
                    .Append(", top=").Append(layout.Top)
                    .Append(", width=").Append(layout.Width)
                    .Append(", height=").Append(layout.Height)
                    .Append(", z=").Append(layout.ZIndex)
                    .AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    public override string ToString() => ToDiagnosticString();
}
