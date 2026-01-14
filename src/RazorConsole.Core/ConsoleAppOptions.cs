// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Controllers;
using RazorConsole.Core.Rendering;

namespace RazorConsole.Core;

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
    /// Callback invoked after a component has been rendered.
    /// </summary>
    public Func<ConsoleLiveDisplayContext, ConsoleViewResult, CancellationToken, Task>? AfterRenderAsync { get; set; } = DefaultAfterRenderAsync;

    internal static Task DefaultAfterRenderAsync(ConsoleLiveDisplayContext context, ConsoleViewResult view, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
