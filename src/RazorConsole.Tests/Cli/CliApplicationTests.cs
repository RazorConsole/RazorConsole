// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using RazorConsole.Cli;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// One in-process run: what reaches stdout and stderr, the JSON envelope, and the exit code for every error code.
/// </summary>
public sealed class CliApplicationTests
{
    [Fact]
    public async Task Success_WritesText_OrMergesDataIntoTheJsonEnvelope()
    {
        var operations = new FakeOperations(_ => CliResult.Success("s1", Json("""{"sent":3,"ok":false,"session":"other"}"""), "sent 3 keys"));

        var human = await RunAsync(operations, "keys", "a", "b", "c");
        human.ExitCode.ShouldBe(CliExitCodes.Success);
        human.Output.ShouldBe("sent 3 keys" + Environment.NewLine);
        human.Error.ShouldBeEmpty();

        var json = await RunAsync(operations, "keys", "a", "--json");
        json.ExitCode.ShouldBe(CliExitCodes.Success);
        json.Error.ShouldBeEmpty();
        json.Output.ShouldBe("""{"ok":true,"session":"s1","sent":3}""" + Environment.NewLine);
    }

    [Fact]
    public async Task NonObjectData_IsWrittenAsResult()
    {
        var operations = new FakeOperations(_ => CliResult.Success(null, Json("[1,2]"), null));

        (await RunAsync(operations, "stats", "--json")).Output.ShouldBe("""{"ok":true,"result":[1,2]}""" + Environment.NewLine);
        (await RunAsync(operations, "stats")).Output.ShouldBeEmpty();
    }

    [Fact]
    public async Task Failure_GoesToStderr_OrIsOneJsonLineOnStdout()
    {
        var operations = new FakeOperations(_ => CliResult.Failure(
            DiagnosticsErrorCodes.ElementNotFound, "No element matches 'x'.", Json("""{"selector":"x"}"""), "s1"));

        var human = await RunAsync(operations, "inspect", "x");
        human.ExitCode.ShouldBe(CliExitCodes.NotFound);
        human.Output.ShouldBeEmpty();
        human.Error.ShouldBe("razorconsole: No element matches 'x'. [element_not_found]" + Environment.NewLine);

        var json = await RunAsync(operations, "inspect", "x", "--json");
        json.ExitCode.ShouldBe(CliExitCodes.NotFound);
        json.Error.ShouldBeEmpty();

        // The default encoder keeps the line ASCII, so it survives any console code page; ' becomes '.
        json.Output.ShouldBe(
            """{"ok":false,"session":"s1","error":{"code":"element_not_found","message":"No element matches \u0027x\u0027.","data":{"selector":"x"}}}"""
            + Environment.NewLine);
    }

