// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Layout;
using Spectre.Console;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// What a terminal can show, which decides how <see cref="AnsiFrameEncoder"/> writes colors and links.
/// </summary>
/// <param name="Colors">The color system: 24-bit, 256 colors, 16 colors, or none.</param>
/// <param name="Links">Whether the terminal shows OSC 8 hyperlinks.</param>
internal readonly record struct AnsiOutputCapabilities(ColorSystem Colors, bool Links);

/// <summary>
/// Encodes frames as ANSI for a terminal that shows them in its viewport, writing only the cells that changed since the
/// frame before.
/// </summary>
/// <remarks>
/// <para>
/// Every frame is addressed absolutely (CUP), so cursor movement outside the encoder never misplaces it. Within a row,
/// only the span from the first to the last changed cell is written, widened so a wide grapheme is never split. A style
/// is written only when it changes, and the terminal is left in its default style after every frame.
/// </para>
/// <para>
/// The first frame, a frame after <see cref="Invalidate"/>, and a frame whose size or terminal size changed are written
/// in full after erasing the viewport (never the scrollback). Autowrap is off while a frame is written, so the last
/// column never scrolls the screen, and the cursor is hidden while drawing.
/// </para>
/// </remarks>
internal sealed class AnsiFrameEncoder(bool hideCursor)
{
    private readonly StringBuilder _builder = new();
    private readonly Dictionary<string, int> _linkIds = new(StringComparer.Ordinal);
    private TerminalFrame? _previous;
    private int _previousRows;
    private int _terminalWidth = -1;
    private int _terminalHeight = -1;

    // The terminal's current style and link while a frame is being encoded.
    private CellStyle _style;
    private string? _link;

    /// <summary>Makes the next frame a full redraw, after a write that may have reached the terminal only in part.</summary>
    public void Invalidate() => _previous = null;

    /// <summary>
    /// Encodes <paramref name="frame"/> for a terminal of <paramref name="terminalWidth"/> by
    /// <paramref name="terminalHeight"/> cells. The frame is clipped to the terminal.
    /// </summary>
    public string Encode(TerminalFrame frame, int terminalWidth, int terminalHeight, AnsiOutputCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var width = Math.Min(frame.Width, Math.Max(0, terminalWidth));
        var rows = Math.Min(frame.Height, Math.Max(1, terminalHeight));
        var previous = _previous;
        var fullRedraw = previous is null
            || terminalWidth != _terminalWidth
            || terminalHeight != _terminalHeight
            || previous.Width != frame.Width;

        _builder.Clear();
        _style = CellStyle.Plain;
        _link = null;
        _builder.Append(RM(DECTCEM)).Append(RM(DECAWM));
        if (fullRedraw)
        {
            _builder.Append(ED(2)).Append(CUP(1, 1));
        }

        for (var y = 0; y < rows; y++)
        {
            // After erasing, and on a row the previous frame did not have, the screen is blank: only cells that are not
            // blank need writing.
            var span = fullRedraw || y >= _previousRows
                ? FindChangedSpan(frame, previous: null, y, width)
                : FindChangedSpan(frame, previous, y, width);
            if (span is { } changed)
            {
                WriteSpan(frame, y, changed.Start, changed.End, capabilities);
            }
        }

        // Rows the previous frame had and this one does not.
        if (!fullRedraw)
        {
            for (var y = rows; y < _previousRows; y++)
            {
                _builder.Append(CUP(y + 1, 1)).Append(EL(2));
            }
        }

        CloseLink();
        if (!_style.IsPlain)
        {
            _builder.Append("\u001b[0m");
            _style = CellStyle.Plain;
        }

        // No newline at the bottom edge: it would scroll the screen.
        _builder.Append(CUP(Math.Max(1, rows), 1)).Append('\r');
        _builder.Append(SM(DECAWM));
        if (!hideCursor)
        {
            _builder.Append(SM(DECTCEM));
        }

        _previous = frame;
        _previousRows = rows;
        _terminalWidth = terminalWidth;
        _terminalHeight = terminalHeight;
        return _builder.ToString();
    }

    // The first and last columns of a row that differ from the previous frame (from a blank screen when there is none),
    // widened to whole graphemes.
    private static (int Start, int End)? FindChangedSpan(TerminalFrame frame, TerminalFrame? previous, int y, int width)
    {
        var start = -1;
        var end = -1;
        for (var x = 0; x < width; x++)
        {
            if (frame[x, y] != (previous is null ? TerminalCell.Empty : previous[x, y]))
            {
                if (start < 0)
                {
                    start = x;
                }

                end = x;
            }
        }

        if (start < 0)
        {
            return null;
        }

        // A continuation cell is drawn by the grapheme it continues: start there.
        while (start > 0 && frame[start, y].IsContinuation)
        {
            start--;
        }

        // A wide grapheme at the end covers the continuation cells after it.
        while (end + 1 < width && frame[end + 1, y].IsContinuation)
        {
            end++;
        }

        return (start, end);
    }

