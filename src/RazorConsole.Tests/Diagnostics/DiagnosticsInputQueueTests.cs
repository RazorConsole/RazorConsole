// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Diagnostics;

public sealed class DiagnosticsInputQueueTests
{
    [Fact]
    public void Enqueue_RefusesMoreThanMaxPendingUntilOneIsTaken()
    {
        var queue = new DiagnosticsInputQueue();
        for (var i = 0; i < DiagnosticsInputQueue.MaxPending; i++)
        {
            queue.Enqueue("x", null);
        }

        Should.Throw<DiagnosticsProtocolException>(() => queue.Enqueue("x", null)).Code.ShouldBe(DiagnosticsErrorCodes.QueueOverflow);

        queue.TryDequeue(out _).ShouldBeTrue();
        queue.Enqueue("x", null).ShouldNotBeNull();
    }

    [Fact]
    public async Task Close_FailsWaitingItemsAndRefusesNewOnes()
    {
        var queue = new DiagnosticsInputQueue();
        var waiting = queue.Enqueue("x", null);

        queue.Close();

        var failure = await Should.ThrowAsync<DiagnosticsProtocolException>(() => waiting.Completion);
        failure.Code.ShouldBe(DiagnosticsErrorCodes.InputUnavailable);
        failure.ToError().Data.ShouldNotBeNull().Deserialize(DiagnosticsJsonContext.Default.DiagnosticsUnavailableData)!
            .Reason.ShouldBe(DiagnosticsUnavailableReasons.InputStopped);
        Should.Throw<DiagnosticsProtocolException>(() => queue.Enqueue("x", null)).Code.ShouldBe(DiagnosticsErrorCodes.InputUnavailable);
        queue.TryDequeue(out _).ShouldBeFalse();
    }

    [Fact]
    public void TryCancel_WithdrawsOnlyItemsTheLoopHasNotStarted()
    {
        var queue = new DiagnosticsInputQueue();
        var withdrawn = queue.Enqueue("a", null);
        var started = queue.Enqueue("b", null);

        withdrawn.TryCancel().ShouldBeTrue();
        withdrawn.Completion.IsCanceled.ShouldBeTrue();
        withdrawn.TryStart().ShouldBeFalse();

        started.TryStart().ShouldBeTrue();
        started.TryCancel().ShouldBeFalse();
        started.Completion.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task WaitAsync_WakesWhenAnItemIsQueued()
    {
        var queue = new DiagnosticsInputQueue();
        var wait = queue.WaitAsync(Timeout.Infinite, TestContext.Current.CancellationToken);
        wait.IsCompleted.ShouldBeFalse();

        queue.Enqueue("x", null);

        await wait.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
