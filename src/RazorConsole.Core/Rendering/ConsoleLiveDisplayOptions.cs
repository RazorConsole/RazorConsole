// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Rendering;

/// <summary>
/// Provides configuration settings for live display rendering.
/// </summary>
public sealed class ConsoleLiveDisplayOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the live display should automatically clear the console when it completes.
    /// </summary>
    public bool AutoClear { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the live display should automatically hide cursor after render or not.
    /// </summary>
    /// <remarks>
    /// By default, is true
    /// </remarks>
    public bool HideCursor { get; set; } = true;

    /// <summary>
    /// Creates a new instance with default settings.
    /// </summary>
    public static ConsoleLiveDisplayOptions Default => new();

    internal ConsoleLiveDisplayOptions Clone()
        => new() { AutoClear = AutoClear, HideCursor = HideCursor };
}
