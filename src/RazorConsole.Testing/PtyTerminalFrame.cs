// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Layout;

namespace RazorConsole.Testing;

/// <summary>
/// An immutable capture of the screen of a <see cref="PtyTerminal"/>: the cell grid, the cursor, and the terminal modes.
/// </summary>
public sealed class PtyTerminalFrame : TerminalFrame
{
    internal PtyTerminalFrame(
        long frameNumber,
        DateTimeOffset observedAt,
        TerminalCell[,] cells,
        int cursorX,
        int cursorY,
        bool cursorVisible,
        TerminalScreenBuffer activeBuffer,
        TerminalMouseTracking mouseTracking,
        bool bracketedPaste)
        : base(frameNumber, observedAt, cells)
    {
        CursorX = cursorX;
        CursorY = cursorY;
        CursorVisible = cursorVisible;
        ActiveBuffer = activeBuffer;
        MouseTracking = mouseTracking;
        BracketedPaste = bracketedPaste;
    }

    /// <summary>
    /// Gets the zero-based cursor column.
    /// </summary>
    public int CursorX { get; }

    /// <summary>
    /// Gets the zero-based cursor row.
    /// </summary>
    public int CursorY { get; }

    /// <summary>
    /// Gets whether the cursor is visible (DEC private mode 25).
    /// </summary>
    public bool CursorVisible { get; }

    /// <summary>
    /// Gets the screen buffer that is shown.
    /// </summary>
    public TerminalScreenBuffer ActiveBuffer { get; }

    /// <summary>
    /// Gets the mouse reporting modes the application has enabled.
    /// </summary>
    public TerminalMouseTracking MouseTracking { get; }

    /// <summary>
    /// Gets whether the application has enabled bracketed paste (DEC private mode 2004).
    /// </summary>
    public bool BracketedPaste { get; }

    internal bool HasSameState(PtyTerminalFrame other)
        => CursorX == other.CursorX
            && CursorY == other.CursorY
            && CursorVisible == other.CursorVisible
            && ActiveBuffer == other.ActiveBuffer
            && MouseTracking == other.MouseTracking
            && BracketedPaste == other.BracketedPaste
            && HasSameCells(other);

    private protected override void AppendHeaderDetails(StringBuilder builder)
        => builder.Append(", cursor: ").Append(CursorX).Append(',').Append(CursorY)
            .Append(CursorVisible ? " visible" : " hidden")
            .Append(", buffer: ").Append(ActiveBuffer)
            .Append(", mouse: ").Append(MouseTracking)
            .Append(", bracketed paste: ").Append(BracketedPaste ? "on" : "off");
}
