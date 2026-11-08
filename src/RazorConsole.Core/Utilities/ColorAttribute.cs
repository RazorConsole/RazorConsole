// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;

namespace RazorConsole.Core.Utilities;

/// <summary>
/// Colors in element attributes, by markup name: a palette color (<c>blue</c>) keeps its index, so the terminal's theme
/// still picks its shade. Hex would turn it into fixed RGB.
/// </summary>
internal static class ColorAttribute
{
    public static string Format(Color color) => color.ToMarkup();

    public static bool TryParse(string? value, out Color color)
    {
        color = Color.Default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Color.TryFromHex(value, out color))
        {
            return true;
        }

        if (Style.TryParse(value, out var style))
        {
            color = style.Foreground;
            return true;
        }
        return false;
    }
}
