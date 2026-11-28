// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Spectre.Console;

namespace RazorConsole.Core.Layout;

/// <summary>
/// Converts between Spectre.Console styles, which components and widgets use, and <see cref="CellStyle"/>, which cells
/// store.
/// </summary>
/// <remarks>
/// A Spectre palette color (<see cref="Color.Red"/>) becomes a palette <see cref="CellColor"/> and an RGB color a 24-bit
/// one, although Spectre treats them as equal. Spectre keeps the palette index internal; it is read with
/// <see cref="UnsafeAccessorAttribute"/>, and from the color's markup name if that ever fails.
/// </remarks>
internal static class SpectreCellStyle
{
    private static readonly FrozenDictionary<string, byte> PaletteByName = Enumerable.Range(0, 256)
        .ToFrozenDictionary(index => Color.FromInt32(index).ToMarkup(), index => (byte)index, StringComparer.Ordinal);

    private static bool s_accessorsWork = true;

    public static CellStyle ToCellStyle(Style? style)
        => style is { } value
            ? new CellStyle(ToCellColor(value.Foreground), ToCellColor(value.Background), (CellDecoration)(ushort)value.Decoration)
            : CellStyle.Plain;

    public static CellColor ToCellColor(Color color)
    {
        if (Volatile.Read(ref s_accessorsWork))
        {
            try
            {
                if (GetIsDefault(ref color))
                {
                    return CellColor.Default;
                }

                return GetNumber(ref color) is { } index
                    ? CellColor.FromPalette(index)
                    : CellColor.FromRgb(color.R, color.G, color.B);
            }
            catch (MissingMemberException)
            {
                Volatile.Write(ref s_accessorsWork, false);
            }
        }

        return ToCellColorByName(color);
    }

    // The slower way, from the markup name Spectre gives a color: "default", a palette name, or "#rrggbb".
    internal static CellColor ToCellColorByName(Color color)
    {
        var markup = color.ToMarkup();
        if (markup == "default")
        {
            return CellColor.Default;
        }

        return PaletteByName.TryGetValue(markup, out var paletteIndex)
            ? CellColor.FromPalette(paletteIndex)
            : CellColor.FromRgb(color.R, color.G, color.B);
    }

    /// <summary>The Spectre style of <paramref name="style"/>, or <see langword="null"/> for a plain cell.</summary>
    public static Style? ToSpectreStyle(CellStyle style)
        => style.IsPlain
            ? (Style?)null
            : new Style(ToSpectreColor(style.Foreground), ToSpectreColor(style.Background), (Decoration)(int)style.Decoration);

    public static Color ToSpectreColor(CellColor color)
        => color.IsPalette ? Color.FromInt32(color.Index)
            : color.IsRgb ? new Color(color.R, color.G, color.B)
            : Color.Default;

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_IsDefault")]
    private static extern bool GetIsDefault(ref Color color);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Number")]
    private static extern byte? GetNumber(ref Color color);
}
