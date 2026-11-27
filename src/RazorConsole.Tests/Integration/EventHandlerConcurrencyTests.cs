// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

/// <summary>
/// Input does not wait for an event handler's <see cref="Task"/> before it handles the next key or click (issue #495).
/// </summary>
public sealed class EventHandlerConcurrencyTests
{
    private const int StartRow = 0;
    private const int CancelRow = 1;
    private const int CountRow = 2;

    [Fact]
    public async Task Cancel_DuringAwaitedHandler_CancelsItQuickly_AndOtherClicksRunMeanwhile()
    {
        var ct = TestContext.Current.CancellationToken;
        var events = new List<string>();
        await using var terminal = await StartLongRunningHostAsync(events, TimeSpan.FromSeconds(5), ct);

        // The click returns once the handler has started, not after its 5 s await.
        var stopwatch = Stopwatch.StartNew();
        await terminal.ClickAsync(1, StartRow, ct);
        await terminal.WaitForTextAsync("status=working", cancellationToken: ct);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));

        // A second click is handled while the first handler is still awaiting.
        await terminal.ClickAsync(1, CountRow, ct);
        await terminal.WaitForTextAsync("status=working count=1", cancellationToken: ct);

        stopwatch.Restart();
        await terminal.ClickAsync(1, CancelRow, ct);
        await terminal.WaitForTextAsync("status=cancelled", cancellationToken: ct);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));

        await terminal.WaitUntilAsync(_ => Snapshot(events).Length == 4, cancellationToken: ct);
        Snapshot(events).ShouldBe(["start", "count", "cancel", "end:cancelled"]);
    }

    [Fact]
    public async Task InputCall_ReturnsOnceTheHandlerHasStarted()
    {
        var ct = TestContext.Current.CancellationToken;
        var events = new List<string>();
        await using var terminal = await StartLongRunningHostAsync(events, TimeSpan.FromMilliseconds(300), ct);

        await terminal.ClickAsync(1, StartRow, ct);

        // The synchronous part ran and rendered; the rest of the handler is still awaiting.
        terminal.CurrentFrame.ScreenText.ShouldContain("status=working");
        Snapshot(events).ShouldBe(["start"]);
        await terminal.WaitForTextAsync("status=done", cancellationToken: ct);
    }

    [Fact]
    public async Task Handled_SetBeforeTheFirstAwait_SuppressesTabTraversal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartHandledKeyHostAsync(awaitBeforeHandled: false, ct);

        await terminal.SendKeyAsync(ConsoleKey.Tab, '\t', cancellationToken: ct);

        terminal.FocusKey.ShouldBe("first");
    }

    [Fact]
    public async Task Handled_SetAfterAnAwait_IsTooLate_AndTabMovesFocus()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartHandledKeyHostAsync(awaitBeforeHandled: true, ct);

        await terminal.SendKeyAsync(ConsoleKey.Tab, '\t', cancellationToken: ct);

        terminal.FocusKey.ShouldBe("second");
    }

    // The stale-id retry in FocusManager.DispatchToLatestRenderAsync recognizes a stale id by exactly this fault.
    [Fact]
    public async Task UnknownHandlerId_FaultsTheDispatch_WithEventHandlerIdParam()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await HeadlessTerminal.StartAsync<LongRunningHandlerHost>(
            new HeadlessTerminalOptions { Width = 30, Height = 5 },
            ct);
        await terminal.WaitForTextAsync("status=idle", cancellationToken: ct);
        var dispatcher = terminal.Services.GetRequiredService<RendererKeyboardEventDispatcher>();

        var ex = await Should.ThrowAsync<ArgumentException>(
            () => dispatcher.DispatchAsync(ulong.MaxValue - 1, new MouseEventArgs(), ct));

        ex.ParamName.ShouldBe("eventHandlerId");
    }

    [Fact]
    public async Task StaleHandlerId_RetriesOnceAfterTheNextSnapshot_WithTheLatestRendersId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartLongRunningHostAsync([], TimeSpan.FromSeconds(5), ct);
        var focus = terminal.Services.GetRequiredService<FocusManager>();
        var dispatcher = terminal.Services.GetRequiredService<RendererKeyboardEventDispatcher>();
        focus.TryGetFocusedTarget(out var target).ShouldBeTrue();
        var snapshotAtAttempt = new List<long>();
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The retry timeout is infinite, so the retry can only proceed once a new snapshot arrives.
        var dispatch = focus.DispatchToLatestRenderAsync(
            target!,
            "onclick",
            handlerId =>
            {
                snapshotAtAttempt.Add(focus.SnapshotSequence);
                if (snapshotAtAttempt.Count == 1)
                {
                    firstAttempt.SetResult();
                    throw new ArgumentException("Stale handler id.", "eventHandlerId");
                }

                return dispatcher.DispatchAsync(handlerId, new MouseEventArgs(), ct);
            },
            Timeout.InfiniteTimeSpan,
            ct);

        // The app is idle, so nothing renders until this click on Count, after the first attempt failed.
        await firstAttempt.Task.WaitAsync(ct);
        await terminal.ClickAsync(1, CountRow, ct);
        var dispatched = await dispatch.WaitAsync(ct);

        dispatched.ShouldBeTrue();
        snapshotAtAttempt.Count.ShouldBe(2);
        snapshotAtAttempt[1].ShouldBeGreaterThan(snapshotAtAttempt[0]);

        // The retried id belongs to the latest render, so the real handler runs.
        await terminal.WaitForTextAsync("status=working count=1", cancellationToken: ct);
    }

    [Fact]
    public async Task StaleHandlerId_StillStaleAfterTheRetry_Faults()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartLongRunningHostAsync([], TimeSpan.FromSeconds(5), ct);
        var focus = terminal.Services.GetRequiredService<FocusManager>();
        focus.TryGetFocusedTarget(out var target).ShouldBeTrue();
        var attempts = 0;

        // A zero retry timeout: no new snapshot is needed for the retry to happen.
        var ex = await Should.ThrowAsync<ArgumentException>(() => focus.DispatchToLatestRenderAsync(
            target!,
            "onclick",
            _ =>
            {
                attempts++;
                throw new ArgumentException("Stale handler id.", "eventHandlerId");
            },
            TimeSpan.Zero,
            ct));

        ex.ParamName.ShouldBe("eventHandlerId");
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task OtherArgumentException_IsNotRetried()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartLongRunningHostAsync([], TimeSpan.FromSeconds(5), ct);
        var focus = terminal.Services.GetRequiredService<FocusManager>();
        focus.TryGetFocusedTarget(out var target).ShouldBeTrue();
        var attempts = 0;

        var ex = await Should.ThrowAsync<ArgumentException>(() => focus.DispatchToLatestRenderAsync(
            target!,
            "onclick",
            _ =>
            {
                attempts++;
                throw new ArgumentException("Something else.", "eventArgs");
            },
            TimeSpan.Zero,
            ct));

        ex.ParamName.ShouldBe("eventArgs");
        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task AsyncHandlerException_IsReportedOnce_AndNotLeftUnobserved()
    {
        var ct = TestContext.Current.CancellationToken;
        var message = $"handler failure {Guid.NewGuid():N}";
        var unobserved = 0;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.Flatten().InnerExceptions.Any(inner => inner.Message == message))
            {
                Interlocked.Increment(ref unobserved);
            }
        }

        var errors = new ErrorCounter(message);
        IDisposable? subscription = null;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            await using (var terminal = await HeadlessTerminal.StartAsync<AsyncThrowingHandlerHost>(
                new HeadlessTerminalOptions
                {
                    Width = 20,
                    Height = 3,
                    Parameters = new Dictionary<string, object?> { [nameof(AsyncThrowingHandlerHost.Message)] = message },
                },
                ct))
            {
                await terminal.WaitForTextAsync("Fail", cancellationToken: ct);
                subscription = terminal.Renderer.Subscribe(errors);

                await terminal.ClickAsync(1, 0, ct);

                // The click returns before the handler fails, so wait for the renderer to report the failure.
                await errors.FirstError.WaitAsync(ct);
            }

            // Disposing the terminal disposes the renderer, which completes and removes its observers.
            // No report can reach the counter after that, so this count is final and needs no settling delay.
            errors.Count.ShouldBe(1);

            // An unobserved task exception is only raised when the faulted task is finalized. Collecting and
            // running finalizers here makes that happen now instead of at some later, unpredictable GC.
            // Without these calls the check below passes even when the dispatch leaves the fault unobserved.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Volatile.Read(ref unobserved).ShouldBe(0);
        }
        finally
        {
            subscription?.Dispose();
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [Fact]
    public async Task TextInput_WithAsyncValueChanged_KeepsTypedTextInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var values = new List<string>();
        await using var terminal = await HeadlessTerminal.StartAsync<AsyncBoundTextInputHost>(
            new HeadlessTerminalOptions
            {
                Width = 30,
                Height = 5,
                Parameters = new Dictionary<string, object?> { [nameof(AsyncBoundTextInputHost.Values)] = values },
            },
            ct);
        await terminal.WaitForTextAsync("bound=<>", cancellationToken: ct);

        await terminal.TypeAsync("hello", ct);

        await terminal.WaitForTextAsync("bound=<hello>", cancellationToken: ct);
        terminal.CurrentFrame.ScreenText.ShouldContain("│hello│");
        Snapshot(values).ShouldBe(["h", "he", "hel", "hell", "hello"]);

        await terminal.TypeAsync("!", ct);
        await terminal.WaitForTextAsync("bound=<hello!>", cancellationToken: ct);
    }

    [Fact]
    public async Task TextInput_ParentNormalizingAsyncValueChanged_ShowsTheParentsValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await HeadlessTerminal.StartAsync<AsyncBoundTextInputHost>(
            new HeadlessTerminalOptions
            {
                Width = 30,
                Height = 5,
                Parameters = new Dictionary<string, object?> { [nameof(AsyncBoundTextInputHost.Normalize)] = true },
            },
            ct);
        await terminal.WaitForTextAsync("bound=<>", cancellationToken: ct);

        await terminal.TypeAsync("abc", ct);

        await terminal.WaitForTextAsync("bound=<ABC>", cancellationToken: ct);
        terminal.CurrentFrame.ScreenText.ShouldContain("│ABC│");
    }

    [Fact]
    public async Task TextArea_KeysTypedDuringAsyncSubmit_StayInTheComposer()
    {
        var ct = TestContext.Current.CancellationToken;
        var submitted = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var terminal = await HeadlessTerminal.StartAsync<AsyncSubmitTextAreaHost>(
            new HeadlessTerminalOptions
            {
                Width = 40,
                Height = 4,
                Parameters = new Dictionary<string, object?>
                {
                    [nameof(AsyncSubmitTextAreaHost.Submitted)] = submitted,
                    [nameof(AsyncSubmitTextAreaHost.SubmitGate)] = gate,
                },
            },
            ct);
        await terminal.WaitForTextAsync("buffer=<>", cancellationToken: ct);
        await terminal.TypeAsync("hi", ct);
        await terminal.WaitForTextAsync("buffer=<hi>", cancellationToken: ct);

        await terminal.SendKeyAsync(ConsoleKey.Enter, '\r', cancellationToken: ct);
        await terminal.WaitForTextAsync("buffer=<>", cancellationToken: ct);
        await terminal.TypeAsync("x", ct);
        await terminal.WaitForTextAsync("buffer=<x>", cancellationToken: ct);

        gate.SetResult();
        await terminal.WaitForTextAsync("buffer=<x> submitted=hi", cancellationToken: ct);
    }

    private static async Task<HeadlessTerminal> StartLongRunningHostAsync(List<string> events, TimeSpan duration, CancellationToken ct)
    {
        var terminal = await HeadlessTerminal.StartAsync<LongRunningHandlerHost>(
            new HeadlessTerminalOptions
            {
                Width = 30,
                Height = 5,
                Parameters = new Dictionary<string, object?>
                {
                    [nameof(LongRunningHandlerHost.Events)] = events,
                    [nameof(LongRunningHandlerHost.WorkDuration)] = duration,
                },
            },
            ct);
        await terminal.WaitForTextAsync("status=idle", cancellationToken: ct);
        return terminal;
    }

    private static async Task<HeadlessTerminal> StartHandledKeyHostAsync(bool awaitBeforeHandled, CancellationToken ct)
    {
        var terminal = await HeadlessTerminal.StartAsync<HandledKeyHost>(
            new HeadlessTerminalOptions
            {
                Width = 20,
                Height = 3,
                Parameters = new Dictionary<string, object?> { [nameof(HandledKeyHost.AwaitBeforeHandled)] = awaitBeforeHandled },
            },
            ct);
        await terminal.WaitUntilAsync(frame => frame.FocusKey == "first", cancellationToken: ct);
        return terminal;
    }

    private static string[] Snapshot(List<string> list)
    {
        lock (list)
        {
            return [.. list];
        }
    }

    private sealed class ErrorCounter(string message) : IObserver<ConsoleRenderer.RenderSnapshot>
    {
        private readonly TaskCompletionSource _firstError = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task FirstError => _firstError.Task;

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            if (error.Message == message)
            {
                Interlocked.Increment(ref _count);
                _firstError.TrySetResult();
            }
        }

        public void OnNext(ConsoleRenderer.RenderSnapshot value)
        {
        }
    }
}
