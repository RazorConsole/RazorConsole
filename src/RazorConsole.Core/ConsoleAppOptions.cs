// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering;

namespace RazorConsole.Core;

/// <summary>
/// Controls the local diagnostics endpoint that the <c>razorconsole</c> CLI connects to.
/// </summary>
public enum RazorConsoleDiagnostics
{
    /// <summary>
    /// Enabled when the host environment is Development or the entry assembly is a Debug build; otherwise disabled.
    /// The RAZORCONSOLE_DIAGNOSTICS environment variable can still turn it off with <c>0</c>, <c>false</c>, <c>off</c> or <c>disabled</c>.
    /// </summary>
    Auto,

    /// <summary>
    /// Never enabled.
    /// </summary>
    Disabled,
}

/// <summary>
/// Options that control how console applications render output.
/// </summary>
public sealed class ConsoleAppOptions
{
    /// <summary>
    /// Gets or sets whether the console should be cleared before writing output.
    /// </summary>
    public bool AutoClearConsole { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the console should be re-rendered after resizing.
    /// Default value is false.
    /// </summary>
    /// <remarks>
    /// Not supported in WASM environment.
    /// </remarks>
    public bool EnableTerminalResizing { get; set; } = false;

    public ConsoleLiveDisplayOptions ConsoleLiveDisplayOptions { get; } = ConsoleLiveDisplayOptions.Default;

    /// <summary>
    /// Gets or sets whether input waits for each event handler to finish before it handles the next key or mouse event.
    /// Default value is false.
    /// </summary>
    /// <remarks>
    /// By default RazorConsole handles events the way Blazor does: an event is dispatched once the previous
    /// handler has started (its synchronous part has run), so a click or key press is handled while an earlier
    /// <c>async</c> handler is still awaiting, for example a Cancel button during long-running work.
    /// Set this to <see langword="true"/> to restore the earlier behavior, where an <c>async</c> handler
    /// blocks all later input until its <see cref="Task"/> completes.
    /// </remarks>
    public bool SerializeEventHandlers { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the local diagnostics endpoint used by the <c>razorconsole</c> CLI may run.
    /// Defaults to <see cref="RazorConsoleDiagnostics.Auto"/>.
    /// </summary>
    /// <remarks>
    /// The endpoint only accepts connections from the current user. Not supported in WASM environment.
    /// </remarks>
    public RazorConsoleDiagnostics Diagnostics { get; set; } = RazorConsoleDiagnostics.Auto;
}
