// Copyright (c) RazorConsole. All rights reserved.

using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RazorConsole.Cli.Mcp;
using RazorConsole.Cli.Operations;

namespace RazorConsole.Tests.Mcp;

/// <summary>
/// The MCP server of the CLI over in-memory pipes, with an SDK client on the other end and fake operations in place of
/// the session host.
/// </summary>
internal sealed class McpTestServer : IAsyncDisposable
{
    /// <summary>
    /// The protocol version the test client asks for unless a test says otherwise. Pinning it matters: a client that
    /// asks for no version first sends <c>server/discover</c> and, when that is not answered within its probe timeout
    /// (5s), falls back to <c>initialize</c> on the same connection. A server that did answer the late probe has
    /// settled on 2026-07-28 by then and rejects the <c>initialize</c>, which is a race on a loaded machine.
    /// </summary>
    public const string DefaultProtocolVersion = "2025-11-25";

    private readonly Pipe _toServer = new();
    private readonly Pipe _fromServer = new();
    private readonly CancellationTokenSource _stop = new();
    private Task<int> _server = Task.FromResult(0);

    private McpTestServer(FakeOperations operations)
    {
        Operations = operations;
    }

    public FakeOperations Operations { get; }

    public McpClient Client { get; private set; } = null!;

    public StringWriter Log { get; } = new();

    public static async Task<McpTestServer> StartAsync(Func<CliRequest, CliResult> execute, CliGlobalOptions? defaults = null, string protocolVersion = DefaultProtocolVersion)
        => await StartAsync(new FakeOperations((request, _, _) => Task.FromResult(execute(request))), defaults, protocolVersion);

    public static async Task<McpTestServer> StartAsync(FakeOperations operations, CliGlobalOptions? defaults = null, string protocolVersion = DefaultProtocolVersion)
    {
        var server = new McpTestServer(operations);
        server._server = McpServerHost.RunAsync(
            operations,
            defaults ?? new CliGlobalOptions(false, null, CliGlobalOptions.DefaultTimeout),
            server._toServer.Reader.AsStream(),
            server._fromServer.Writer.AsStream(),
            server.Log,
            server._stop.Token);
        var transport = new StreamClientTransport(server._toServer.Writer.AsStream(), server._fromServer.Reader.AsStream());
        server.Client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ClientInfo = new Implementation { Name = "tests", Version = "1" }, ProtocolVersion = protocolVersion },
            cancellationToken: TestContext.Current.CancellationToken);
        return server;
    }

    public async Task<CallToolResult> CallAsync(string tool, string arguments = "{}")
    {
        using var document = JsonDocument.Parse(arguments);
        var values = document.RootElement.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value.Clone());
        return await Client.CallToolAsync(new CallToolRequestParams { Name = tool, Arguments = values }, TestContext.Current.CancellationToken);
    }

    public static JsonElement Body(CallToolResult result)
    {
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _stop.CancelAsync();
        try
        {
            await _server.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
        }

        _stop.Dispose();
    }
}

/// <summary>
/// Operations that record what they were asked and answer from a function.
/// </summary>
internal sealed class FakeOperations(Func<CliRequest, CliGlobalOptions, CancellationToken, Task<CliResult>> execute) : ICliOperations
{
    public List<(CliRequest Request, CliGlobalOptions Options)> Requests { get; } = [];

    public Task<CliResult> ExecuteAsync(CliRequest request, CliGlobalOptions options, CancellationToken cancellationToken, ICliStream? stream = null)
    {
        lock (Requests)
        {
            Requests.Add((request, options));
        }

        return execute(request, options, cancellationToken);
    }
}
