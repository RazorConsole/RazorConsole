// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;

namespace RazorConsole.Core.Layout;

/// <summary>
/// A snapshot of a scroll container's position and size.
/// </summary>
/// <param name="Top">The first visible row of the content.</param>
/// <param name="ContentHeight">The height of all the content, in rows.</param>
/// <param name="ViewportHeight">The number of rows that are visible at once.</param>
public readonly record struct ScrollViewMetrics(int Top, int ContentHeight, int ViewportHeight)
{
    /// <summary>Gets the largest value <see cref="Top"/> can have.</summary>
    public int MaxTop => Math.Max(0, ContentHeight - ViewportHeight);

    /// <summary>Gets a value indicating whether there is content to scroll to.</summary>
    public bool CanScroll => MaxTop > 0;

    /// <summary>Gets a value indicating whether the first row is visible.</summary>
    public bool IsAtStart => Top <= 0;

    /// <summary>Gets a value indicating whether the last row is visible.</summary>
    public bool IsAtEnd => Top >= MaxTop;
}

/// <summary>
/// Scrolls a container from code. Pass an instance to the <c>Controller</c> parameter of a scroll container
/// (<c>ScrollView</c>, or a <c>Box</c>, <c>Panel</c>, <c>Padder</c> or <c>Border</c> with <c>Overflow="OverflowMode.Scroll"</c>).
/// </summary>
/// <remarks>
/// A controller drives one container at a time and does nothing until that container has rendered.
/// Scrolling happens in the layout engine and repaints without a Blazor render.
/// <see cref="MetricsChanged"/> can be raised on any thread.
/// </remarks>
public sealed class ScrollController
{
    private readonly object _gate = new();
    private ScrollStateRegistry? _registry;
    private ScrollInputNavigator? _navigator;
    private string? _scrollId;

    /// <summary>
    /// Raised when the position or the size of the content or the viewport changes.
    /// </summary>
    public event Action<ScrollViewMetrics>? MetricsChanged;

    /// <summary>
    /// Gets a value indicating whether a container is currently using this controller.
    /// </summary>
    public bool IsAttached
    {
        get
        {
            lock (_gate)
            {
                return _registry is not null;
            }
        }
    }

    /// <summary>
    /// Gets the current position and size; all zeros until the container has been laid out.
    /// </summary>
    public ScrollViewMetrics Metrics
    {
        get
        {
            var (registry, id, _) = Snapshot();
            return registry is null || id is null ? default : ToMetrics(registry.Get(id));
        }
    }

    /// <summary>
    /// Scrolls to a row of the content; the value is limited to the scrollable range.
    /// </summary>
    /// <param name="top">The first row to show.</param>
    /// <returns>A task that is already complete; scrolling does not wait for the next frame.</returns>
    public Task ScrollToAsync(int top)
    {
        var (registry, id, _) = Snapshot();
        if (registry is not null && id is not null)
        {
            registry.SetTop(id, top, ScrollChangeSource.Engine);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Scrolls by a number of rows; negative values scroll up.
    /// </summary>
    /// <param name="delta">The rows to move by.</param>
    /// <returns>A task that is already complete.</returns>
    public Task ScrollByAsync(int delta)
    {
        var (registry, id, _) = Snapshot();
        if (registry is not null && id is not null)
        {
            registry.SetTop(id, registry.Get(id).Top + delta, ScrollChangeSource.Engine);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Scrolls to the first row.
    /// </summary>
    /// <returns>A task that is already complete.</returns>
    public Task ScrollToStartAsync() => ScrollToAsync(0);

    /// <summary>
    /// Scrolls to the last row.
    /// </summary>
    /// <returns>A task that is already complete.</returns>
    public Task ScrollToEndAsync() => ScrollToAsync(int.MaxValue);

    /// <summary>
    /// Scrolls the container so that an element inside it is visible.
    /// </summary>
    /// <param name="target">The element: its <c>data-vnode-hook</c>, its <c>data-focus-key</c> or its <c>@key</c>.</param>
    /// <returns><see langword="true"/> when the element was found; it is then visible, or as visible as the container allows.</returns>
    public Task<bool> ScrollIntoViewAsync(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var (_, _, navigator) = Snapshot();
        return Task.FromResult(navigator?.ScrollIntoView(target) == true);
    }

    internal void Attach(ScrollStateRegistry registry, string scrollId, ScrollInputNavigator? navigator)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_registry, registry) && _scrollId == scrollId)
            {
                _navigator = navigator;
                return;
            }
        }

        Detach();
        lock (_gate)
        {
            _registry = registry;
            _scrollId = scrollId;
            _navigator = navigator;
        }

        registry.TopChanged += OnTopChanged;
        registry.MetricsChanged += OnMetricsChanged;
    }

    internal void Detach()
    {
        ScrollStateRegistry? registry;
        lock (_gate)
        {
            registry = _registry;
            _registry = null;
            _scrollId = null;
            _navigator = null;
        }

        if (registry is not null)
        {
            registry.TopChanged -= OnTopChanged;
            registry.MetricsChanged -= OnMetricsChanged;
        }
    }

    private static ScrollViewMetrics ToMetrics(ScrollState state)
        => new(state.Top, state.Metrics.ContentHeight, state.Metrics.ViewportHeight);

    private (ScrollStateRegistry? Registry, string? Id, ScrollInputNavigator? Navigator) Snapshot()
    {
        lock (_gate)
        {
            return (_registry, _scrollId, _navigator);
        }
    }

    private void OnTopChanged(string scrollId, int top) => Raise(scrollId);

    private void OnMetricsChanged(string scrollId, ScrollMetrics metrics) => Raise(scrollId);

    private void Raise(string scrollId)
    {
        var (registry, id, _) = Snapshot();
        if (registry is null || id != scrollId)
        {
            return;
        }

        MetricsChanged?.Invoke(ToMetrics(registry.Get(id)));
    }
}
