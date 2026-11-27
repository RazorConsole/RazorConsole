// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// Provides a simplified facade over Spectre.Console live display updates.
/// </summary>
internal sealed class ConsoleLiveDisplayContext : IDisposable, IAsyncDisposable, IObserver<ConsoleRenderer.RenderSnapshot>
{
    private readonly ILiveDisplayCanvas _canvas;
    private readonly Lock _sync = new();
    private readonly VdomDiffService _diffService;
    private readonly ConsoleRenderer _renderer;
    private bool _disposed;
    private int _scrollRepaintPending;
    private readonly Lock _repaintSync = new();
    private Task _repaints = Task.CompletedTask;
    private ConsoleViewResult? _currentView;
    private readonly IDisposable? _snapshotSubscription;
    private readonly AnimationTicker _animationTicker;
    private readonly TerminalMonitor _terminalMonitor;

    internal ConsoleLiveDisplayContext(
        ILiveDisplayCanvas canvas,
        ConsoleRenderer renderer,
        TerminalMonitor terminalMonitor,
        ConsoleViewResult? initialView = null,
        VdomDiffService? diffService = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _animationTicker = new AnimationTicker(_canvas.Refresh);
        _terminalMonitor = terminalMonitor;
        _terminalMonitor.OnResized += HandleTerminalResize;
        _renderer = renderer;
        if (renderer.ScrollRegistry is { } scrollRegistry)
        {
            scrollRegistry.RepaintRequested += HandleScrollRepaintRequested;
        }

        if (initialView is not null)
        {
            _currentView = initialView;
        }

        _snapshotSubscription = renderer.Subscribe(this);
        _diffService = diffService ?? new VdomDiffService();

        if (initialView is not null)
        {
            UpdateAnimations(initialView.AnimatedRenderables);
        }
    }

    /// <summary>
    /// Updates the live display with a new view when the HTML differs from the last rendered HTML.
    /// </summary>
    /// <param name="view">The view to display.</param>
    /// <returns><see langword="true"/> when the view was updated; otherwise <see langword="false"/>.</returns>
    public bool UpdateView(ConsoleViewResult view)
    {
        if (view is null || view.Renderable is null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        lock (_sync)
        {
            var previous = _currentView;
            var previousRoot = previous?.VdomRoot;
            var currentRoot = view.VdomRoot;

            // An unchanged tree keeps the current output; any change repaints the whole frame.
            var updated = previousRoot is null
                || currentRoot is null
                || _diffService.HasChanges(previousRoot, currentRoot);
            if (updated)
            {
                _canvas.UpdateTarget(view.Renderable);
            }

            _currentView = view;
            UpdateAnimations(view.AnimatedRenderables);
            return updated;
        }
    }

    /// <summary>
    /// Replaces the live display target with a custom renderable.
    /// </summary>
    /// <param name="renderable">The renderable to display.</param>
    public void UpdateRenderable(IRenderable? renderable)
    {
        _canvas.UpdateTarget(renderable);
        lock (_sync)
        {
            _currentView = null;
            _animationTicker.Update(null);
        }
    }

    // Scroll state changed outside a Blazor render: relayout and repaint through the same path as a resize.
    // Requests arriving while one is pending are coalesced.
    private void HandleScrollRepaintRequested()
    {
        if (_disposed || Interlocked.Exchange(ref _scrollRepaintPending, 1) == 1)
        {
            return;
        }

        QueueRepaint(() => Volatile.Write(ref _scrollRepaintPending, 0));
    }

    // Raised on the terminal monitor's thread, which does not wait for the repaint, so it never waits for a busy dispatcher.
    private void HandleTerminalResize()
        => QueueRepaint();

    // Repaints run one at a time in request order, each with the snapshot current when it starts, so an earlier repaint
    // never replaces the frame of a later one. WhenIdleAsync waits for the last one queued. Each runs on the thread pool,
    // off the requester's stack, so a request raised during input handling or layout never re-enters the renderer.
    private void QueueRepaint(Action? starting = null)
    {
        lock (_repaintSync)
        {
            var previous = _repaints;
            _repaints = Task.Run(async () =>
            {
                try
                {
                    await previous.ConfigureAwait(false);
                }
                catch
                {
                    // That repaint's own failure was reported to whoever waited for it; this one still runs.
                }

                starting?.Invoke();
                if (!_disposed)
                {
                    await RepaintAsync().ConfigureAwait(false);
                }
            });
        }
    }

    private async Task RepaintAsync()
    {
        try
        {
            var snapshot = await _renderer.RefreshSnapshotAsync().ConfigureAwait(false);
            var view = ConsoleViewResult.FromSnapshot(snapshot);
            lock (_sync)
            {
                _canvas.UpdateTarget(view.Renderable);
                _currentView = view;
                UpdateAnimations(view.AnimatedRenderables);
            }
        }
        catch
        {
            _canvas.Refresh();
        }
    }

    /// <summary>
    /// Returns a task that completes once the frames requested so far, including the repaints queued for terminal
    /// resizes and scroll changes, have been written to the console.
    /// </summary>
    public async Task WhenIdleAsync(CancellationToken cancellationToken = default)
    {
        Task repaints;
        lock (_repaintSync)
        {
            repaints = _repaints;
        }

        await repaints.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _canvas.WhenIdleAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!BeginDispose())
        {
            return;
        }

        if (_canvas is IDisposable disposableCanvas)
        {
            disposableCanvas.Dispose();
        }
    }

