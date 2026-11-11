// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.ComponentMarkup;

/// <summary>
/// Represents a console renderable that requires periodic animation updates.
/// </summary>
/// <remarks>
/// All animations on screen share one refresh timer that runs at the shortest <see cref="RefreshInterval"/>
/// among them, so a renderable can be rendered more often than its own interval. It must pick its frame
/// from the current time when it renders (for example <c>DateTime.UtcNow.Ticks / RefreshInterval.Ticks</c>),
/// not from how many times it has been rendered.
/// </remarks>
public interface IAnimatedConsoleRenderable : IRenderable
{
    /// <summary>
    /// Gets how often the animation's frame changes. The display refreshes at least this often while the
    /// renderable is on screen, and possibly more often when a faster animation is shown at the same time.
    /// </summary>
    TimeSpan RefreshInterval { get; }
}
