// Copyright (c) RazorConsole. All rights reserved.

using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using RazorConsole.Cli.Mcp;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Mcp;

/// <summary>
/// A failed operation is a tool result with <c>isError</c> and the CLI's own JSON; only protocol problems are JSON-RPC
/// errors; a cancelled call cancels the operation.
/// </summary>
public sealed class McpErrorMappingTests
{
    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task Success_IsTheSameJsonAsTheCliPrintsWithJson()
    {
        var expected = CliResult.Success("s1", Json("""{"sent":3}"""), "sent 3 keys");
        await using var server = await McpTestServer.StartAsync(_ => expected);

        var result = await server.CallAsync("send_keys", """{"session":"s1","keys":["a","b","c"]}""");

        result.IsError.ShouldNotBe(true);
        result.Content.OfType<TextContentBlock>().Single().Text.ShouldBe(CliOutput.ToJson(expected));
        server.Operations.Requests.Single().Options.Session.ShouldBe("s1");
    }

    [Theory]
    [InlineData(DiagnosticsErrorCodes.ElementNotFound, """{"selector":"hook:x"}""")]
    [InlineData(DiagnosticsErrorCodes.AmbiguousSelector, """{"candidates":[{"path":"/0"},{"path":"/1"}]}""")]
    [InlineData(DiagnosticsErrorCodes.Timeout, """{"screen":"Loading..."}""")]
    [InlineData(DiagnosticsErrorCodes.ProcessExited, """{"exitCode":3,"outputTail":"Unhandled exception. boom","lastErrors":[]}""")]
    [InlineData(DiagnosticsErrorCodes.HostUnavailable, null)]
    [InlineData(DiagnosticsErrorCodes.SessionNotFound, null)]
    [InlineData(DiagnosticsErrorCodes.TerminalLayerUnavailable, """{"reason":"attached"}""")]
    public async Task OperationFailure_IsAnErrorResult_WithTheCliCodeMessageAndData(string code, string? data)
    {
        var failure = CliResult.Failure(code, "It failed.", data is null ? null : Json(data), "s1");
        await using var server = await McpTestServer.StartAsync(_ => failure);

        var result = await server.CallAsync("get_tree", """{"session":"s1"}""");

        result.IsError.ShouldBe(true);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.ShouldBe(CliOutput.ToJson(failure));
        var body = McpTestServer.Body(result);
        body.GetProperty("ok").GetBoolean().ShouldBeFalse();
        body.GetProperty("error").GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("error").GetProperty("message").GetString().ShouldBe("It failed.");
        if (data is not null)
        {
            body.GetProperty("error").GetProperty("data").GetRawText().ShouldBe(Json(data).GetRawText());
        }
    }

    [Fact]
    public async Task FailedAssert_IsAnAnswerNotAnError_WithTheScreenAndTheReason()
    {
        var data = Json("""{"condition":"text \"Done\"","not":false,"screen":"Working..."}""");
        await using var server = await McpTestServer.StartAsync(_ => CliResult.Failure(DiagnosticsErrorCodes.AssertionFailed, "Assertion failed.", data, "s1"));

        var result = await server.CallAsync("assert", """{"session":"s1","text":"Done"}""");

        result.IsError.ShouldNotBe(true);
        var body = McpTestServer.Body(result);
        body.GetProperty("ok").GetBoolean().ShouldBeTrue();
        body.GetProperty("passed").GetBoolean().ShouldBeFalse();
        var failure = body.GetProperty("failure");
        failure.GetProperty("code").GetString().ShouldBe("assertion_failed");
        failure.GetProperty("data").GetProperty("screen").GetString().ShouldBe("Working...");
    }

    [Fact]
    public async Task PassingAssert_ReportsPassedTrue_AndOtherAssertErrorsStayErrors()
    {
        await using var passing = await McpTestServer.StartAsync(_ => CliResult.Success("s1", Json("""{"passed":true}"""), "ok"));
        McpTestServer.Body(await passing.CallAsync("assert", """{"session":"s1","text":"Done"}""")).GetProperty("passed").GetBoolean().ShouldBeTrue();

        await using var missing = await McpTestServer.StartAsync(_ => CliResult.Failure(DiagnosticsErrorCodes.SessionNotFound, "No such session.", session: "s1"));
        var result = await missing.CallAsync("assert", """{"session":"s1","text":"Done"}""");
        result.IsError.ShouldBe(true);
        McpTestServer.Body(result).GetProperty("error").GetProperty("code").GetString().ShouldBe("session_not_found");
    }

