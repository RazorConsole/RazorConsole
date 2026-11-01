// Copyright (c) RazorConsole. All rights reserved.

using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using RazorConsole.Cli.Mcp;
using RazorConsole.Cli.Operations;

namespace RazorConsole.Tests.Mcp;

/// <summary>
/// <c>razorconsole mcp</c> serves every protocol version of the SDK: the four that open with <c>initialize</c> and
/// 2026-07-28, which opens with <c>server/discover</c> and carries its metadata in every request.
/// </summary>
public sealed class McpHandshakeTests
{
    public static TheoryData<string> Versions => new()
    {
        "2024-11-05",
        "2025-03-26",
        "2025-06-18",
        "2025-11-25",
        "2026-07-28",
    };

    [Theory(Timeout = 60_000)]
    [MemberData(nameof(Versions))]
    public async Task AClientPinnedToAVersion_ConnectsListsToolsAndCallsOne(string version)
    {
        await using var server = await McpTestServer.StartAsync(static _ => CliResult.Success(null, null, null), protocolVersion: version);

        server.Client.NegotiatedProtocolVersion.ShouldBe(version);
        server.Client.ServerInfo!.Name.ShouldBe("razorconsole");
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        tools.Select(static tool => tool.Name).ShouldBe(McpTools.All.Select(static tool => tool.Name));

        var result = await server.CallAsync("doctor");
        result.IsError.ShouldNotBe(true);
    }

    [Fact(Timeout = 60_000)]
    public async Task ARawServerDiscover_IsAnsweredWithTheServerAndTheVersionsItSupports()
    {
        await using var wire = new Wire();

        var answer = await wire.RequestAsync(1, "server/discover", Meta("2026-07-28"));

        var result = answer.GetProperty("result");
        result.GetProperty("supportedVersions").EnumerateArray().Select(static version => version.GetString()).ShouldContain("2026-07-28");
        result.GetProperty("capabilities").GetProperty("tools").ValueKind.ShouldBe(JsonValueKind.Object);
        var listed = await wire.RequestAsync(2, "tools/list", Meta("2026-07-28"));
        listed.GetProperty("result").GetProperty("tools").GetArrayLength().ShouldBe(McpTools.All.Count);
    }

    [Fact(Timeout = 60_000)]
    public async Task ARawInitialize_IsAnsweredLikeTheSmokeTestSendsIt()
    {
        await using var wire = new Wire();

        var answer = await wire.RequestAsync(1, "initialize", """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw","version":"1"}}""");

        var result = answer.GetProperty("result");
        result.GetProperty("serverInfo").GetProperty("name").GetString().ShouldBe("razorconsole");
        result.GetProperty("protocolVersion").GetString().ShouldBe("2025-06-18");
    }

    // The cause of the CI failure, pinned: once the server has answered server/discover it works in 2026-07-28 and
    // rejects initialize. A default client that probes with discover, gives up waiting after its probe timeout and
    // falls back to initialize on the same connection meets exactly this when the server was only late.
    [Fact(Timeout = 60_000)]
    public async Task InitializeAfterAnAnsweredDiscover_IsRejected_SoClientsMustNotMixTheTwoOnOneConnection()
    {
        await using var wire = new Wire();
        await wire.RequestAsync(1, "server/discover", Meta("2026-07-28"));

        var answer = await wire.RequestAsync(2, "initialize", """{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"raw","version":"1"}}""");

        answer.GetProperty("error").GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("not available on protocol version '2026-07-28'");
    }

    private static string Meta(string version)
        => """{"_meta":{"io.modelcontextprotocol/protocolVersion":"VERSION","io.modelcontextprotocol/clientInfo":{"name":"raw","version":"1"},"io.modelcontextprotocol/clientCapabilities":{}}}""".Replace("VERSION", version, StringComparison.Ordinal);

    // The server on in-memory pipes, spoken to line by line.
    private sealed class Wire : IAsyncDisposable
    {
        private readonly Pipe _toServer = new();
        private readonly Pipe _fromServer = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly Task<int> _server;

        public Wire()
        {
            _server = McpServerHost.RunAsync(
                new FakeOperations((_, _, _) => Task.FromResult(CliResult.Success(null, null, null))),
                new CliGlobalOptions(false, null, CliGlobalOptions.DefaultTimeout),
                _toServer.Reader.AsStream(),
                _fromServer.Writer.AsStream(),
                new StringWriter(),
                _stop.Token);
            _reader = new StreamReader(_fromServer.Reader.AsStream(), new UTF8Encoding(false));
            _writer = new StreamWriter(_toServer.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true };
        }

        public async Task<JsonElement> RequestAsync(int id, string method, string parameters)
        {
            await _writer.WriteLineAsync($$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{parameters}}}""");
            while (true)
            {
                var line = await _reader.ReadLineAsync(TestContext.Current.CancellationToken) ?? throw new EndOfStreamException("The server closed its output.");
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement.Clone();
                if (message.TryGetProperty("id", out var answered) && answered.GetInt32() == id)
                {
                    return message;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
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
}
