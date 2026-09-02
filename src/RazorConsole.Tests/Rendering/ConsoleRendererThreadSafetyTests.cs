// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using RazorConsole.Core.Rendering;

namespace RazorConsole.Tests.Rendering;

public sealed class ConsoleRendererThreadSafetyTests
{
    [Fact]
    public async Task Dispatcher_RunsWorkItemsOneAtATime()
    {
        // Blazor's Renderer is not thread-safe and relies on its dispatcher to admit one
        // caller at a time. A console app has at least two threads that want in — the key
        // reading loop dispatching events, and background work calling
        // InvokeAsync(StateHasChanged) — so a dispatcher that runs work inline on the
        // calling thread lets them collide, and the render tree corrupts: mismatched pool
        // returns, expired ParameterViews, then NullReferenceException in the diff builder,
        // after which the renderer produces no further frames.
        using var renderer = TestHelpers.CreateTestRenderer();
        var dispatcher = renderer.Dispatcher;

        var inFlight = 0;
        var overlapped = false;

        var callers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (Interlocked.Increment(ref inFlight) > 1)
                    {
                        Volatile.Write(ref overlapped, true);
                    }

                    Thread.Sleep(1);
                    Interlocked.Decrement(ref inFlight);
                });
            }
        })).ToArray();

        await Task.WhenAll(callers);

        Volatile.Read(ref overlapped).ShouldBeFalse();
    }

    [Fact]
    public async Task Dispatcher_CheckAccess_IsTrueInsideWorkItemAndFalseOutside()
    {
        using var renderer = TestHelpers.CreateTestRenderer();
        var dispatcher = renderer.Dispatcher;

        dispatcher.CheckAccess().ShouldBeFalse();

        await dispatcher.InvokeAsync(() =>
        {
            dispatcher.CheckAccess().ShouldBeTrue();

            // Nested calls must run inline rather than deadlock waiting on the queue.
            dispatcher.InvokeAsync(() => dispatcher.CheckAccess().ShouldBeTrue()).IsCompleted.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Dispatcher_ContinuationsAfterAwait_StayOnTheDispatcher()
    {
        // Pages start background loads from an event handler and let them run detached.
        // Their continuations have to come back to the dispatcher, or they render from a
        // pool thread while the next keystroke is being dispatched.
        using var renderer = TestHelpers.CreateTestRenderer();
        var dispatcher = renderer.Dispatcher;

        var onDispatcherAfterAwait = false;

        await dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(1);
            onDispatcherAfterAwait = dispatcher.CheckAccess();
        });

        onDispatcherAfterAwait.ShouldBeTrue();
    }

    [Fact]
    public async Task Subscribe_FromMultipleThreads_IsThreadSafe()
    {
        using var renderer = TestHelpers.CreateTestRenderer();
        var observers = new List<TestObserver>();
        var exceptions = new List<Exception>();

        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            try
            {
                var observer = new TestObserver();
                lock (observers)
                {
                    observers.Add(observer);
                }
                renderer.Subscribe(observer);
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        exceptions.ShouldBeEmpty();
        observers.Count.ShouldBe(10);
    }

    [Fact]
    public async Task Unsubscribe_FromMultipleThreads_IsThreadSafe()
    {
        using var renderer = TestHelpers.CreateTestRenderer();
        var subscriptions = new List<IDisposable>();
        var exceptions = new List<Exception>();

        // Subscribe first
        for (int i = 0; i < 10; i++)
        {
            var observer = new TestObserver();
            subscriptions.Add(renderer.Subscribe(observer));
        }

        // Unsubscribe from multiple threads
        var tasks = subscriptions.Select(sub => Task.Run(() =>
        {
            try
            {
                sub.Dispose();
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        exceptions.ShouldBeEmpty();
    }

    [Fact]
    public async Task MountComponentAsync_SequentialCalls_WorkCorrectly()
    {
        using var renderer = TestHelpers.CreateTestRenderer();

        // MountComponentAsync is not designed for concurrent calls
        // Test sequential calls instead to verify it works correctly
        var snapshot1 = await renderer.MountComponentAsync<SimpleTestComponent>(
            ParameterView.Empty, CancellationToken.None);
        snapshot1.Root.ShouldNotBeNull();

        var snapshot2 = await renderer.MountComponentAsync<SimpleTestComponent>(
            ParameterView.Empty, CancellationToken.None);
        snapshot2.Root.ShouldNotBeNull();
    }

    [Fact]
    public async Task Subscribe_AndUnsubscribe_Concurrently_IsThreadSafe()
    {
        using var renderer = TestHelpers.CreateTestRenderer();
        var subscriptions = new List<IDisposable>();
        var exceptions = new List<Exception>();
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            Xunit.TestContext.Current?.CancellationToken ?? CancellationToken.None);
        var cancellationToken = cancellationTokenSource.Token;

        // Start subscribe tasks
        var subscribeTasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            try
            {
                var observer = new TestObserver();
                var sub = renderer.Subscribe(observer);
                lock (subscriptions)
                {
                    subscriptions.Add(sub);
                }
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        }, cancellationToken)).ToArray();

        // Mount a component to trigger render notifications
        // This will cause the observer list to be read (via NotifyObservers) while subscribe/unsubscribe modify it
        var mountTask = Task.Run(async () =>
        {
            try
            {
                await renderer.MountComponentAsync<SimpleTestComponent>(
                    ParameterView.Empty, cancellationToken);
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        }, cancellationToken);

        // Start unsubscribe task concurrently (it will unsubscribe as subscriptions become available)
        // This tests the race condition between subscribe (write) and unsubscribe (write)
        // The mount operation above will trigger notifications that read the observer list concurrently
        var unsubscribeTask = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    IDisposable? sub = null;
                    lock (subscriptions)
                    {
                        if (subscriptions.Count > 0)
                        {
                            sub = subscriptions[0];
                            subscriptions.RemoveAt(0);
                        }
                    }

                    if (sub != null)
                    {
                        try
                        {
                            sub.Dispose();
                        }
                        catch (Exception ex)
                        {
                            lock (exceptions)
                            {
                                exceptions.Add(ex);
                            }
                        }
                    }
                    else
                    {
                        // Small delay to avoid busy-waiting
                        await Task.Delay(1, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }, cancellationToken);

        // Wait for all subscribe tasks to complete
        await Task.WhenAll(subscribeTasks);

        // Wait for mount to complete (this triggers notifications that read the observer list)
        await mountTask;

        // Give unsubscribe task time to exercise the race condition with any pending notifications
        await Task.Delay(50, cancellationToken);

        // Cancel ongoing tasks
        cancellationTokenSource.Cancel();

        // Wait for unsubscribe task to complete
        await unsubscribeTask;

        // Clean up any remaining subscriptions
        lock (subscriptions)
        {
            foreach (var sub in subscriptions)
            {
                try
                {
                    sub.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }

        exceptions.ShouldBeEmpty();
    }

    private sealed class SimpleTestComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddContent(1, "Test");
            builder.CloseElement();
        }
    }

    private sealed class TestObserver : IObserver<ConsoleRenderer.RenderSnapshot>
    {
        public List<ConsoleRenderer.RenderSnapshot> Snapshots { get; } = new();
        public List<Exception> Errors { get; } = new();
        public bool IsCompleted { get; private set; }

        public void OnNext(ConsoleRenderer.RenderSnapshot value)
        {
            lock (Snapshots)
            {
                Snapshots.Add(value);
            }
        }

        public void OnError(Exception error)
        {
            lock (Errors)
            {
                Errors.Add(error);
            }
        }

        public void OnCompleted()
        {
            IsCompleted = true;
        }
    }
}

