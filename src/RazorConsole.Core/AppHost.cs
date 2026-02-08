// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Utilities;
using Spectre.Console;

namespace RazorConsole.Core;

/// <summary>
/// Extension methods for wiring Razor Console into generic host builders.
/// </summary>
public static class HostBuilderExtension
{
    /// <summary>
    /// Adds Razor Console services to the specified <see cref="IHostBuilder"/> using the provided root component.
    /// </summary>
    /// <typeparam name="TComponent">The Razor component that acts as the application's root component.</typeparam>
    /// <param name="hostBuilder">The host builder to configure.</param>
    /// <param name="configure">An optional callback to perform additional configuration.</param>
    /// <returns>The configured <see cref="IHostBuilder"/> instance.</returns>
    public static IHostBuilder UseRazorConsole
        <[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>
    (
        this IHostBuilder hostBuilder,
        Action<IHostBuilder>? configure = null
    )
        where TComponent : IComponent
    {
        RuntimeEncoding.EnsureUtf8();
        hostBuilder.ConfigureServices(RegisterDefaults<TComponent>);
        configure?.Invoke(hostBuilder);

        return hostBuilder;
    }

    /// <summary>
    /// Adds Razor Console services to the specified <see cref="IHostApplicationBuilder"/> using the provided root component.
    /// </summary>
    /// <typeparam name="TComponent">The Razor component that acts as the application's root component.</typeparam>
    /// <param name="hostBuilder">The host application builder to configure.</param>
    /// <param name="configure">An optional callback to perform additional configuration.</param>
    /// <returns>The configured <see cref="IHostApplicationBuilder"/> instance.</returns>
    public static IHostApplicationBuilder UseRazorConsole
    <[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>
    (
        this IHostApplicationBuilder hostBuilder,
        Action<IHostApplicationBuilder>? configure = null
    )
        where TComponent : IComponent
    {
        RuntimeEncoding.EnsureUtf8();
        RegisterDefaults<TComponent>(hostBuilder.Services);

        configure?.Invoke(hostBuilder);

        return hostBuilder;
    }

    private static void RegisterDefaults<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    TComponent>(IServiceCollection services) where TComponent : IComponent
    {
        services.AddRazorConsoleServices();
        services.AddHostedService<ComponentService<TComponent>>();

        // clear all log providers because it would interfere with console rendering
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
        });
    }
}

internal class ComponentService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
    ConsoleAppOptions options,
    ConsoleRenderer consoleRenderer,
    FocusManager focusManager,
    KeyboardEventManager keyboardEventManager,
    TerminalMonitor terminalMonitor) : BackgroundService where TComponent : IComponent
{
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    /// <summary>
    /// Ensures exceptions that occur during component execution are surfaced when the host stops.
    /// </summary>
    /// <param name="cancellationToken">A token that requests the stop operation to cancel.</param>
    /// <returns>A task that completes when background processing has stopped.</returns>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // Bubble exceptions up into Host.StopAsync, invoked when Host self-stops when a BackgroundService throws
        if (ExecuteTask?.Exception is not null)
        {
            var flattened = ExecuteTask.Exception.Flatten();
            if (flattened.InnerException is not null)
            {
                throw flattened.InnerException;
            }
            else
            {
                throw flattened;
            }
        }

        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var initialView = await RenderComponentInternalAsync(token).ConfigureAwait(false);

        var callback = options.AfterRenderAsync ?? ConsoleAppOptions.DefaultAfterRenderAsync;

        if (options.AutoClearConsole)
        {
            AnsiConsole.Clear();
        }

        using var liveContext = new ConsoleLiveDisplayContext(new LiveDisplayCanvas(AnsiConsole.Console), consoleRenderer, terminalMonitor, null);
        using var _ = consoleRenderer.Subscribe(focusManager);
        using var focusSession = focusManager.BeginSession(liveContext, initialView, token);
        await focusSession.InitializationTask.ConfigureAwait(false);
        var keyListenerTask = keyboardEventManager.RunAsync(token);
        if (options.EnableTerminalResizing)
        {
            terminalMonitor.Start(token);
        }

        await callback(liveContext, initialView, token).ConfigureAwait(false);

        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
    }

    private async Task<ConsoleViewResult> RenderComponentInternalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _renderLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var parameterView = CreateParameterView();
            var snapshot = await consoleRenderer.MountComponentAsync<TComponent>(parameterView, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            return ConsoleViewResult.FromSnapshot(snapshot);
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private static ParameterView CreateParameterView() => ParameterView.Empty;
}