    /// <summary>
    /// Stops the sources of new frames, then waits for the canvas to finish the frame it is writing before it
    /// restores the terminal.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!BeginDispose())
        {
            return;
        }

        if (_canvas is IAsyncDisposable asyncDisposableCanvas)
        {
            await asyncDisposableCanvas.DisposeAsync().ConfigureAwait(false);
        }
        else if (_canvas is IDisposable disposableCanvas)
        {
            disposableCanvas.Dispose();
        }
    }

    private bool BeginDispose()
    {
        if (_disposed)
        {
            return false;
        }

        _disposed = true;
        _animationTicker.Dispose();
        _terminalMonitor.OnResized -= HandleTerminalResize;
        if (_renderer.ScrollRegistry is { } scrollRegistry)
        {
            scrollRegistry.RepaintRequested -= HandleScrollRepaintRequested;
        }

        _snapshotSubscription?.Dispose();
        return true;
    }

    internal static ConsoleLiveDisplayContext CreateForTesting(
        ILiveDisplayCanvas canvas,
        ConsoleViewResult? initialView,
        VdomDiffService? diffService)
    {
        var renderer = CreateTestRenderer();
        return new ConsoleLiveDisplayContext(canvas, renderer, new TerminalMonitor(), initialView, diffService: diffService);
    }

    private static ConsoleRenderer CreateTestRenderer()
    {
        var services = new ServiceCollection();
        RazorConsoleServiceCollectionExtensions.AddRazorConsoleServices(services);
        var serviceProvider = services.BuildServiceProvider();
        return new ConsoleRenderer(serviceProvider, NullLoggerFactory.Instance);
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    void IObserver<ConsoleRenderer.RenderSnapshot>.OnNext(ConsoleRenderer.RenderSnapshot value)
    {
        try
        {
            var view = ConsoleViewResult.FromSnapshot(value);
            UpdateView(view);
        }
        catch
        {
            // skip rendering errors from background updates
        }
    }

    private void UpdateAnimations(IReadOnlyCollection<IAnimatedConsoleRenderable> animatedRenderables)
        => _animationTicker.Update(animatedRenderables);

    /// <summary>
    /// Gets the interval the shared animation timer runs at, or <see langword="null"/> when no animation is on screen.
    /// </summary>
    internal TimeSpan? AnimationInterval => _animationTicker.Interval;

    internal interface ILiveDisplayCanvas
    {
        /// <summary>
        /// Requests a frame showing <paramref name="renderable"/>. Must not block or throw for write errors.
        /// </summary>
        void UpdateTarget(IRenderable? renderable);

        /// <summary>
        /// Requests a frame of the current target, best effort. Must not block or throw: a refresh that arrives while
        /// a write is in progress is folded into a write that follows it.
        /// </summary>
        void Refresh();

        /// <summary>
        /// Returns a task that completes once every frame requested before the call has been written, or has failed.
        /// </summary>
        Task WhenIdleAsync(CancellationToken cancellationToken = default);

        event Action? Refreshed;
    }
}
