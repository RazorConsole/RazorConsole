// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Operations;

/// <summary>
/// Runs requests. The CLI verbs, and later an MCP server, call this and only format what comes back.
/// </summary>
internal interface ICliOperations
{
    /// <param name="request">What to do.</param>
    /// <param name="options">The global options.</param>
    /// <param name="cancellationToken">Cancelled by Ctrl+C.</param>
    /// <param name="stream">Where a verb that streams (<c>logs --follow</c>) writes its items as they come. The final
    /// result then carries no data of its own.</param>
    Task<CliResult> ExecuteAsync(CliRequest request, CliGlobalOptions options, CancellationToken cancellationToken, ICliStream? stream = null);
}

/// <summary>
/// Receives the items of a streaming verb: one NDJSON line each with <c>--json</c>, one text line each without.
/// </summary>
internal interface ICliStream
{
    Task WriteAsync(JsonElement item, string text);
}

/// <summary>
/// The outcome of one request: either a result (<see cref="Data"/> for <c>--json</c>, <see cref="Text"/> for people)
/// or an <see cref="Error"/> whose code decides the exit code.
/// </summary>
/// <param name="Error">The failure, or <see langword="null"/> on success.</param>
/// <param name="Session">The session the request ran against, if any.</param>
/// <param name="Data">The result fields, merged into the JSON envelope; a JSON object.</param>
/// <param name="Text">The human-readable result.</param>
internal sealed record CliResult(DiagnosticsError? Error, string? Session, JsonElement? Data, string? Text)
{
    public bool Ok => Error is null;

    public static CliResult Success(string? session, JsonElement? data, string? text) => new(null, session, data, text);

    public static CliResult Failure(string code, string message, JsonElement? data = null, string? session = null)
        => new(new DiagnosticsError(code, message, data), session, null, null);
}

/// <summary>
/// The operations before the session host exists: every request fails with <c>internal</c>.
/// </summary>
internal sealed class NotImplementedOperations : ICliOperations
{
    public Task<CliResult> ExecuteAsync(CliRequest request, CliGlobalOptions options, CancellationToken cancellationToken, ICliStream? stream = null)
        => Task.FromResult(CliResult.Failure(
            DiagnosticsErrorCodes.Internal,
            $"'{request.GetType().Name}' is not implemented yet."));
}
