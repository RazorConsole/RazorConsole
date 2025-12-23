// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// Provides a simplified facade over Spectre.Console live display updates.
/// </summary>
public sealed class ConsoleLiveDisplayContext : IDisposable, IObserver<ConsoleRenderer.RenderSnapshot>
{
    private readonly ILiveDisplayCanvas _canvas;
#if NET9_0_OR_GREATER
    private readonly Lock _sync = new();
#else
    private readonly object _sync = new();
#endif
    private readonly VdomDiffService _diffService;
    private readonly Translation.Contexts.TranslationContext _translationContext;
    private bool _disposed;
    private ConsoleViewResult? _currentView;
    private readonly IDisposable? _snapshotSubscription;
    private List<AnimationSubscription>? _animationSubscriptions;

    internal ConsoleLiveDisplayContext(
        ILiveDisplayCanvas canvas,
        ConsoleRenderer renderer,
        ConsoleViewResult? initialView = null,
        VdomDiffService? diffService = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _translationContext = renderer.GetTranslationContext();
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

#if NET9_0_OR_GREATER
        using (_sync.EnterScope())
#else
        lock (_sync)
#endif
        {
            var previous = _currentView;
            var previousRoot = previous?.VdomRoot;
            var currentRoot = view.VdomRoot;

            bool updated;

            if (previousRoot is not null && currentRoot is not null && _diffService is not null)
            {
                var diff = _diffService.Diff(previousRoot, currentRoot);
                if (!diff.HasChanges)
                {
                    updated = false;
                }
                else
                {
                    var applied = TryApplyMutations(diff);
                    if (!applied)
                    {
                        _canvas.UpdateTarget(view.Renderable);
                    }

                    updated = true;
                }
            }
            else
            {
                _canvas.UpdateTarget(view.Renderable);
                updated = true;
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
#if NET9_0_OR_GREATER
        using (_sync.EnterScope())
#else
        lock (_sync)
#endif
        {
            _currentView = null;
            DisposeAnimations();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        DisposeAnimations();
        _snapshotSubscription?.Dispose();
        _disposed = true;
    }

    internal static ConsoleLiveDisplayContext CreateForTesting(
        ILiveDisplayCanvas canvas,
        ConsoleViewResult? initialView,
        VdomDiffService? diffService)
    {
        var renderer = CreateTestRenderer();
        return new ConsoleLiveDisplayContext(canvas, renderer, initialView, diffService: diffService);
    }

    private static ConsoleRenderer CreateTestRenderer()
    {
        var services = new ServiceCollection();
        RazorConsoleServiceCollectionExtensions.AddRazorConsoleServices(services);
        var serviceProvider = services.BuildServiceProvider();
        var translationContext = serviceProvider.GetRequiredService<TranslationContext>();
        return new ConsoleRenderer(serviceProvider, NullLoggerFactory.Instance, translationContext);
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

    private bool TryApplyMutations(VdomDiffResult diff)
    {
        if (!diff.HasChanges)
        {
            return false;
        }

        foreach (var mutation in diff.Mutations)
        {
            var applied = mutation.Kind switch
            {
                VdomMutationKind.UpdateText => _canvas.TryUpdateText(mutation.Path, mutation.Text),
                VdomMutationKind.UpdateAttributes => _canvas.TryUpdateAttributes(mutation.Path, mutation.Attributes ?? EmptyAttributes),
                VdomMutationKind.ReplaceNode => TryReplaceNode(mutation),
                _ => false,
            };

            if (!applied)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryReplaceNode(VdomMutation mutation)
    {
        if (mutation.Node is null)
        {
            return false;
        }

        if (!SpectreRenderableFactory.TryCreateRenderable(mutation.Node, _translationContext, out var renderable, out _) || renderable is null)
        {
            return false;
        }

        return _canvas.TryReplaceNode(mutation.Path, renderable);
    }

    private void UpdateAnimations(IReadOnlyCollection<IAnimatedConsoleRenderable> animatedRenderables)
    {
        DisposeAnimations();

        if (animatedRenderables is null || animatedRenderables.Count == 0)
        {
            return;
        }

        var subscriptions = new List<AnimationSubscription>(animatedRenderables.Count);
        foreach (var animated in animatedRenderables)
        {
            subscriptions.Add(new AnimationSubscription(animated.RefreshInterval, _canvas));
        }

        _animationSubscriptions = subscriptions;
    }

    private void DisposeAnimations()
    {
        if (_animationSubscriptions is null)
        {
            return;
        }

        foreach (var subscription in _animationSubscriptions)
        {
            subscription.Dispose();
        }

        _animationSubscriptions = null;
    }

    //private static ConsoleRenderer CreateRenderer()
    //{
    //    var services = new ServiceCollection();
    //    services.AddDefaultVdomTranslators();
    //    services.AddSingleton<Rendering.Vdom.VdomSpectreTranslator>(sp =>
    //    {
    //        var translators = sp.GetServices<Rendering.Vdom.IVdomElementTranslator>()
    //            .OrderBy(t => t.Priority)
    //            .ToList();
    //        return new Rendering.Vdom.VdomSpectreTranslator(translators);
    //    });
    //    var serviceProvider = services.BuildServiceProvider();
    //    var translator = serviceProvider.GetRequiredService<Rendering.Vdom.VdomSpectreTranslator>();
    //    return new ConsoleRenderer(serviceProvider, NullLoggerFactory.Instance, translator);
    //}

    private static readonly IReadOnlyDictionary<string, object?> EmptyAttributes =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(0, EqualityComparer<object?>.Default));

    internal interface ILiveDisplayCanvas
    {
        void UpdateTarget(IRenderable? renderable);

        bool TryReplaceNode(IReadOnlyList<int> path, IRenderable renderable);

        bool TryUpdateText(IReadOnlyList<int> path, string? text);

        bool TryUpdateAttributes(IReadOnlyList<int> path, IReadOnlyDictionary<string, object?> attributes);

        void Refresh();

        event Action? Refreshed;
    }

    private sealed class AnimationSubscription : IDisposable
    {
        private readonly Timer _timer;

        public AnimationSubscription(TimeSpan interval, ILiveDisplayCanvas canvas)
        {
            _timer = new Timer(_ => SafeRefresh(canvas), null, interval, interval);
        }

        private static void SafeRefresh(ILiveDisplayCanvas canvas)
        {
            try
            {
                canvas.Refresh();
            }
            catch
            {
                // Ignore rendering exceptions from background updates.
            }
        }

        public void Dispose()
            => _timer.Dispose();
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

        public bool TryReplaceNode(IReadOnlyList<int> path, IRenderable renderable)
            => false;

        public bool TryUpdateText(IReadOnlyList<int> path, string? text)
            => false;

        public bool TryUpdateAttributes(IReadOnlyList<int> path, IReadOnlyDictionary<string, object?> attributes)
            => false;

        public void Refresh()
        {
            _context.Refresh();
            Refreshed?.Invoke();
        }

        public event Action? Refreshed;
    }
}
