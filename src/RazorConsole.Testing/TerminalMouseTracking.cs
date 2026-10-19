// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Testing;

/// <summary>
/// The mouse reporting modes an application has enabled in the terminal.
/// </summary>
[Flags]
public enum TerminalMouseTracking
{
    /// <summary>
    /// No mouse reporting.
    /// </summary>
    None = 0,

    /// <summary>
    /// Button press and release reporting (DEC private mode 1000).
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Motion reporting while a button is held (DEC private mode 1002).
    /// </summary>
    ButtonEvent = 2,

    /// <summary>
    /// Reporting of all motion (DEC private mode 1003).
    /// </summary>
    AnyEvent = 4,

    /// <summary>
    /// SGR extended coordinates (DEC private mode 1006).
    /// </summary>
    SgrEncoding = 8,
}
