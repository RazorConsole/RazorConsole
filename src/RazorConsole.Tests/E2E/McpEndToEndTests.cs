// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RazorConsole.Tests.E2E;

/// <summary>
/// The real <c>razorconsole mcp</c> process, spoken to over its standard streams like an agent's client does, driving the
/// PtyApp <c>diag</c> mode through a session host that the MCP process starts itself.
/// </summary>
[Trait("Category", "E2E")]
public sealed class McpEndToEndTests
{
    private const int TestTimeoutMs = 240_000;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeoutMs)]
    public async Task FullLoop_StartScreenTreeClickWaitResourceStop_ThenTheProcessEndsAndItsOutputReachesEof()
    {
        await using var cli = CliE2E.Create();
        await using var mcp = cli.StartMcp();
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(mcp.Input, mcp.Output),
            new McpClientOptions { ClientInfo = new Implementation { Name = "e2e", Version = "1" } },
            cancellationToken: Token);

        client.ServerInfo.ShouldNotBeNull().Name.ShouldBe("razorconsole");
        (await client.ListToolsAsync(cancellationToken: Token)).Select(static tool => tool.Name).ShouldContain("start_session");

        var start = Ok(await Call(client, "start_session", new { dll = CliE2E.PtyAppPath, args = new[] { "diag" } }));
        var session = start.GetProperty("name").GetString()!;
        session.ShouldBe("s1");
        start.GetProperty("kind").GetString().ShouldBe("pty");
        cli.Track(start.GetProperty("pid").GetInt32());
        Ok(await Call(client, "wait_for", new { session, text = "App layer view" }));

        // The terminal and the app layer, as the CLI shows them.
        var screen = Ok(await Call(client, "screenshot", new { session }));
        screen.GetProperty("source").GetString().ShouldBe("pty");
        screen.GetProperty("lines").EnumerateArray().Select(static line => line.GetString()).ShouldContain(line => line!.Contains("App layer view", StringComparison.Ordinal));
        Ok(await Call(client, "screenshot", new { session, format = "json", region = "0,0,40,3" })).GetProperty("cells").GetArrayLength().ShouldBeGreaterThan(0);

        var tree = await Call(client, "get_tree", new { session });
        Ok(tree);
        Text(tree).ShouldContain("\"hook\":\"save\"");

        var found = Ok(await Call(client, "find_elements", new { session, selector = "hook:save" }));
        found.GetProperty("matches").EnumerateArray().ShouldHaveSingleItem().GetProperty("focusKey").GetString().ShouldBe("save");

        // A click on the element is real mouse input through the PTY.
        Ok(await Call(client, "click", new { session, selector = "hook:save" }));
        Ok(await Call(client, "wait_for", new { session, text = "clicked: 1" }));
        Ok(await Call(client, "assert", new { session, text = "clicked: 1" })).GetProperty("passed").GetBoolean().ShouldBeTrue();

        // A condition that does not hold is an answer with the screen, not an error.
        var failed = await Call(client, "assert", new { session, text = "this is not on the screen" });
        failed.IsError.ShouldNotBe(true);
        var failure = Ok(failed);
        failure.GetProperty("passed").GetBoolean().ShouldBeFalse();
        failure.GetProperty("failure").GetProperty("data").GetProperty("screen").GetString()!.ShouldContain("App layer view");

        // A failed operation is an error result with the CLI's own code.
        var missing = await Call(client, "click", new { session, selector = "hook:does-not-exist" });
        missing.IsError.ShouldBe(true);
        Body(missing).GetProperty("error").GetProperty("code").GetString().ShouldBe("element_not_found");
        var timedOut = await Call(client, "wait_for", new { session, text = "never appears", timeout = "300ms" });
        timedOut.IsError.ShouldBe(true);
        var timeout = Body(timedOut).GetProperty("error");
        timeout.GetProperty("code").GetString().ShouldBe("timeout");
        timeout.GetProperty("data").GetProperty("screen").GetString()!.ShouldContain("App layer view");

        // Resources: the same screen, the tree, and the session list.
        var screenResource = await client.ReadResourceAsync($"razorconsole://session/{session}/screen", cancellationToken: Token);
        screenResource.Contents.OfType<TextResourceContents>().Single().Text.ShouldContain("clicked: 1");
        var treeResource = await client.ReadResourceAsync($"razorconsole://session/{session}/tree", cancellationToken: Token);
        treeResource.Contents.OfType<TextResourceContents>().Single().Text.ShouldContain("\"hook\":\"save\"");
        var sessions = await client.ReadResourceAsync("razorconsole://sessions", cancellationToken: Token);
        sessions.Contents.OfType<TextResourceContents>().Single().Text.ShouldContain($"\"name\":\"{session}\"");
        (await client.ListResourcesAsync(cancellationToken: Token)).Select(static resource => resource.Uri).ShouldContain($"razorconsole://session/{session}/screen");

        // The developer's CLI sees the session the agent started: the same host and state directory.
        Ok(await cli.RunAsync("--json", "list")).GetProperty("sessions").EnumerateArray().Select(static item => item.GetProperty("name").GetString()).ShouldContain(session);

        Ok(await Call(client, "stop_session", new { session }));
        Ok(await Call(client, "list_sessions", new { })).GetProperty("sessions").GetArrayLength().ShouldBe(0);

        // The session host was started by this MCP process. It must not hold the MCP process's pipes: when the client
        // closes standard input the process exits, and its standard output reaches EOF while the host still runs.
        Ok(await cli.RunAsync("--json", "host", "status")).GetProperty("running").GetBoolean().ShouldBeTrue();
        (await mcp.CloseAndWaitForExitAsync(TimeSpan.FromSeconds(30))).ShouldBe(0);
        var closed = await client.Completion.WaitAsync(TimeSpan.FromSeconds(30), Token);
        closed.Exception.ShouldBeNull();
        Ok(await cli.RunAsync("--json", "host", "status")).GetProperty("running").GetBoolean().ShouldBeTrue();
        (await mcp.Error).ShouldNotContain("error:", Case.Insensitive);

        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OnlyProtocolReachesStdout_AndAnAppThatCannotStartIsAnErrorResult()
    {
        await using var cli = CliE2E.Create();
        await using var mcp = cli.StartMcp();
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(mcp.Input, mcp.Output),
            cancellationToken: Token);

        // The "crash" mode dies before it draws. Like the CLI, a later command on the session fails with process_exited,
        // whose data has the exit code and the end of the terminal output, where .NET printed the exception.
        var start = Ok(await Call(client, "start_session", new { dll = CliE2E.PtyAppPath, args = new[] { "crash" }, noWait = true }));
        var session = start.GetProperty("name").GetString()!;
        cli.Track(start.GetProperty("pid").GetInt32());
        Ok(await Call(client, "wait_for", new { session, exit = true })).GetProperty("exitCode").GetInt32().ShouldNotBe(0);
        var crashed = await Call(client, "send_keys", new { session, keys = new[] { "Tab" } });

        crashed.IsError.ShouldBe(true);
        var error = Body(crashed).GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe("process_exited");
        error.GetProperty("data").GetProperty("outputTail").GetString()!.ShouldContain("diag crash");

        // A malformed call is an error result, not a dead server.
        var bad = await Call(client, "click", new { session = "s1" });
        bad.IsError.ShouldBe(true);
        Body(bad).GetProperty("error").GetProperty("code").GetString().ShouldBe("usage");

        (await mcp.CloseAndWaitForExitAsync(TimeSpan.FromSeconds(30))).ShouldBe(0);
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    private static async Task<CallToolResult> Call(McpClient client, string tool, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var values = document.RootElement.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value.Clone());
        return await client.CallToolAsync(new CallToolRequestParams { Name = tool, Arguments = values }, Token);
    }

    private static string Text(CallToolResult result) => result.Content.OfType<TextContentBlock>().Single().Text;

    private static JsonElement Body(CallToolResult result)
    {
        using var document = JsonDocument.Parse(Text(result));
        return document.RootElement.Clone();
    }

    private static JsonElement Ok(CallToolResult result)
    {
        result.IsError.ShouldNotBe(true, Text(result));
        var body = Body(result);
        body.GetProperty("ok").GetBoolean().ShouldBeTrue(Text(result));
        return body;
    }

    private static JsonElement Ok(CliRun run)
    {
        run.ExitCode.ShouldBe(0, run.Describe());
        return run.Json;
    }
}
