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
internal sealed class ConsoleLiveDisplayContext : IDisposable, IObserver<ConsoleRenderer.RenderSnapshot>
{
    private readonly ILiveDisplayCanvas _canvas;
    private readonly Lock _sync = new();
    private readonly VdomDiffService _diffService;
    private readonly ConsoleRenderer _renderer;
    private bool _disposed;
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

    private void HandleTerminalResize()
    {
        try
        {
            var snapshot = _renderer.RefreshSnapshot();
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _animationTicker.Dispose();
        _terminalMonitor.OnResized -= HandleTerminalResize;
        _snapshotSubscription?.Dispose();
        if (_canvas is IDisposable disposableCanvas)
        {
            disposableCanvas.Dispose();
        }

        _disposed = true;
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
        void UpdateTarget(IRenderable? renderable);

        /// <summary>
        /// Writes the current target again, best effort. Must not block or throw: a refresh that arrives while
        /// a write is in progress is folded into a write that follows it.
        /// </summary>
        void Refresh();

        event Action? Refreshed;
    }

    private sealed class LiveDisplayCanvasAdapter : ILiveDisplayCanvas
    {
        private readonly Spectre.Console.LiveDisplayContext _context;

        public LiveDisplayCanvasAdapter(Spectre.Console.LiveDisplayContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public void UpdateTarget(IRenderable? renderable)
            => _context.UpdateTarget(renderable);

        public void Refresh()
        {
            _context.Refresh();
            Refreshed?.Invoke();
        }

        public event Action? Refreshed;
    }
}
