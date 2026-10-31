// Copyright (c) RazorConsole. All rights reserved.

using System.CommandLine;
using System.Text.Json;
using RazorConsole.Cli.Commands;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli;

/// <summary>
/// One run of <c>razorconsole</c>: parse, run the request, write the result and pick the exit code. Everything the
/// process provides is passed in, so tests run it in-process.
/// </summary>
internal static class CliApplication
{
    /// <param name="args">The command line.</param>
    /// <param name="operations">Runs the parsed request.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="getEnvironmentVariable">Reads environment variables.</param>
    /// <param name="currentDirectory">The directory relative paths are resolved against.</param>
    /// <param name="cancellationToken">Cancelled by Ctrl+C.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        ICliOperations operations,
        TextWriter output,
        TextWriter error,
        Func<string, string?> getEnvironmentVariable,
        string currentDirectory,
        CancellationToken cancellationToken)
    {
        var outcome = new CliCommandLine(getEnvironmentVariable, currentDirectory).Parse(args);
        if (outcome.BuiltIn is { } builtIn)
        {
            return builtIn.Invoke(new InvocationConfiguration { Output = output, Error = error });
        }

        CliResult result;
        if (outcome.Errors.Count > 0)
        {
            var data = JsonSerializer.SerializeToElement(new UsageErrorData(outcome.Errors), CliJsonContext.Default.UsageErrorData);
            var message = outcome.Errors.Count == 1 ? outcome.Errors[0] : string.Join(Environment.NewLine, outcome.Errors);
            result = CliResult.Failure(DiagnosticsErrorCodes.Usage, message, data);
        }
        else
        {
            try
            {
                result = await operations.ExecuteAsync(outcome.Request!, outcome.Options, cancellationToken, new OutputStream(output, outcome.Options.Json)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result = CliResult.Failure(DiagnosticsErrorCodes.Cancelled, "The command was cancelled.", session: outcome.Options.Session);
            }
            catch (Exception exception)
            {
                // Every failure becomes a result, never a stack trace.
                result = CliResult.Failure(DiagnosticsErrorCodes.Internal, exception.Message, session: outcome.Options.Session);
            }
        }

        CliOutput.Write(result, outcome.Options.Json, output, error);
        return result.Error is { } failure ? CliExitCodes.For(failure.Code) : CliExitCodes.Success;
    }

    // Each item of a streaming verb is flushed as it arrives, so a reader on a pipe sees it at once.
    private sealed class OutputStream(TextWriter output, bool json) : ICliStream
    {
        public async Task WriteAsync(JsonElement item, string text)
        {
            await output.WriteLineAsync(json ? item.GetRawText() : text).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
    }
}