    [Fact]
    public async Task BadArguments_AreAnErrorResultWithUsageAndTheProblems_AndNothingRuns()
    {
        await using var server = await McpTestServer.StartAsync(_ => CliResult.Success(null, null, null));

        var result = await server.CallAsync("click", """{"session":"s1","bogus":1}""");

        result.IsError.ShouldBe(true);
        var error = McpTestServer.Body(result).GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe("usage");
        error.GetProperty("data").GetProperty("errors").EnumerateArray().Select(static item => item.GetString()).ShouldContain("bogus: this tool has no such argument.");
        server.Operations.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnOperationThatThrows_BecomesAnInternalErrorResult_NotAStackTrace()
    {
        var operations = new FakeOperations((_, _, _) => throw new InvalidOperationException("kaput"));
        await using var server = await McpTestServer.StartAsync(operations);

        var result = await server.CallAsync("get_stats", """{"session":"s1"}""");

        result.IsError.ShouldBe(true);
        var error = McpTestServer.Body(result).GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe("internal");
        error.GetProperty("message").GetString().ShouldBe("kaput");
    }

    [Fact]
    public async Task UnknownTool_IsAJsonRpcInvalidParamsError()
    {
        await using var server = await McpTestServer.StartAsync(_ => CliResult.Success(null, null, null));

        var exception = await Should.ThrowAsync<McpProtocolException>(() => server.CallAsync("rm_rf"));

        exception.ErrorCode.ShouldBe(McpErrorCode.InvalidParams);
    }

    [Fact]
    public async Task SessionInfo_FindsOneSession_OrSaysItIsMissing()
    {
        var list = Json("""{"sessions":[{"name":"s1","kind":"pty","pid":42,"exited":false},{"name":"s2","kind":"pty","pid":43,"exited":true,"exitCode":3}]}""");
        await using var server = await McpTestServer.StartAsync(_ => CliResult.Success(null, list, null));

        var found = McpTestServer.Body(await server.CallAsync("session_info", """{"session":"s2"}"""));
        found.GetProperty("pid").GetInt32().ShouldBe(43);
        found.GetProperty("exitCode").GetInt32().ShouldBe(3);

        var missing = await server.CallAsync("session_info", """{"session":"nope"}""");
        missing.IsError.ShouldBe(true);
        McpTestServer.Body(missing).GetProperty("error").GetProperty("code").GetString().ShouldBe("session_not_found");

        var unnamed = await server.CallAsync("session_info");
        unnamed.IsError.ShouldBe(true);
        McpTestServer.Body(unnamed).GetProperty("error").GetProperty("code").GetString().ShouldBe("usage");
    }

    [Fact]
    public async Task NotificationsCancelled_CancelsTheRunningOperation_AndTheServerKeepsServing()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new FakeOperations(async (request, _, cancellationToken) =>
        {
            if (request is not WaitRequest)
            {
                return CliResult.Success("s1", Json("""{"path":"/","relative":""}"""), null);
            }

            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }

            return CliResult.Success("s1", null, null);
        });

        // Speak the protocol by hand: the message is notifications/cancelled, as a client sends it.
        var toServer = new Pipe();
        var fromServer = new Pipe();
        using var stop = new CancellationTokenSource();
        var server = McpServerHost.RunAsync(
            operations,
            new CliGlobalOptions(false, null, CliGlobalOptions.DefaultTimeout),
            toServer.Reader.AsStream(),
            fromServer.Writer.AsStream(),
            TextWriter.Null,
            stop.Token);
        var input = new StreamWriter(toServer.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        using var output = new StreamReader(fromServer.Reader.AsStream(), Encoding.UTF8);
        var limit = TimeSpan.FromSeconds(10);

        async Task<JsonElement> ReadAsync()
        {
            var line = await output.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(limit, TestContext.Current.CancellationToken);
            return Json(line!);
        }

        await input.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""");
        (await ReadAsync()).GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString().ShouldBe("razorconsole");
        await input.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        await input.WriteLineAsync("""{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"wait_for","arguments":{"session":"s1","text":"never"}}}""");
        await started.Task.WaitAsync(limit, TestContext.Current.CancellationToken);
        await input.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":11,"reason":"the user gave up"}}""");

        // The operation's token fires, like Ctrl+C in the CLI (the host client forwards it to the host).
        await cancelled.Task.WaitAsync(limit, TestContext.Current.CancellationToken);

        // The cancelled request gets no answer; the next request is answered normally.
        await input.WriteLineAsync("""{"jsonrpc":"2.0","id":12,"method":"tools/call","params":{"name":"get_route","arguments":{"session":"s1"}}}""");
        var reply = await ReadAsync();
        reply.GetProperty("id").GetInt32().ShouldBe(12);
        reply.GetProperty("result").GetProperty("isError").ValueKind.ShouldNotBe(JsonValueKind.True);

        await stop.CancelAsync();
        try
        {
            await server.WaitAsync(limit, TestContext.Current.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
