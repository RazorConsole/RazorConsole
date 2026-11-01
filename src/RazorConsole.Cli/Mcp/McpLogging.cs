// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Logging;

namespace RazorConsole.Cli.Mcp;

/// <summary>
/// Writes the MCP SDK's warnings and errors to standard error. Standard output belongs to the protocol, so nothing
/// else may ever be written there.
/// </summary>
internal sealed class StderrLoggerFactory(TextWriter error, LogLevel minimum = LogLevel.Warning) : ILoggerFactory
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new StderrLogger(this, categoryName);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private void Write(string category, LogLevel level, string message)
    {
        lock (_gate)
        {
            error.WriteLine($"razorconsole mcp: {level.ToString().ToLowerInvariant()}: {category}: {message}");
            error.Flush();
        }
    }

    private sealed class StderrLogger(StderrLoggerFactory factory, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= factory._minimum && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            factory.Write(category, logLevel, exception is null ? message : $"{message} {exception.GetType().Name}: {exception.Message}");
        }
    }

    private LogLevel _minimum => minimum;
}
