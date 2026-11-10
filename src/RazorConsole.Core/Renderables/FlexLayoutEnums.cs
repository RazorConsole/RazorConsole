// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Renderables;

/// <summary>
/// Specifies the main axis direction for a flex layout.
/// </summary>
public enum FlexDirection
{
    /// <summary>Items are laid out horizontally, left to right.</summary>
    Row,

    /// <summary>Items are laid out vertically, top to bottom.</summary>
    Column,
}

/// <summary>
/// Specifies how free space is distributed along the main axis.
/// </summary>
public enum FlexJustify
{
    /// <summary>Items are packed toward the start of the main axis.</summary>
    Start,

    /// <summary>Items are packed toward the end of the main axis.</summary>
    End,

    /// <summary>Items are centered along the main axis.</summary>
    Center,

    /// <summary>Items are evenly distributed; first item is at the start, last item is at the end.</summary>
    SpaceBetween,

    /// <summary>Items are evenly distributed with equal space around each item.</summary>
    SpaceAround,

    /// <summary>Items are evenly distributed with equal space between and at the edges.</summary>
    SpaceEvenly,
}

/// <summary>
/// Specifies how items are aligned along the cross axis.
/// </summary>
public enum FlexAlign
{
    /// <summary>Items are aligned to the start of the cross axis.</summary>
    Start,

    /// <summary>Items are aligned to the end of the cross axis.</summary>
    End,

    /// <summary>Items are centered along the cross axis.</summary>
    Center,

    /// <summary>Items are stretched to fill the cross axis.</summary>
    Stretch,
}

/// <summary>
/// Specifies whether flex items are forced onto one line or can wrap.
/// </summary>
public enum FlexWrap
{
    /// <summary>All items are laid out in a single line.</summary>
    NoWrap,

    /// <summary>Items wrap onto additional lines when they exceed the available space.</summary>
    Wrap,
}
