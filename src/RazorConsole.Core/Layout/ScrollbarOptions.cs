// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering;
using Spectre.Console;

namespace RazorConsole.Core.Layout;

/// <summary>
/// How a <see cref="BoxWidget"/> that hosts a <see cref="ScrollViewportWidget"/> draws its scrollbar.
/// </summary>
public sealed record ScrollbarOptions(
    ScrollbarPlacement Placement = ScrollbarPlacement.Border,
    ScrollbarVisibility Visibility = ScrollbarVisibility.Auto,
    char TrackChar = '│',
    char ThumbChar = '█',
    Style? TrackStyle = null,
    Style? ThumbStyle = null,
    int MinThumbHeight = 1);

/// <summary>
/// The painted scrollbar of a scroll container, published by layout so the pointer can click and drag it.
/// </summary>
/// <param name="ScrollId">Scroll state key, or <see langword="null"/> when the container has no engine-owned state.</param>
/// <param name="Viewport">The rows that show content.</param>
/// <param name="Track">The track, in document coordinates.</param>
/// <param name="Thumb">The thumb inside the track; empty when there is nothing to scroll.</param>
/// <param name="Top">The applied scroll offset.</param>
/// <param name="ContentHeight">The height of the content.</param>
/// <param name="ViewportHeight">The height of the visible window.</param>
public sealed record ScrollLayoutInfo(
    string? ScrollId,
    LayoutRect Viewport,
    LayoutRect Track,
    LayoutRect Thumb,
    int Top,
    int ContentHeight,
    int ViewportHeight);
