// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace RazorConsole.Testing;

/// <summary>
/// Options for starting a <see cref="HeadlessTerminal"/>.
/// </summary>
public sealed class HeadlessTerminalOptions
{
    /// <summary>
    /// Gets or sets the terminal width in columns. Defaults to 80.
    /// </summary>
    public int Width { get; set; } = 80;

    /// <summary>
    /// Gets or sets the terminal height in rows. Defaults to 24.
    /// </summary>
    public int Height { get; set; } = 24;

    /// <summary>
    /// Gets or sets a callback that registers the application's own services.
    /// It runs after the RazorConsole services are registered, so it can also replace them.
    /// </summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    /// <summary>
    /// Gets or sets the parameters passed to the mounted component.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the ANSI output written for each frame is captured
    /// and exposed through <see cref="HeadlessTerminal.AnsiOutput"/>. Defaults to <see langword="false"/>.
    /// </summary>
    public bool CaptureAnsiOutput { get; set; }
}
