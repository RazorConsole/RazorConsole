// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Controllers;
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
    /// Gets or sets whether the local diagnostics endpoint used by the <c>razorconsole</c> CLI may run.
    /// Defaults to <see cref="RazorConsoleDiagnostics.Auto"/>.
    /// </summary>
    /// <remarks>
    /// The endpoint only accepts connections from the current user. Not supported in WASM environment.
    /// </remarks>
    public RazorConsoleDiagnostics Diagnostics { get; set; } = RazorConsoleDiagnostics.Auto;

    /// <summary>
    /// Callback invoked after a component has been rendered.
    /// </summary>
    public Func<ConsoleLiveDisplayContext, ConsoleViewResult, CancellationToken, Task>? AfterRenderAsync { get; set; } = DefaultAfterRenderAsync;

    internal static Task DefaultAfterRenderAsync(ConsoleLiveDisplayContext context, ConsoleViewResult view, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

}
