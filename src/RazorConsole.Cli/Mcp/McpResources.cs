// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using RazorConsole.Cli.Commands;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Mcp;

/// <summary>
/// The resources of the MCP server: the session list, and the current screen and component tree of each session.
/// </summary>
internal sealed class McpResources(ICliOperations operations, CliGlobalOptions defaults)
{
    public const string SessionsUri = "razorconsole://sessions";
    public const string ScreenTemplate = "razorconsole://session/{name}/screen";
    public const string TreeTemplate = "razorconsole://session/{name}/tree";

    private const string Prefix = "razorconsole://session/";

    public static IReadOnlyList<ResourceTemplate> Templates { get; } =
    [
        new ResourceTemplate
        {
            Name = "screen",
            Title = "Screen of a session",
            Description = "The text on the session's screen now.",
            UriTemplate = ScreenTemplate,
            MimeType = "text/plain",
        },
        new ResourceTemplate
        {
            Name = "tree",
            Title = "Component tree of a session",
            Description = "The app's component tree with layout, as the get_tree tool returns it. Needs the app layer.",
            UriTemplate = TreeTemplate,
            MimeType = "application/json",
        },
    ];

    /// <summary>
    /// The sessions list, and the screen and tree of each running session. Never starts the session host.
    /// </summary>
    public async Task<ListResourcesResult> ListAsync(CancellationToken cancellationToken)
    {
        var resources = new List<Resource>
        {
            new()
            {
                Name = "sessions",
                Title = "Sessions",
                Description = "The sessions of the session host, as list_sessions returns them.",
                Uri = SessionsUri,
                MimeType = "application/json",
            },
        };

        try
        {
            var result = await operations.ExecuteAsync(new ListRequest(), Options(null), cancellationToken).ConfigureAwait(false);
            if (result.Data is { ValueKind: JsonValueKind.Object } data && data.TryGetProperty("sessions", out var sessions))
            {
                foreach (var session in sessions.EnumerateArray())
                {
                    if (session.TryGetProperty("name", out var name) && name.GetString() is { } text)
                    {
                        resources.Add(new Resource { Name = text + " screen", Uri = $"{Prefix}{text}/screen", MimeType = "text/plain" });
                        resources.Add(new Resource { Name = text + " tree", Uri = $"{Prefix}{text}/tree", MimeType = "application/json" });
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A listing that cannot reach the host still lists the static resource.
        }

        return new ListResourcesResult { Resources = resources };
    }

    public async Task<ReadResourceResult> ReadAsync(string uri, CancellationToken cancellationToken)
    {
        if (uri == SessionsUri)
        {
            var result = await RunAsync(new ListRequest(), null, cancellationToken).ConfigureAwait(false);
            return Contents(uri, "application/json", CliOutput.ToJson(result));
        }

        if (uri.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var parts = uri[Prefix.Length..].Split('/');
            if (parts is [var name, var kind] && CliValueParsers.TryValidateSessionName(name, out _))
            {
                if (kind == "screen")
                {
                    var result = await RunAsync(new ScreenRequest(ScreenFormat.Text, null), name, cancellationToken).ConfigureAwait(false);
                    return Contents(uri, "text/plain", ScreenText(result));
                }

                if (kind == "tree")
                {
                    var request = new TreeRequest(null, McpResultLimiter.DefaultTreeDepth, true, true);
                    var result = await RunAsync(request, name, cancellationToken).ConfigureAwait(false);
                    result = McpResultLimiter.Apply(McpShape.Tree, new McpBound(request, Options(name), TreeBudget: McpResultLimiter.DefaultTreeBudget), result);
                    return Contents(uri, "application/json", CliOutput.ToJson(result));
                }
            }
        }

        throw new McpProtocolException($"Unknown resource '{uri}'. Resources: {SessionsUri}, {ScreenTemplate}, {TreeTemplate}.", McpErrorCode.ResourceNotFound);
    }

    private CliGlobalOptions Options(string? session) => new(true, session ?? defaults.Session, defaults.Timeout);

    private async Task<CliResult> RunAsync(CliRequest request, string? session, CancellationToken cancellationToken)
    {
        var result = await operations.ExecuteAsync(request, Options(session), cancellationToken).ConfigureAwait(false);
        if (result.Error is { } failure)
        {
            // A resource has no isError, so a failure is a protocol error that still carries the CLI's code.
            var code = failure.Code is DiagnosticsErrorCodes.SessionNotFound ? McpErrorCode.ResourceNotFound : McpErrorCode.InternalError;
            throw new McpProtocolException($"{failure.Message} [{failure.Code}]", code);
        }

        return result;
    }

    private static string ScreenText(CliResult result)
    {
        var text = new StringBuilder();
        if (result.Data is { ValueKind: JsonValueKind.Object } data && data.TryGetProperty("lines", out var lines))
        {
            foreach (var line in lines.EnumerateArray())
            {
                if (text.Length > 0)
                {
                    text.Append('\n');
                }

                text.Append(line.GetString()?.TrimEnd());
            }
        }

        if (text.Length > McpResultLimiter.MaxResultCharacters)
        {
            var total = text.Length;
            text.Length = McpResultLimiter.MaxResultCharacters;
            text.Append($"\n[truncated: {McpResultLimiter.MaxResultCharacters} of {total} characters]");
        }

        return text.ToString();
    }

    private static ReadResourceResult Contents(string uri, string mimeType, string text)
        => new() { Contents = [new TextResourceContents { Uri = uri, MimeType = mimeType, Text = text }] };
}
