// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task StaleHandlerId_FaultsTheDispatch_SoFocusedInputCanRetry()
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
                var errors = new ErrorCounter(message);
                using var subscription = terminal.Renderer.Subscribe(errors);

                await terminal.ClickAsync(1, 0, ct);

                await errors.FirstError.WaitAsync(TimeSpan.FromSeconds(2), ct);
                await Task.Delay(100, ct);
                errors.Count.ShouldBe(1);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Volatile.Read(ref unobserved).ShouldBe(0);
        }
        finally
        {
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
        await terminal.WaitUntilAsync(_ => terminal.FocusKey == "first", cancellationToken: ct);
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
