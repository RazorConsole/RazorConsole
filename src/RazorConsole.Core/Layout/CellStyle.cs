// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

/// <summary>
/// The color of a terminal cell: the terminal's default, an entry of its 256-color palette, or a 24-bit color.
/// </summary>
/// <remarks>
/// A palette color stays an index, so the terminal's color scheme picks its shade: palette red and 24-bit
/// <c>#FF0000</c> are different colors here.
/// </remarks>
public readonly struct CellColor : IEquatable<CellColor>
{
    private const byte DefaultKind = 0;
    private const byte PaletteKind = 1;
    private const byte RgbKind = 2;

    private readonly byte _kind;

    private CellColor(byte kind, byte r, byte g, byte b)
    {
        _kind = kind;
        R = r;
        G = g;
        B = b;
    }

    /// <summary>The terminal's default color.</summary>
    public static CellColor Default => default;

    /// <summary>Whether this is the terminal's default color.</summary>
    public bool IsDefault => _kind == DefaultKind;

    /// <summary>Whether this is an entry of the terminal's palette; <see cref="Index"/> says which.</summary>
    public bool IsPalette => _kind == PaletteKind;

    /// <summary>Whether this is a 24-bit color; <see cref="R"/>, <see cref="G"/> and <see cref="B"/> give it.</summary>
    public bool IsRgb => _kind == RgbKind;

    /// <summary>The palette index (0–255) of a palette color; 0 otherwise.</summary>
    public byte Index => IsPalette ? R : (byte)0;

    /// <summary>The red component of a 24-bit color; for a palette color, its index.</summary>
    public byte R { get; }

    /// <summary>The green component of a 24-bit color.</summary>
    public byte G { get; }

    /// <summary>The blue component of a 24-bit color.</summary>
    public byte B { get; }

    /// <summary>An entry of the terminal's 256-color palette: 0–7 the standard colors, 8–15 their bright versions.</summary>
    public static CellColor FromPalette(byte index) => new(PaletteKind, index, 0, 0);

    /// <summary>A 24-bit color.</summary>
    public static CellColor FromRgb(byte r, byte g, byte b) => new(RgbKind, r, g, b);

    public bool Equals(CellColor other) => _kind == other._kind && R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is CellColor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_kind, R, G, B);

    public override string ToString()
        => _kind switch
        {
            PaletteKind => $"palette({Index})",
            RgbKind => $"#{R:X2}{G:X2}{B:X2}",
            _ => "default",
        };

    public static bool operator ==(CellColor left, CellColor right) => left.Equals(right);

    public static bool operator !=(CellColor left, CellColor right) => !left.Equals(right);
}

/// <summary>The text decorations of a terminal cell.</summary>
[Flags]
public enum CellDecoration : ushort
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Invert = 16,
    Conceal = 32,
    SlowBlink = 64,
    RapidBlink = 128,
    Strikethrough = 256,
}

/// <summary>
/// How a terminal cell looks: its foreground and background colors and its decorations. The default value is a plain
/// cell, in the terminal's default colors.
/// </summary>
public readonly record struct CellStyle(CellColor Foreground, CellColor Background, CellDecoration Decoration = CellDecoration.None)
{
    /// <summary>A plain cell, in the terminal's default colors and without decorations.</summary>
    public static CellStyle Plain => default;

    /// <summary>Whether this is a plain cell.</summary>
    public bool IsPlain => this == default;
}
