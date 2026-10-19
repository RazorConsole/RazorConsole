// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Layout;

namespace RazorConsole.Testing;

/// <summary>
/// An immutable capture of one terminal screen: the cell grid and its size.
/// </summary>
/// <remarks>
/// <see cref="HeadlessTerminalFrame"/> adds focus and layout information from a <see cref="HeadlessTerminal"/>;
/// <see cref="PtyTerminalFrame"/> adds cursor and terminal mode state from a <see cref="PtyTerminal"/>.
/// </remarks>
public abstract class TerminalFrame
{
    private readonly TerminalCell[,] _cells;

    private protected TerminalFrame(long frameNumber, DateTimeOffset observedAt, TerminalCell[,] cells)
    {
        FrameNumber = frameNumber;
        ObservedAt = observedAt;
        _cells = cells;
        Height = cells.GetLength(0);
        Width = cells.GetLength(1);
    }

    /// <summary>
    /// Gets the sequence number of the frame within its terminal.
    /// </summary>
    public long FrameNumber { get; }

    /// <summary>
    /// Gets the time at which the frame was captured.
    /// </summary>
    public DateTimeOffset ObservedAt { get; }

    /// <summary>
    /// Gets the terminal width in columns when the frame was captured.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the terminal height in rows when the frame was captured.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the cell at the zero-based column <paramref name="x"/> and row <paramref name="y"/>.
    /// </summary>
    /// <param name="x">The zero-based column.</param>
    /// <param name="y">The zero-based row.</param>
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

    /// <summary>
    /// Gets the plain text of the screen, one line per row separated by <c>\n</c>.
    /// </summary>
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

    /// <summary>
    /// Returns whether the screen text contains <paramref name="text"/> (ordinal comparison).
    /// </summary>
    /// <param name="text">The text to look for.</param>
    public bool ContainsText(string text)
        => ScreenText.Contains(text, StringComparison.Ordinal);

    /// <summary>
    /// Returns whether this frame has the same size and cells as <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The frame to compare with.</param>
    public bool HasSameCells(TerminalFrame other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Width != other.Width || Height != other.Height)
        {
            return false;
        }
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (_cells[y, x] != other._cells[y, x])
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Gets the plain text of one row.
    /// </summary>
    /// <param name="row">The zero-based row.</param>
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

    /// <summary>
    /// Formats the frame for failure messages: a header and the screen lines.
    /// </summary>
    public virtual string ToDiagnosticString()
    {
        var builder = new StringBuilder();
        builder.Append("Frame ").Append(FrameNumber)
            .Append(" (").Append(Width).Append('x').Append(Height).Append(')');
        AppendHeaderDetails(builder);
        builder.AppendLine();
        AppendScreen(builder);
        AppendFooterDetails(builder);
        return builder.ToString().TrimEnd();
    }

    /// <inheritdoc />
    public override string ToString() => ToDiagnosticString();

    private protected virtual void AppendHeaderDetails(StringBuilder builder)
    {
    }

    private protected virtual void AppendFooterDetails(StringBuilder builder)
    {
    }

    private void AppendScreen(StringBuilder builder)
    {
        for (var y = 0; y < Height; y++)
        {
            builder.Append(GetLine(y).TrimEnd()).AppendLine();
        }
    }
}
