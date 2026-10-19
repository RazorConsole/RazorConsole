// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Testing;

/// <summary>
/// The screen buffer a terminal shows.
/// </summary>
public enum TerminalScreenBuffer
{
    /// <summary>
    /// The normal screen buffer.
    /// </summary>
    Normal,

    /// <summary>
    /// The alternate screen buffer (DEC private modes 47, 1047, and 1049).
    /// </summary>
    Alternate,
}
