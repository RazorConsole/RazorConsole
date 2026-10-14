// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Controllers;
using RazorConsole.Core.Rendering;

namespace RazorConsole.Core;

public enum RazorConsoleRenderingPipeline
{
    /// <summary>
    /// The deprecated Spectre.Console-based layout pipeline, retained for compatibility until its complete removal in v1.1.0.
    /// Use <see cref="WidgetLayout"/> for layout metadata and coordinate-aware mouse events.
    /// </summary>
    [Obsolete("The legacy Spectre layout pipeline is deprecated and will be completely removed in v1.1.0. Use WidgetLayout instead.")]
    LegacySpectre,

    /// <summary>
    /// The default widget-based layout pipeline.
    /// </summary>
    WidgetLayout,
}

/// <summary>
/// Options that control how console applications render output.
/// </summary>
public sealed class ConsoleAppOptions
{
    private const string RenderingPipelineEnvironmentVariableName = "RAZORCONSOLE_RENDERING_PIPELINE";

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
    /// Gets or sets the rendering pipeline. Defaults to <see cref="RazorConsoleRenderingPipeline.WidgetLayout"/>
    /// unless overridden by the RAZORCONSOLE_RENDERING_PIPELINE environment variable.
    /// </summary>
    /// <remarks>
    /// The legacy Spectre layout pipeline is deprecated and will be completely removed in v1.1.0.
    /// Use WidgetLayout for new applications.
    /// </remarks>
    public RazorConsoleRenderingPipeline RenderingPipeline { get; set; } = ResolveDefaultRenderingPipeline();

    /// <summary>
    /// Callback invoked after a component has been rendered.
    /// </summary>
    public Func<ConsoleLiveDisplayContext, ConsoleViewResult, CancellationToken, Task>? AfterRenderAsync { get; set; } = DefaultAfterRenderAsync;

    internal static Task DefaultAfterRenderAsync(ConsoleLiveDisplayContext context, ConsoleViewResult view, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static RazorConsoleRenderingPipeline ResolveDefaultRenderingPipeline()
    {
        var value = Environment.GetEnvironmentVariable(RenderingPipelineEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return RazorConsoleRenderingPipeline.WidgetLayout;
        }

        if (string.Equals(value, "widget", StringComparison.OrdinalIgnoreCase))
        {
            return RazorConsoleRenderingPipeline.WidgetLayout;
        }

        if (string.Equals(value, "legacy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "spectre", StringComparison.OrdinalIgnoreCase))
        {
#pragma warning disable CS0618 // Preserve deprecated environment variable aliases until removal in v1.1.0.
            return RazorConsoleRenderingPipeline.LegacySpectre;
#pragma warning restore CS0618
        }

        return Enum.TryParse<RazorConsoleRenderingPipeline>(value, ignoreCase: true, out var pipeline)
            ? pipeline
            : RazorConsoleRenderingPipeline.WidgetLayout;
    }
}