    [Fact]
    public async Task Failure_WithText_PrintsTheTextOnStdout_BeforeTheErrorOnStderr()
    {
        var data = Json("""{"checks":[{"name":"os.pty","status":"fail","detail":"no"}]}""");
        var operations = new FakeOperations(_ => new CliResult(
            new DiagnosticsError(DiagnosticsErrorCodes.ChecksFailed, "1 of 1 checks failed: os.pty", null), null, data, "fail  os.pty  no"));

        var human = await RunAsync(operations, "doctor");
        human.ExitCode.ShouldBe(CliExitCodes.AssertionFailed);
        human.Output.ShouldBe("fail  os.pty  no" + Environment.NewLine);
        human.Error.ShouldBe("razorconsole: 1 of 1 checks failed: os.pty [checks_failed]" + Environment.NewLine);

        var json = await RunAsync(operations, "doctor", "--json");
        json.ExitCode.ShouldBe(CliExitCodes.AssertionFailed);
        json.Error.ShouldBeEmpty();
        using var document = JsonDocument.Parse(json.Output);
        document.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();

        // The checks are where they are on success; the error sits next to them and carries no copy.
        document.RootElement.GetProperty("checks")[0].GetProperty("name").GetString().ShouldBe("os.pty");
        var failure = document.RootElement.GetProperty("error");
        failure.GetProperty("code").GetString().ShouldBe("checks_failed");
        failure.TryGetProperty("data", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Success_WithChecks_HasThemAtTheTopLevel_AsAFailureDoes()
    {
        var data = Json("""{"checks":[{"name":"os.pty","status":"pass","detail":"ok"}]}""");
        var result = await RunAsync(new FakeOperations(_ => CliResult.Success(null, data, "pass  os.pty  ok")), "doctor", "--json");

        result.ExitCode.ShouldBe(CliExitCodes.Success);
        using var document = JsonDocument.Parse(result.Output);
        document.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
        document.RootElement.GetProperty("checks")[0].GetProperty("status").GetString().ShouldBe("pass");
        document.RootElement.TryGetProperty("error", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task UsageErrors_ExitWith2_ListEveryError_AndNeverRunTheOperation()
    {
        var operations = new FakeOperations(_ => throw new InvalidOperationException("must not run"));

        var json = await RunAsync(operations, "--json", "click", "1", "2", "--text", "OK");
        json.ExitCode.ShouldBe(CliExitCodes.Usage);
        using var document = JsonDocument.Parse(json.Output);
        document.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe(DiagnosticsErrorCodes.Usage);
        document.RootElement.GetProperty("error").GetProperty("data").GetProperty("errors").GetArrayLength().ShouldBe(1);

        var human = await RunAsync(operations, "frames", "--last", "0", "--format", "x");
        human.ExitCode.ShouldBe(CliExitCodes.Usage);
        human.Error.ShouldStartWith("razorconsole: ");
        human.Error.ShouldContain("[usage]");

        operations.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheParsedRequestAndOptionsReachTheOperations()
    {
        var operations = new FakeOperations(_ => CliResult.Success(null, null, null));

        (await RunAsync(operations, "-s", "s2", "--timeout", "3s", "type", "hi")).ExitCode.ShouldBe(CliExitCodes.Success);

        operations.Requests.ShouldHaveSingleItem().ShouldBe((new TypeRequest("hi"), new CliGlobalOptions(false, "s2", TimeSpan.FromSeconds(3))));
    }

    [Fact]
    public async Task HelpAndVersion_WriteToTheGivenOutput_AndSkipTheOperations()
    {
        var operations = new FakeOperations(_ => throw new InvalidOperationException("must not run"));

        var help = await RunAsync(operations, "--help");
        help.ExitCode.ShouldBe(0);
        help.Output.ShouldContain("razorconsole");
        help.Output.ShouldContain("inspect-at");
        help.Output.ShouldNotContain("__host");

        var verbHelp = await RunAsync(operations, "click", "--help");
        verbHelp.ExitCode.ShouldBe(0);
        verbHelp.Output.ShouldContain("--element");

        var version = await RunAsync(operations, "--version");
        version.ExitCode.ShouldBe(0);
        version.Output.Trim().ShouldNotBeEmpty();

        operations.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Exceptions_AndCancellation_BecomeInternalErrors()
    {
        var failing = await RunAsync(new FakeOperations(_ => throw new IOException("pipe broke")), "list", "--json");
        failing.ExitCode.ShouldBe(CliExitCodes.Internal);
        failing.Output.ShouldContain("\"code\":\"internal\"");
        failing.Output.ShouldContain("pipe broke");

        using var cancellation = new CancellationTokenSource();
        var cancelled = await RunAsync(
            new FakeOperations(_ =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
                return CliResult.Success(null, null, null);
            }),
            cancellation.Token,
            "list");
        cancelled.ExitCode.ShouldBe(CliExitCodes.Cancelled);
        cancelled.ExitCode.ShouldBe(130);
        cancelled.Error.ShouldContain("The command was cancelled. [cancelled]");
    }

    [Theory]
    [InlineData("assertion_failed", 1)]
    [InlineData("checks_failed", 1)]
    [InlineData("usage", 2)]
    [InlineData("invalid_params", 2)]
    [InlineData("session_not_found", 3)]
    [InlineData("element_not_found", 3)]
    [InlineData("ambiguous_selector", 3)]
    [InlineData("element_not_visible", 3)]
    [InlineData("app_layer_unavailable", 4)]
    [InlineData("input_unavailable", 4)]
    [InlineData("screen_unavailable", 4)]
    [InlineData("timeout", 5)]
    [InlineData("process_exited", 6)]
    [InlineData("pty_unsupported", 7)]
    [InlineData("host_unavailable", 8)]
    [InlineData("host_version_mismatch", 8)]
    [InlineData("internal", 10)]
    [InlineData("cancelled", 130)]
    [InlineData("method_not_found", 10)]
    [InlineData("protocol_mismatch", 10)]
    [InlineData("too_many_connections", 10)]
    [InlineData("something_new", 10)]
    public async Task EveryErrorCode_MapsToItsExitCode(string code, int exitCode)
    {
        CliExitCodes.For(code).ShouldBe(exitCode);
        (await RunAsync(new FakeOperations(_ => CliResult.Failure(code, "x")), "list")).ExitCode.ShouldBe(exitCode);
    }

    [Fact]
    public async Task TheDefaultOperations_ReportNotImplemented()
    {
        var result = await RunAsync(new NotImplementedOperations(), "list");

        result.ExitCode.ShouldBe(CliExitCodes.Internal);
        result.Error.ShouldContain("not implemented");
    }

    [Fact]
    public async Task Follow_WritesEachItemAsOneLine_ThenTheFinalResult()
    {
        var json = await RunAsync(new StreamingOperations(), "logs", "--follow", "--json");
        json.ExitCode.ShouldBe(CliExitCodes.Success);
        var lines = json.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Length.ShouldBe(3);
        lines[0].ShouldBe("{\"seq\":1,\"message\":\"first\"}");
        lines[1].ShouldBe("{\"seq\":2,\"message\":\"second\"}");
        lines[2].ShouldBe("{\"ok\":true,\"session\":\"s1\"}");

        var text = await RunAsync(new StreamingOperations(), "logs", "--follow");
        text.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ShouldBe(["first", "second"]);
    }

    [Theory]
    [InlineData("element_not_focusable", CliExitCodes.NotFound)]
    [InlineData("terminal_layer_unavailable", CliExitCodes.AppLayerUnavailable)]
    public void NewErrorCodes_MapToTheirExitCodes(string code, int exitCode)
        => CliExitCodes.For(code).ShouldBe(exitCode);

    [Fact]
    public async Task FailureData_WithAScreen_IsPrintedForPeople()
    {
        var data = Json("""{"screen":"line one\nline two"}""");
        var operations = new FakeOperations(_ => CliResult.Failure("assertion_failed", "Expected x.", data));

        var result = await RunAsync(operations, "assert", "--text", "x");

        result.ExitCode.ShouldBe(CliExitCodes.AssertionFailed);
        result.Error.ShouldContain("Expected x. [assertion_failed]");
        result.Error.ShouldContain("--- screen ---");
        result.Error.ShouldContain("line two");
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Task<RunResult> RunAsync(ICliOperations operations, params string[] args)
        => RunAsync(operations, CancellationToken.None, args);

    private static async Task<RunResult> RunAsync(ICliOperations operations, CancellationToken cancellationToken, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CliApplication.RunAsync(args, operations, output, error, static _ => null, Path.GetTempPath(), cancellationToken);
        return new RunResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record RunResult(int ExitCode, string Output, string Error);

    private sealed class FakeOperations(Func<CliRequest, CliResult> execute) : ICliOperations
    {
        public List<(CliRequest Request, CliGlobalOptions Options)> Requests { get; } = [];

        public Task<CliResult> ExecuteAsync(CliRequest request, CliGlobalOptions options, CancellationToken cancellationToken, ICliStream? stream = null)
        {
            Requests.Add((request, options));
            return Task.FromResult(execute(request));
        }
    }

    // Writes two items to the stream, then ends the way a follow does on Ctrl+C: with no data of its own.
    private sealed class StreamingOperations : ICliOperations
    {
        public async Task<CliResult> ExecuteAsync(CliRequest request, CliGlobalOptions options, CancellationToken cancellationToken, ICliStream? stream = null)
        {
            await stream!.WriteAsync(Json("{\"seq\":1,\"message\":\"first\"}"), "first");
            await stream.WriteAsync(Json("{\"seq\":2,\"message\":\"second\"}"), "second");
            return CliResult.Success("s1", null, null);
        }
    }
}
