// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Keeps recent log entries for the diagnostics endpoint, since Razor Console removes all console log providers.
/// </summary>
/// <remarks>
/// Registered only when diagnostics are enabled, with its own minimum level of <see cref="MinimumLevel"/>.
/// When <see cref="ConsoleAppOptions.Diagnostics"/> opts out there is no state, and every logger is a no-op.
/// </remarks>
internal sealed class DiagnosticsLoggerProvider(DiagnosticsState? state) : ILoggerProvider
{
    public const LogLevel MinimumLevel = LogLevel.Debug;

    public ILogger CreateLogger(string categoryName)
        => state is null ? NullLogger.Instance : new DiagnosticsLogger(categoryName, state.Logs);

    public void Dispose()
    {
    }

    private sealed class DiagnosticsLogger(string category, DiagnosticsRing<DiagnosticsLogEntry> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            logs.Add(new DiagnosticsLogEntry(
                DateTimeOffset.UtcNow,
                logLevel,
                category,
                eventId.Id,
                formatter(state, exception),
                exception?.ToString()));
        }
    }
}

internal sealed record DiagnosticsLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    int EventId,
    string Message,
    string? Exception);
