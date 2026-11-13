// Copyright (c) RazorConsole. All rights reserved.

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

    public ScrollInputNavigator(ConsoleRenderer renderer, IVNodeLayoutAccessor layouts, FocusManager focus)
    {
        _renderer = renderer;
        _layouts = layouts;
        _focus = focus;
        _focus.FocusChanged += OnFocusChanged;
    }

    /// <summary>
    /// Scrolls the nearest scroll container of the focused node for <paramref name="key"/>.
    /// The arrow keys only apply to a focused container itself, because inside it they belong to the focused control.
    /// </summary>
    /// <returns><see langword="true"/> when something scrolled.</returns>
    public bool TryScroll(string focusedNodeId, ConsoleKey key)
    {
        if (_renderer.ScrollRegistry is not { } registry || _renderer.RefreshSnapshot().Root is not { } root)
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
            if (_layouts.GetLayoutByVNodeIdOrDefault(node.ID)?.Scroll is not { ScrollId: { } scrollId } scroll
                || (isArrow && node.ID != focusedNodeId))
            {
                continue;
            }

            var state = registry.Get(scrollId);
            var max = state.Metrics.MaxTop;
            var page = Math.Max(1, scroll.ViewportHeight);
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

    private void OnFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        if (_renderer.ScrollRegistry is not { } registry
            || !_focus.TryGetFocusedTarget(out var target)
            || target is null
            || _renderer.RefreshSnapshot().Root is not { } root
            || _layouts.GetLayoutByVNodeIdOrDefault(target.VNodeId) is not { Top: { } top, Height: { } height })
        {
            return;
        }

        var bottom = top + height;
        foreach (var node in MouseEventManager.Path(root, target.VNodeId).AsEnumerable().Reverse())
        {
            if (node.ID == target.VNodeId
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
