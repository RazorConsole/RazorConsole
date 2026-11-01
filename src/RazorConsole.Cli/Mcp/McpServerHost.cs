// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RazorConsole.Cli.Host;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Mcp;

/// <summary>
/// The MCP server of <c>razorconsole mcp</c>: tools, resources and prompts over the operations the CLI verbs use, so an
/// agent drives the same session host and sees the same sessions as a developer in a terminal.
/// </summary>
/// <remarks>
/// Standard output carries only protocol messages. A tool result is the JSON the verb prints with <c>--json</c>; a failed
/// operation is a result with <c>isError</c> and the same error code, message and data. Only a malformed request, an
/// unknown tool, prompt or resource is a JSON-RPC error. <c>notifications/cancelled</c> cancels the running operation's
/// token, which the host client forwards to the host like Ctrl+C does.
/// </remarks>
internal sealed class McpServerHost
{
    public const string ServerName = "razorconsole";

    public const string Instructions = """
        Drives RazorConsole terminal apps in pseudoterminal sessions, the same ones the `razorconsole` command line shows a developer.

        Always pass `session` (the name start_session returned) to every session tool: the "current" session is shared with the developer and may be theirs.

        The loop: start_session, wait_for the first text, screenshot and get_tree to see, then act (click, send_keys, type_text) and wait_for the result instead of sleeping. Finish with stop_session.

        Results are JSON: {"ok":true,...} or, with isError, {"ok":false,"error":{"code","message","data"}}. Useful codes: element_not_found and ambiguous_selector (fix the selector; ambiguous lists candidates), timeout (data.screen is the screen when it gave up), process_exited (data.outputTail is where .NET prints a crash), session_not_found, host_unavailable (run doctor), terminal_layer_unavailable (an attached session has no terminal). assert answers passed:false instead of failing.

        Selectors are `<kind>:<value>`: hook:<name> (most stable), focus:<key>, key:<key>, id:<id>, text:<substring>, text~:<regex>, tag:<name>[attr=value], path:/0/2/1. The tree, find, inspect, focus, route, errors and logs tools need the app layer: an app run in Development or Debug.

        Long results are cut and carry a `truncated` array that says what was cut and how to get the rest. get_logs, get_errors and get_output page with `since` and `next`.

        Waits and asserts always have a timeout (default 10s, at most 5 minutes). A cancelled call stops the operation.
        """;

    private readonly ICliOperations _operations;
    private readonly CliGlobalOptions _defaults;
    private readonly McpResources _resources;
    private readonly IReadOnlyList<Tool> _listing;

    public McpServerHost(ICliOperations operations, CliGlobalOptions defaults)
    {
        _operations = operations;
        _defaults = defaults;
        _resources = new McpResources(operations, defaults);
        _listing = McpTools.All.Select(static tool => tool.ToProtocol()).ToList();
    }

