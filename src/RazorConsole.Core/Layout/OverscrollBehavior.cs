// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

/// <summary>
/// Controls what happens when a scroll container is already at its limit and the user keeps scrolling, like CSS <c>overscroll-behavior</c>.
/// </summary>
public enum OverscrollBehavior
{
    /// <summary>The scroll chains to the nearest scrollable ancestor.</summary>
    Auto,

    /// <summary>The scroll stops at this container; ancestors do not move.</summary>
    Contain,
}