    private void WriteSpan(TerminalFrame frame, int y, int start, int end, AnsiOutputCapabilities capabilities)
    {
        if (end < start)
        {
            return;
        }

        _builder.Append(CUP(y + 1, start + 1));
        for (var x = start; x <= end; x++)
        {
            var cell = frame[x, y];
            if (cell.IsContinuation)
            {
                // Drawn by the wide grapheme before it; a continuation at the span's start never happens (see above).
                continue;
            }

            SetLink(capabilities.Links ? cell.Link : null);
            SetStyle(cell.CellStyle, capabilities.Colors);
            _builder.Append(cell.Text);
        }
    }

    // Writes only what changed: a return to plain, or a decoration that went away, resets first; otherwise the added
    // decorations and the colors that changed are set on top of the current style.
    private void SetStyle(CellStyle style, ColorSystem colors)
    {
        if (style == _style)
        {
            return;
        }

        var current = _style;
        _style = style;
        if (style.IsPlain)
        {
            _builder.Append("\u001b[0m");
            return;
        }

        var reset = (current.Decoration & ~style.Decoration) != 0;
        if (reset)
        {
            current = CellStyle.Plain;
        }

        var start = _builder.Length;
        _builder.Append("\u001b[");
        if (reset)
        {
            _builder.Append('0');
        }

        AppendDecoration(style.Decoration & ~current.Decoration);
        if (style.Foreground != current.Foreground)
        {
            AppendColor(style.Foreground, colors, foreground: true);
        }

        if (style.Background != current.Background)
        {
            AppendColor(style.Background, colors, foreground: false);
        }

        if (_builder.Length == start + 2)
        {
            // Nothing the terminal can show changed (a color on a terminal without colors).
            _builder.Length = start;
            return;
        }

        // The first code needs no separator.
        if (!reset && _builder[start + 2] == ';')
        {
            _builder.Remove(start + 2, 1);
        }

        _builder.Append('m');
    }

    private static readonly (CellDecoration Flag, char Code)[] DecorationCodes =
    [
        (CellDecoration.Bold, '1'),
        (CellDecoration.Dim, '2'),
        (CellDecoration.Italic, '3'),
        (CellDecoration.Underline, '4'),
        (CellDecoration.SlowBlink, '5'),
        (CellDecoration.RapidBlink, '6'),
        (CellDecoration.Invert, '7'),
        (CellDecoration.Conceal, '8'),
        (CellDecoration.Strikethrough, '9'),
    ];

    private void AppendDecoration(CellDecoration decoration)
    {
        if (decoration == CellDecoration.None)
        {
            return;
        }

        foreach (var (flag, code) in DecorationCodes)
        {
            if ((decoration & flag) != 0)
            {
                _builder.Append(';').Append(code);
            }
        }
    }

    private void AppendColor(CellColor color, ColorSystem colors, bool foreground)
    {
        if (colors == ColorSystem.NoColors)
        {
            return;
        }

        if (color.IsDefault)
        {
            _builder.Append(foreground ? ";39" : ";49");
            return;
        }

        var fitted = Fit(color, colors);
        if (fitted.IsPalette && fitted.Index < 8)
        {
            _builder.Append(';').Append((foreground ? 30 : 40) + fitted.Index);
        }
        else if (fitted.IsPalette && fitted.Index < 16)
        {
            _builder.Append(';').Append((foreground ? 90 : 100) + fitted.Index - 8);
        }
        else if (fitted.IsPalette)
        {
            _builder.Append(foreground ? ";38;5;" : ";48;5;").Append(fitted.Index);
        }
        else
        {
            _builder.Append(foreground ? ";38;2;" : ";48;2;")
                .Append(fitted.R).Append(';').Append(fitted.G).Append(';').Append(fitted.B);
        }
    }

    // A color the terminal can show: palette colors beyond its palette and 24-bit colors on a terminal without them
    // become the closest palette entry, the way Spectre.Console fits them.
    private static CellColor Fit(CellColor color, ColorSystem colors)
    {
        var limit = colors switch
        {
            ColorSystem.TrueColor => 256,
            ColorSystem.EightBit => 256,
            _ => 16,
        };

        if (color.IsPalette ? color.Index < limit : colors == ColorSystem.TrueColor)
        {
            return color;
        }

        return SpectreCellStyle.ToCellColor(SpectreCellStyle.ToSpectreColor(color).ExactOrClosest(colors));
    }

    private void SetLink(string? link)
    {
        if (string.Equals(link, _link, StringComparison.Ordinal))
        {
            return;
        }

        CloseLink();
        if (link is not null)
        {
            if (!_linkIds.TryGetValue(link, out var id))
            {
                id = _linkIds.Count + 1;
                _linkIds[link] = id;
            }

            _builder.Append("\u001b]8;id=").Append(id).Append(';').Append(link).Append("\u001b\\");
            _link = link;
        }
    }

    private void CloseLink()
    {
        if (_link is null)
        {
            return;
        }

        _builder.Append("\u001b]8;;\u001b\\");
        _link = null;
    }
}
