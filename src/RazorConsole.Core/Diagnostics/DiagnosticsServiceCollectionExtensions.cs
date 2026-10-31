// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RazorConsole.Core.Diagnostics;

internal static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the diagnostics services when <see cref="DiagnosticsActivation"/> allows them for this process.
    /// Otherwise registers nothing, so a disabled app resolves no diagnostics type at all.
    /// </summary>
    public static IServiceCollection AddRazorConsoleDiagnostics(this IServiceCollection services, IHostEnvironment? environment)
        => services.AddRazorConsoleDiagnostics(DiagnosticsActivation.IsEnabled(environment, RazorConsoleDiagnostics.Auto));

    internal static IServiceCollection AddRazorConsoleDiagnostics(this IServiceCollection services, bool enabled)
    {
        if (!enabled)
        {
            return services;
        }

        // ConsoleAppOptions is configured after registration, so its opt-out is applied here, when first resolved.
        // A null state keeps every consumer on its disabled path.
        services.TryAddSingleton<DiagnosticsState>(sp =>
            sp.GetRequiredService<ConsoleAppOptions>().Diagnostics == RazorConsoleDiagnostics.Disabled
                ? null!
                : new DiagnosticsState());

        services.AddHostedService<DiagnosticsEndpointService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DiagnosticsLoggerProvider>());
        services.Configure<LoggerFilterOptions>(options =>
            options.AddFilter<DiagnosticsLoggerProvider>(category: null, DiagnosticsLoggerProvider.MinimumLevel));

        return services;
    }
}
