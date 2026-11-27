// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Rendering;

/// <summary>
/// Input and resize handling never block their thread while the renderer's dispatcher is busy. They run on thread pool
/// threads, and the dispatcher runs its own work on the thread pool: a few of them blocked at once left the dispatcher
/// no thread to run on, which froze every app in the process (or deadlocked it with a small thread pool).
/// </summary>
public sealed class BusyDispatcherInputTests
{
    // Only how long a call that does not block takes to return. A call that blocks returns only once the dispatcher is released.
    private static readonly TimeSpan ReturnTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task MouseInput_ReturnsWhileTheDispatcherIsBusy()
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        using var provider = services.BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(provider);
        var mouse = new MouseEventManager(renderer, provider.GetRequiredService<VNodeLayoutAccessor>(), new NoopDispatcher(), new FocusManager());

        Task? handling = null;
        using (BlockDispatcher(renderer))
        {
            ReturnsOnItsOwnThread(() => handling = mouse.HandleAsync(new TerminalMouseEvent(TerminalMouseKind.Move, 1, 1)))
                .ShouldBeTrue("HandleAsync blocked its thread until the dispatcher was free.");
        }

        await handling!.WaitAsync(ReturnTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TerminalResize_ReturnsWhileTheDispatcherIsBusy()
    {
        using var renderer = TestHelpers.CreateTestRenderer();
        var monitor = new TerminalMonitor(80, 24);
        using var context = new ConsoleLiveDisplayContext(new NoopCanvas(), renderer, monitor);

        using (BlockDispatcher(renderer))
        {
            ReturnsOnItsOwnThread(() => monitor.Resize(100, 30))
                .ShouldBeTrue("The resize handler blocked the resizing thread until the dispatcher was free.");
        }
    }

    [Fact]
    public async Task TerminalResizes_WhenIdleWaitsForTheRepaintOfEveryResize()
    {
        using var renderer = TestHelpers.CreateTestRenderer();
        var monitor = new TerminalMonitor(80, 24);
        var canvas = new NoopCanvas();
        using var context = new ConsoleLiveDisplayContext(canvas, renderer, monitor);

        var before = canvas.UpdateCount;
        Task idle;
        using (BlockDispatcher(renderer))
        {
            monitor.Resize(100, 30);
            monitor.Resize(40, 40);
            idle = context.WhenIdleAsync(TestContext.Current.CancellationToken);
            idle.IsCompleted.ShouldBeFalse("WhenIdleAsync returned before the repaints, which wait for the dispatcher.");
        }

        await idle.WaitAsync(ReturnTimeout, TestContext.Current.CancellationToken);
        (canvas.UpdateCount - before).ShouldBe(2, "WhenIdleAsync returned before the repaint of every resize.");
    }

    [Fact]
    public async Task ScrollRepaint_IsAwaitedByWhenIdle()
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        using var provider = services.BuildServiceProvider();
        var registry = new ScrollStateRegistry();
        using var renderer = new ConsoleRenderer(
            provider,
            NullLoggerFactory.Instance,
            provider.GetService<WidgetTranslationContext>(),
            provider.GetService<LayoutEngine>(),
            provider.GetService<VNodeIdAccessor>(),
            provider.GetService<VNodeLayoutAccessor>(),
            scrollStateRegistry: registry);
        var canvas = new NoopCanvas();
        using var context = new ConsoleLiveDisplayContext(canvas, renderer, new TerminalMonitor(80, 24));

        var before = canvas.UpdateCount;
        Task idle;
        using (BlockDispatcher(renderer))
        {
            registry.RequestRepaint();
            idle = context.WhenIdleAsync(TestContext.Current.CancellationToken);
            idle.IsCompleted.ShouldBeFalse("WhenIdleAsync returned before the scroll repaint, which waits for the dispatcher.");
        }

        await idle.WaitAsync(ReturnTimeout, TestContext.Current.CancellationToken);
        (canvas.UpdateCount - before).ShouldBe(1);
    }

    // Runs work that never ends on the dispatcher until disposed. An idle dispatcher runs InvokeAsync work inline on the
    // caller, so the work starts on a thread of its own; a thread pool thread would take one the code under test may need.
    private static IDisposable BlockDispatcher(ConsoleRenderer renderer)
    {
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var thread = new Thread(() => renderer.Dispatcher.InvokeAsync(() =>
        {
            entered.Set();
            release.Wait();
        }))
        { IsBackground = true };
        thread.Start();
        entered.Wait(TestContext.Current.CancellationToken);
        return new Release(release, thread);
    }

    // On a thread of its own, so a call that blocks cannot hang the test: it is released when the dispatcher is.
    private static bool ReturnsOnItsOwnThread(Action call)
    {
        var thread = new Thread(() => call()) { IsBackground = true };
        thread.Start();
        return thread.Join(ReturnTimeout);
    }

    private sealed class Release(ManualResetEventSlim release, Thread thread) : IDisposable
    {
        public void Dispose()
        {
            release.Set();
            thread.Join();
        }
    }

    private sealed class NoopDispatcher : IKeyboardEventDispatcher
    {
        public Task DispatchAsync(ulong handlerId, EventArgs eventArgs, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopCanvas : ConsoleLiveDisplayContext.ILiveDisplayCanvas
    {
        private int _updateCount;

        // Repaints that reached the canvas: a new target, or a refresh when the repaint could not build one.
        public int UpdateCount => Volatile.Read(ref _updateCount);

        public void UpdateTarget(IRenderable? renderable) => Interlocked.Increment(ref _updateCount);

        public void Refresh()
        {
            Interlocked.Increment(ref _updateCount);
            Refreshed?.Invoke();
        }

        public Task WhenIdleAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public event Action? Refreshed;
    }
}
