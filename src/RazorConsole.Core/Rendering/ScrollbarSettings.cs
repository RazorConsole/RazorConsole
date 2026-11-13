// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components.Web;
using Spectre.Console;

namespace RazorConsole.Core.Rendering;

public sealed record ScrollbarSettings
{
    public ScrollbarSettings(
        char trackChar = '│',
        char thumbChar = '█',
        Color? trackColor = null,
        Color? thumbColor = null,
        Color? trackFocusedColor = null,
        Color? thumbFocusedColor = null,
        int minThumbHeight = 1,
        Action<FocusEventArgs>? onFocusInCallback = null,
        Action<FocusEventArgs>? onFocusOutCallback = null,
        ScrollbarPlacement placement = ScrollbarPlacement.Inset,
        ScrollbarVisibility visibility = ScrollbarVisibility.Auto)
    {
        Placement = placement;
        Visibility = visibility;
        TrackChar = trackChar;
        ThumbChar = thumbChar;
        TrackColor = trackColor ?? Color.Grey;
        ThumbColor = thumbColor ?? Color.White;
        TrackFocusedColor = trackFocusedColor ?? Color.Grey74;
        ThumbFocusedColor = thumbFocusedColor ?? Color.DeepSkyBlue1;
        MinThumbHeight = minThumbHeight;
        OnFocusInCallback = onFocusInCallback;
        OnFocusOutCallback = onFocusOutCallback;
    }

    /// <summary>
    /// Where a scroll container draws its scrollbar. Default is <see cref="ScrollbarPlacement.Inset"/>.
    /// </summary>
    public ScrollbarPlacement Placement { get; set; } = ScrollbarPlacement.Inset;

    /// <summary>
    /// When a scroll container shows its scrollbar. Default is <see cref="ScrollbarVisibility.Auto"/>.
    /// </summary>
    public ScrollbarVisibility Visibility { get; set; } = ScrollbarVisibility.Auto;

    /// <summary>
    /// Character to use for the scrollbar track. Default is │.
    /// </summary>
    public char TrackChar { get; set; } = '│';

    /// <summary>
    /// Character to use for the scrollbar thumb. Default is █.
    /// </summary>
    public char ThumbChar { get; set; } = '█';

    /// <summary>
    /// Color for the track. Default is <see cref="Color.Gray"/>.
    /// </summary>
    public Color TrackColor { get; set; } = Color.Grey;

    /// <summary>
    /// Color for the thumb. Default is <see cref="Color.White"/>.
    /// </summary>
    public Color ThumbColor { get; set; } = Color.White;

    /// <summary>
    /// Color for the track when scrollbar is focused. Default is <see cref="Color.Gray74"/>.
    /// </summary>
    public Color TrackFocusedColor { get; set; } = Color.Grey74;

    /// <summary>
    /// Color for the track when scrollbar is focused. Default is <see cref="Color.DeepSkyBlue1"/>.
    /// </summary>
    public Color ThumbFocusedColor { get; set; } = Color.DeepSkyBlue1;

    /// <summary>
    /// Minimum height for the thumb in characters.
    /// </summary>
    public int MinThumbHeight { get; set; } = 1;

    /// <summary>
    /// Callback, that called after focus in event, can be used for any UI changes outside scrollbar.
    /// </summary>
    public Action<FocusEventArgs>? OnFocusInCallback { get; set; }

    /// <summary>
    /// Callback, that called after focus out event, can be used for any UI changes outside scrollbar.
    /// </summary>
    public Action<FocusEventArgs>? OnFocusOutCallback { get; set; }
}
