// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Host;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// Input verbs that name an element wait for it within the timeout instead of failing on the first frame that lacks it.
/// </summary>
public sealed class HostElementWaitTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(DiagnosticsErrorCodes.ElementNotFound)]
    [InlineData(DiagnosticsErrorCodes.ElementNotFocusable)]
    [InlineData(DiagnosticsErrorCodes.ElementNotVisible)]
    public async Task AnElementThatIsNotReadyYet_IsRetriedUntilItIs(string code)
    {
        var attempts = 0;

        var result = await HostSession.WaitForElementAsync(
            _ => ++attempts < 4 ? throw new DiagnosticsProtocolException(code, "not yet") : Task.FromResult("ready"),
            TimeSpan.FromSeconds(30),
            Token);

        result.ShouldBe("ready");
        attempts.ShouldBe(4);
    }

    [Fact]
    public async Task AnElementThatNeverComes_FailsWithTheLastErrorAfterTheTimeout()
    {
        var attempts = 0;

        var error = await Should.ThrowAsync<DiagnosticsProtocolException>(() => HostSession.WaitForElementAsync<string>(
            _ => throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.ElementNotFocusable, $"attempt {++attempts}"),
            TimeSpan.FromMilliseconds(300),
            Token));

        error.Code.ShouldBe(DiagnosticsErrorCodes.ElementNotFocusable);
        attempts.ShouldBeGreaterThan(1);
        error.Message.ShouldBe($"attempt {attempts}");
    }

    [Theory]
    [InlineData(DiagnosticsErrorCodes.AmbiguousSelector)]
    [InlineData(DiagnosticsErrorCodes.InvalidParams)]
    [InlineData(DiagnosticsErrorCodes.ProcessExited)]
    public async Task OtherErrors_AreNotRetried(string code)
    {
        var attempts = 0;

        var error = await Should.ThrowAsync<DiagnosticsProtocolException>(() => HostSession.WaitForElementAsync<string>(
            _ => throw new DiagnosticsProtocolException(code, $"attempt {++attempts}"),
            TimeSpan.FromSeconds(30),
            Token));

        error.Code.ShouldBe(code);
        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task ACancelledRequest_StopsTheWait()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(200);

        await Should.ThrowAsync<OperationCanceledException>(() => HostSession.WaitForElementAsync<string>(
            _ => throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.ElementNotFound, "not yet"),
            TimeSpan.FromSeconds(30),
            cancellation.Token));
    }
}
