// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;

namespace RazorConsole.Core.Layout;

/// <summary>
/// One cell of a terminal screen: the text drawn in it, how it looks, and the hyperlink it belongs to.
/// </summary>
/// <param name="Text">
/// The text in the cell: one grapheme, or <see cref="string.Empty"/> for the cells a wide grapheme (such as CJK text or
/// an emoji) covers after its first cell.
/// </param>
/// <param name="CellStyle">How the cell looks.</param>
/// <param name="Link">The hyperlink the cell belongs to, or <see langword="null"/>.</param>
public readonly record struct TerminalCell(string Text, CellStyle CellStyle, string? Link = null)
{
    /// <summary>A plain cell from a Spectre.Console style; <see langword="null"/> is a plain cell.</summary>
    public TerminalCell(string Text, Style? Style, string? Link = null)
        : this(Text, SpectreCellStyle.ToCellStyle(Style), Link)
    {
    }

    public static TerminalCell Empty { get; } = new(" ", CellStyle.Plain);

    /// <summary>
    /// The cell's style as a Spectre.Console style, or <see langword="null"/> for a plain cell. Built from
    /// <see cref="CellStyle"/> on every read: terminal backends read <see cref="CellStyle"/>.
    /// </summary>
    public Style? Style => SpectreCellStyle.ToSpectreStyle(CellStyle);

    /// <summary>Whether this cell continues a wide grapheme that starts in the cell before it.</summary>
    public bool IsContinuation => Text.Length == 0;
}
