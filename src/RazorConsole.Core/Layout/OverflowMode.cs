// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

/// <summary>
/// Controls what a container does with content that is taller than the container, like CSS <c>overflow</c>.
/// </summary>
/// <remarks>Named <c>OverflowMode</c> because <c>Overflow</c> clashes with <c>Spectre.Console.Overflow</c>.</remarks>
public enum OverflowMode
{
    /// <summary>The container grows with its content and nothing is clipped.</summary>
    Visible,

    /// <summary>Content taller than the container is clipped and cannot be scrolled.</summary>
    Hidden,

    /// <summary>Content taller than the container is clipped and can be scrolled.</summary>
    Scroll,
}
