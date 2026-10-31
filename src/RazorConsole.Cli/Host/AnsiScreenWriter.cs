// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text;
using RazorConsole.Testing;
using Spectre.Console;

namespace RazorConsole.Cli.Host;

/// <summary>
/// Writes a region of a frame as text with SGR styles (truecolor and decorations) for <c>screen --format ansi</c>.
/// Every line ends with a reset, so lines can be printed or cut independently. Links are not written.
/// </summary>
internal static class AnsiScreenWriter
{
    private static readonly (Decoration Flag, int Code)[] Decorations =
    [
        (Decoration.Bold, 1),
        (Decoration.Dim, 2),
        (Decoration.Italic, 3),
        (Decoration.Underline, 4),
        (Decoration.SlowBlink, 5),
        (Decoration.RapidBlink, 6),
        (Decoration.Invert, 7),
        (Decoration.Conceal, 8),
        (Decoration.Strikethrough, 9),
    ];

    public static string Write(TerminalFrame frame, int x, int y, int width, int height)
    {
        var builder = new StringBuilder();
        for (var row = y; row < y + height; row++)
        {
            if (row > y)
            {
                builder.Append('\n');
            }

            Style? current = null;
            for (var column = x; column < x + width; column++)
            {
                var cell = frame[column, row];
                if (!Equals(cell.Style, current))
                {
                    AppendStyle(builder, cell.Style);
                    current = cell.Style;
                }

                builder.Append(cell.Text);
            }

            builder.Append("\u001b[0m");
        }

        return builder.ToString();
    }

    private static void AppendStyle(StringBuilder builder, Style? style)
    {
        builder.Append("\u001b[0");
        if (style is not null)
        {
            foreach (var (flag, code) in Decorations)
            {
                if ((style.Decoration & flag) != 0)
                {
                    builder.Append(';').Append(code.ToString(CultureInfo.InvariantCulture));
                }
            }

            AppendColor(builder, 38, style.Foreground);
            AppendColor(builder, 48, style.Background);
        }

        builder.Append('m');
    }

    private static void AppendColor(StringBuilder builder, int code, Color color)
    {
        if (color == Color.Default)
        {
            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $";{code};2;{color.R};{color.G};{color.B}");
    }
}
