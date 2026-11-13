// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Rendering;

/// <summary>
/// Where the scrollbar of a scroll container is drawn.
/// </summary>
public enum ScrollbarPlacement
{
    /// <summary>
    /// In one column reserved inside the container, only while the content overflows. This is the default.
    /// </summary>
    Inset,

    /// <summary>
    /// In one column added to the right of the container's border.
    /// </summary>
    Outside,
}

/// <summary>
/// When the scrollbar of a scroll container is shown.
/// </summary>
public enum ScrollbarVisibility
{
    /// <summary>Shown only while the content overflows the container.</summary>
    Auto,

    /// <summary>Always shown, even when the content fits.</summary>
    Always,

    /// <summary>Never shown. The container can still be scrolled by other means.</summary>
    Never,
}
