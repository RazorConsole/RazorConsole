// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;

namespace RazorConsole.Tests.Diagnostics;

public sealed class DiagnosticsInputQueueTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Enqueue_RefusesMoreThanMaxPendingUntilOneIsDispatched()
    {
        var router = CreateRouter();
        var queue = new DiagnosticsInputQueue(router);
        var waiting = new List<DiagnosticsInputItem>();
        for (var i = 0; i < DiagnosticsInputQueue.MaxPending; i++)
        {
            waiting.Add(queue.Enqueue("x", null));
        }

        Should.Throw<DiagnosticsProtocolException>(() => queue.Enqueue("x", null)).Code.ShouldBe(DiagnosticsErrorCodes.QueueOverflow);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var loop = router.RunAsync(input: null, stop.Token);
        await Task.WhenAll(waiting.Select(item => item.Completion)).WaitAsync(Token);

        await queue.Enqueue("x", null).Completion.WaitAsync(Token);
        await stop.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task AStoppedRouter_FailsWaitingInputAndLaterInputAsUnavailable()
    {
        var router = CreateRouter();
        var queue = new DiagnosticsInputQueue(router);
        var waiting = queue.Enqueue("x", null);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await router.RunAsync(input: null, stop.Token);

        foreach (var item in new[] { waiting, queue.Enqueue("y", null) })
        {
            var failure = await Should.ThrowAsync<DiagnosticsProtocolException>(() => item.Completion);
            failure.Code.ShouldBe(DiagnosticsErrorCodes.InputUnavailable);
            failure.ToError().Data.ShouldNotBeNull().Deserialize(DiagnosticsJsonContext.Default.DiagnosticsUnavailableData)!
                .Reason.ShouldBe(DiagnosticsUnavailableReasons.InputStopped);
        }
    }

    [Fact]
    public async Task TryCancel_WithdrawsOnlyInputTheRouterHasNotStarted()
    {
        var router = CreateRouter();
        var queue = new DiagnosticsInputQueue(router);
        var withdrawn = queue.Enqueue("a", null);
        var dispatched = queue.Enqueue("b", null);

        withdrawn.TryCancel().ShouldBeTrue();
        await Should.ThrowAsync<TaskCanceledException>(() => withdrawn.Completion);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var loop = router.RunAsync(input: null, stop.Token);
        await dispatched.Completion.WaitAsync(Token);
        dispatched.TryCancel().ShouldBeFalse();

        await stop.CancelAsync();
        await loop;
    }

    // Nothing is focused, so keys complete at once without dispatching anything.
    private static InputRouter CreateRouter()
        => new(new KeyboardEventManager(new FocusManager(), Substitute.For<IKeyboardEventDispatcher>()));
}
