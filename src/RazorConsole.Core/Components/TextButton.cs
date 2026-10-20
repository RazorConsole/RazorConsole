// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Spectre.Console;

namespace RazorConsole.Components;

/// <summary>
/// Describes an interactive text button without producing markup.
/// </summary>
public class TextButton : ComponentBase
{
    private bool _isHovered;

    internal bool IsFocused { get; private set; }
    internal bool IsHovered => _isHovered && OnClick.HasDelegate;

    /// <summary>Background used when the pointer is over an actionable button.</summary>
    [Parameter] public Color HoverColor { get; set; } = Color.Grey70;

    /// <summary>Text color paired with <see cref="HoverColor"/>.</summary>
    [Parameter] public Color HoverForegroundColor { get; set; } = Color.Black;

    /// <summary>Background used when the button has focus.</summary>
    [Parameter] public Color FocusedColor { get; set; } = Color.DeepSkyBlue1;

    /// <summary>Background used when the button is neither focused nor hovered.</summary>
    [Parameter] public Color BackgroundColor { get; set; } = Color.Default;

    /// <summary>Plain text displayed on the button.</summary>
    [Parameter] public string Content { get; set; } = string.Empty;

    /// <summary>Callback invoked when the button is activated.</summary>
    [Parameter] public EventCallback OnClick { get; set; }

    /// <summary>Tab order. A null value uses document order.</summary>
    [Parameter] public int? FocusOrder { get; set; }

    // Infrastructure invokes these through EventCallback so ComponentBase schedules rendering.
    internal void SetFocused(bool focused) => IsFocused = focused;
    internal void SetHovered(bool hovered) => _isHovered = hovered;
}
