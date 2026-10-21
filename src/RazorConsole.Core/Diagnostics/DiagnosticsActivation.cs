// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Decides whether the diagnostics endpoint may run: on in Development or Debug builds unless opted out, off otherwise.
/// </summary>
internal static class DiagnosticsActivation
{
    internal const string EnvironmentVariableName = "RAZORCONSOLE_DIAGNOSTICS";

    public static bool IsEnabled(IHostEnvironment? environment, RazorConsoleDiagnostics mode)
        => IsEnabled(
            mode,
            isDevelopment: environment?.IsDevelopment() == true,
            environmentOverride: Environment.GetEnvironmentVariable(EnvironmentVariableName),
            entryAssemblyIsDebugBuild: IsDebugBuild(Assembly.GetEntryAssembly()),
            isBrowser: OperatingSystem.IsBrowser());

    internal static bool IsEnabled(
        RazorConsoleDiagnostics mode,
        bool isDevelopment,
        string? environmentOverride,
        bool entryAssemblyIsDebugBuild,
        bool isBrowser)
    {
        if (isBrowser || mode == RazorConsoleDiagnostics.Disabled || IsOptOut(environmentOverride))
        {
            return false;
        }

        return isDevelopment || entryAssemblyIsDebugBuild;
    }

    // Only opting out is recognised; any other value leaves the automatic rule in charge.
    internal static bool IsOptOut(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null
            && (trimmed == "0"
                || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "disabled", StringComparison.OrdinalIgnoreCase));
    }

    // Release builds carry DebuggableAttribute too, so only a disabled JIT optimizer marks a Debug build.
    // NativeAOT drops the attribute, so those apps rely on the Development environment instead.
    internal static bool IsDebugBuild(Assembly? assembly)
        => assembly?.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true;
}