    /// <summary>
    /// Serves MCP on the two streams until the client closes <paramref name="input"/> or <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    /// <returns>The exit code: 0 when the client closed the connection.</returns>
    public static async Task<int> RunAsync(
        ICliOperations operations,
        CliGlobalOptions defaults,
        Stream input,
        Stream output,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        var host = new McpServerHost(operations, defaults);
        var loggerFactory = new StderrLoggerFactory(log);
        var options = host.CreateOptions();
        await using var transport = new StreamServerTransport(input, output, ServerName, loggerFactory);
        await using var server = McpServer.Create(transport, options, loggerFactory);
        await server.RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public McpServerOptions CreateOptions() => new()
    {
        ServerInfo = new Implementation { Name = ServerName, Version = HostFiles.ToolVersion },
        ServerInstructions = Instructions,
        Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Resources = new ResourcesCapability(),
            Prompts = new PromptsCapability(),
        },
        Handlers = new McpServerHandlers
        {
            ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [.. _listing] }),
            CallToolHandler = (request, cancellationToken) => CallToolAsync(request.Params?.Name, request.Params?.Arguments, cancellationToken),
            ListResourcesHandler = async (_, cancellationToken) => await _resources.ListAsync(cancellationToken).ConfigureAwait(false),
            ListResourceTemplatesHandler = (_, _) => ValueTask.FromResult(new ListResourceTemplatesResult { ResourceTemplates = [.. McpResources.Templates] }),
            ReadResourceHandler = async (request, cancellationToken)
                => await _resources.ReadAsync(request.Params?.Uri ?? string.Empty, cancellationToken).ConfigureAwait(false),
            ListPromptsHandler = (_, _) => ValueTask.FromResult(new ListPromptsResult { Prompts = [.. McpPrompts.List] }),
            GetPromptHandler = (request, _) => ValueTask.FromResult(McpPrompts.Get(request.Params?.Name ?? string.Empty, request.Params?.Arguments)),
        },
    };

    /// <summary>
    /// Runs one tool call.
    /// </summary>
    /// <exception cref="McpProtocolException">The tool does not exist.</exception>
    /// <exception cref="OperationCanceledException">The call was cancelled; the server sends no answer for it.</exception>
    public async ValueTask<CallToolResult> CallToolAsync(string? name, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var tool = (name is null ? null : McpTools.Find(name))
            ?? throw new McpProtocolException($"Unknown tool '{name}'.", McpErrorCode.InvalidParams);

        var bound = tool.TryBind(arguments, _defaults, out var errors);
        if (bound is null)
        {
            var data = JsonSerializer.SerializeToElement(new UsageErrorData(errors), CliJsonContext.Default.UsageErrorData);
            return ToToolResult(CliResult.Failure(DiagnosticsErrorCodes.Usage, string.Join(" ", errors), data));
        }

        CliResult result;
        try
        {
            result = await _operations.ExecuteAsync(bound.Request, bound.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Every failure becomes a result, never a stack trace.
            result = CliResult.Failure(DiagnosticsErrorCodes.Internal, exception.Message, session: bound.Options.Session);
        }

        return ToToolResult(Shape(tool, bound, result));
    }

    private static CliResult Shape(McpTool tool, McpBound bound, CliResult result)
    {
        switch (tool.Shape)
        {
            case McpShape.SessionInfo:
                return OneSession(bound.Options.Session, result);

            case McpShape.Assert when result.Error is { Code: DiagnosticsErrorCodes.AssertionFailed } failure:
            {
                // A condition that does not hold is an answer, not a failure of the tool. The envelope owns `error`,
                // so the explanation is `failure`.
                var node = new JsonObject
                {
                    ["passed"] = false,
                    ["failure"] = new JsonObject { ["code"] = failure.Code, ["message"] = failure.Message },
                };
                if (failure.Data is { } data)
                {
                    node["failure"]!["data"] = JsonNode.Parse(data.GetRawText());
                }

                using var document = JsonDocument.Parse(node.ToJsonString());
                return CliResult.Success(result.Session, document.RootElement.Clone(), failure.Message);
            }

            default:
                return McpResultLimiter.Apply(tool.Shape, bound, result);
        }
    }

    // There is no host call for one session; the list has it.
    private static CliResult OneSession(string? name, CliResult result)
    {
        if (result.Error is not null)
        {
            return result;
        }

        if (name is null)
        {
            return CliResult.Failure(DiagnosticsErrorCodes.Usage, "session: is required.");
        }

        if (result.Data is { ValueKind: JsonValueKind.Object } data && data.TryGetProperty("sessions", out var sessions))
        {
            foreach (var session in sessions.EnumerateArray())
            {
                if (session.TryGetProperty("name", out var found) && found.GetString() == name)
                {
                    return CliResult.Success(name, session, null);
                }
            }
        }

        return CliResult.Failure(DiagnosticsErrorCodes.SessionNotFound, $"There is no session named '{name}'.", session: name);
    }

    private static CallToolResult ToToolResult(CliResult result) => new()
    {
        IsError = !result.Ok,
        Content = [new TextContentBlock { Text = CliOutput.ToJson(result) }],
    };
}
