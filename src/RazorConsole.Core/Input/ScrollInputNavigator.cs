// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Input;

/// <summary>
/// Keyboard scrolling for scroll containers: keys that nobody handled scroll the nearest container, and moving focus
/// scrolls the focused element into view.
/// </summary>
internal sealed class ScrollInputNavigator
{
    private readonly ConsoleRenderer _renderer;
    private readonly IVNodeLayoutAccessor _layouts;
    private readonly FocusManager _focus;
    private readonly ILogger<ScrollInputNavigator> _logger;

    public ScrollInputNavigator(
        ConsoleRenderer renderer,
        IVNodeLayoutAccessor layouts,
        FocusManager focus,
        ILogger<ScrollInputNavigator>? logger = null)
    {
        _renderer = renderer;
        _layouts = layouts;
        _focus = focus;
        _logger = logger ?? NullLogger<ScrollInputNavigator>.Instance;
        _focus.FocusChanged += OnFocusChanged;
    }

    /// <summary>
    /// Scrolls the nearest scroll container of the focused node for <paramref name="key"/>.
    /// The arrow keys only apply to a focused container itself, because inside it they belong to the focused control.
    /// </summary>
    /// <returns><see langword="true"/> when something scrolled.</returns>
    public async Task<bool> TryScrollAsync(string focusedNodeId, ConsoleKey key)
    {
        if (_renderer.ScrollRegistry is not { } registry
            || (await _renderer.RefreshSnapshotAsync().ConfigureAwait(false)).Root is not { } root)
        {
            return false;
        }

        var isArrow = key is ConsoleKey.UpArrow or ConsoleKey.DownArrow;
        if (!isArrow && key is not (ConsoleKey.PageUp or ConsoleKey.PageDown or ConsoleKey.Home or ConsoleKey.End))
        {
            return false;
        }

        foreach (var node in MouseEventManager.Path(root, focusedNodeId).AsEnumerable().Reverse())
        {
            // The scroll id comes from the element itself, so keys keep working while layout is a frame behind a render.
            if (MouseEventManager.ResolveScrollId(node, _layouts) is not { } scrollId || (isArrow && node.ID != focusedNodeId))
            {
                continue;
            }

            var state = registry.Get(scrollId);
            var max = state.Metrics.MaxTop;
            var page = Math.Max(1, state.Metrics.ViewportHeight);
            var target = key switch
            {
                ConsoleKey.UpArrow => state.Top - 1,
                ConsoleKey.DownArrow => state.Top + 1,
                ConsoleKey.PageUp => state.Top - page,
                ConsoleKey.PageDown => state.Top + page,
                ConsoleKey.Home => 0,
                _ => max,
            };
            target = Math.Clamp(target, 0, max);
            if (target != state.Top)
            {
                registry.SetTop(scrollId, target, ScrollChangeSource.Engine);
                return true;
            }

            if (MouseEventManager.IsContained(node))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Scrolls the containers around an element so that it is visible.
    /// </summary>
    /// <param name="target">The element's <c>data-vnode-hook</c>, <c>data-focus-key</c> or key.</param>
    /// <returns><see langword="true"/> when the element exists.</returns>
    public async Task<bool> ScrollIntoViewAsync(string target)
    {
        if ((await _renderer.RefreshSnapshotAsync().ConfigureAwait(false)).Root is not { } root || Find(root, target) is not { } node)
        {
            return false;
        }

        ScrollNodeIntoView(root, node.ID);
        return true;
    }

    private static VNode? Find(VNode node, string target)
    {
        if (node.Kind == VNodeKind.Element
            && (node.Key == target
                || (node.Attributes.TryGetValue("data-vnode-hook", out var hook) && hook == target)
                || (node.Attributes.TryGetValue("data-focus-key", out var focusKey) && focusKey == target)))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (Find(child, target) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        // The element was only rendered again: the focus did not move, so the user's scroll position stands.
        if (e.Rendered)
        {
            return;
        }

        // The element this change focused: by the time the scroll runs the focus may have moved on, and each element
        // it passed still scrolls into view in turn.
        if (!_focus.TryGetFocusedTarget(out var target) || target is null)
        {
            return;
        }

        // The focus moves on the input loop, a thread pool thread: waiting there for a busy dispatcher can starve the
        // pool that the dispatcher itself runs on. Queue the scroll on the dispatcher instead. A render or snapshot
        // requested after this focus change is queued behind it, so it already shows the scrolled position.
        _ = ScrollIntoViewOnDispatcherAsync(target.VNodeId);
    }

    private async Task ScrollIntoViewOnDispatcherAsync(string nodeId)
    {
        try
        {
            await _renderer.Dispatcher.InvokeAsync(() =>
            {
                if (_renderer.RefreshSnapshot().Root is { } root)
                {
                    ScrollNodeIntoView(root, nodeId);
                }
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The app shut down after the focus moved.
        }
        catch (Exception ex)
        {
            // Nobody awaits this scroll, so report its failure here.
            _logger.LogErrorScrollingFocusIntoView(ex, nodeId);
        }
    }

    private void ScrollNodeIntoView(VNode root, string nodeId)
    {
        if (_renderer.ScrollRegistry is not { } registry
            || _layouts.GetLayoutByVNodeIdOrDefault(nodeId) is not { Top: { } top, Height: { } height })
        {
            return;
        }

        var bottom = top + height;
        foreach (var node in MouseEventManager.Path(root, nodeId).AsEnumerable().Reverse())
        {
            if (node.ID == nodeId
                || _layouts.GetLayoutByVNodeIdOrDefault(node.ID)?.Scroll is not { ScrollId: { } scrollId, Viewport: { IsEmpty: false } viewport })
            {
                continue;
            }

            // Move the container by the least amount that brings the element into view; the next container out sees it where it ends up.
            var delta = 0;
            if (top < viewport.Y)
            {
                delta = top - viewport.Y;
            }
            else if (bottom > viewport.Bottom)
            {
                delta = Math.Min(bottom - viewport.Bottom, top - viewport.Y);
            }

            if (delta == 0)
            {
                continue;
            }

            var state = registry.Get(scrollId);
            var next = Math.Clamp(state.Top + delta, 0, state.Metrics.MaxTop);
            if (next != state.Top)
            {
                registry.SetTop(scrollId, next, ScrollChangeSource.Engine);
                top -= next - state.Top;
                bottom -= next - state.Top;
            }
        }
    }
}
