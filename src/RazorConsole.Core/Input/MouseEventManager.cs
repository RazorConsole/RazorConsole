// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components.Web;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Input;

internal sealed class MouseEventManager(ConsoleRenderer renderer, IVNodeLayoutAccessor layouts,
    IKeyboardEventDispatcher dispatcher, FocusManager focus)
{
    private string? _capturedId;
    private string? _pressedId;
    private (int X, int Y) _pressedAt;
    private bool _moved;
    private string? _hoverId;

    // The engine handles the scrollbar itself: a press that starts on it is consumed until the button is released.
    private bool _scrollbarPressed;
    private ScrollDrag? _scrollDrag;

    private sealed record ScrollDrag(string NodeId, int GrabOffset);

    public string LastDispatch { get; private set; } = "No mouse input";

    public async Task HandleAsync(TerminalMouseEvent input, CancellationToken token = default)
    {
        var snapshot = renderer.RefreshSnapshot();
        if (snapshot.Root is null)
        {
            return;
        }

        if (HandleScrollbar(snapshot.Root, input))
        {
            return;
        }

        var hit = Hit(snapshot.Root, input.X, input.Y);
        if (input.Kind == TerminalMouseKind.Move && hit?.ID != _hoverId)
        {
            await DispatchAsync(snapshot.Root, _hoverId, "onmouseleave", input, token).ConfigureAwait(false);
            _hoverId = hit?.ID;
            await DispatchAsync(snapshot.Root, _hoverId, "onmouseenter", input, token).ConfigureAwait(false);
        }
        var target = input.Kind is TerminalMouseKind.Move or TerminalMouseKind.Up
            && _capturedId is not null ? Find(snapshot.Root, _capturedId) : hit;
        if (input.Kind == TerminalMouseKind.Down)
        {
            _pressedId = hit?.ID;
            _pressedAt = (input.X, input.Y);
            _moved = false;
            // Capture the event-owning ancestor, so a drag can leave its painted rectangle.
            _capturedId = FindHandlerPath(snapshot.Root, hit?.ID, "onmousedown").LastOrDefault()?.ID;
        }
        if (input.Kind == TerminalMouseKind.Move && (input.X, input.Y) != _pressedAt)
        {
            _moved = true;
        }

        var name = input.Kind switch
        {
            TerminalMouseKind.Down => "onmousedown",
            TerminalMouseKind.Up => "onmouseup",
            TerminalMouseKind.Wheel => "onwheel",
            _ => "onmousemove",
        };
        LastDispatch = $"{input.Kind} ({input.X},{input.Y}) button={input.Button} target={target?.ID ?? "<none>"}";
        if (input.Kind == TerminalMouseKind.Down && input.Button == 0)
        {
            var path = Path(snapshot.Root, target?.ID);
            foreach (var node in path.AsEnumerable().Reverse())
            {
                if (node.Attributes.TryGetValue("data-focus-key", out var key) && key is not null)
                {
                    await focus.FocusAsync(key, token).ConfigureAwait(false);
                    break;
                }
            }
        }
        var dispatched = await DispatchAsync(snapshot.Root, target?.ID, name, input, token).ConfigureAwait(false);
        if (input.Kind == TerminalMouseKind.Wheel)
        {
            ScrollNatively(snapshot.Root, target?.ID, input.DeltaY, dispatched);
        }
        if (input.Kind == TerminalMouseKind.Up)
        {
            var click = input.Button == 0 && !_moved && hit?.ID == _pressedId && hit is not null;
            _capturedId = null;
            _pressedId = null;
            if (click)
            {
                await DispatchAsync(renderer.RefreshSnapshot().Root!, hit!.ID, "onclick", input, token).ConfigureAwait(false);
            }
        }
    }

    // Clicks on a scrollbar track page the content; dragging the thumb scrolls it. No Razor handlers are involved,
    // and the drag keeps its own capture so the pointer can leave the track.
    private bool HandleScrollbar(VNode root, TerminalMouseEvent input)
    {
        if (renderer.ScrollRegistry is not { } registry)
        {
            return false;
        }

        if (_scrollDrag is { } drag)
        {
            switch (input.Kind)
            {
                case TerminalMouseKind.Move:
                    // Resolve the geometry again on every move, so a resize or relayout during the drag is followed.
                    if (layouts.GetLayoutByVNodeIdOrDefault(drag.NodeId)?.Scroll is { ScrollId: { } id } scroll)
                    {
                        var thumbTop = input.Y - scroll.Track.Y - drag.GrabOffset;
                        var top = ScrollbarGeometry.OffsetForThumbTop(
                            thumbTop, scroll.Track.Height, scroll.Thumb.Height, scroll.ViewportHeight, scroll.ContentHeight);
                        registry.SetTop(id, top, ScrollChangeSource.Engine);
                    }
                    else
                    {
                        _scrollDrag = null;
                    }

                    return true;
                case TerminalMouseKind.Up:
                    _scrollDrag = null;
                    _scrollbarPressed = false;
                    return true;
                case TerminalMouseKind.Wheel:
                    return false;
                default:
                    return true;
            }
        }

        if (_scrollbarPressed)
        {
            if (input.Kind == TerminalMouseKind.Up)
            {
                _scrollbarPressed = false;
            }

            return input.Kind != TerminalMouseKind.Wheel;
        }

        if (input.Kind != TerminalMouseKind.Down || input.Button != 0)
        {
            return false;
        }

        var point = new LayoutPoint(input.X, input.Y);
        if (FindScrollbar(root, point) is not { } found || found.Scroll.ScrollId is not { } scrollId)
        {
            return false;
        }

        _scrollbarPressed = true;
        var info = found.Scroll;
        if (info.Thumb.Contains(point))
        {
            _scrollDrag = new ScrollDrag(found.NodeId, input.Y - info.Thumb.Y);
            return true;
        }

        var page = Math.Max(1, info.ViewportHeight);
        registry.SetTop(scrollId, input.Y < info.Thumb.Y ? info.Top - page : info.Top + page, ScrollChangeSource.Engine);
        return true;
    }

    // The innermost scrollbar under the point; the pointer must also be where the container is visible.
    private (string NodeId, ScrollLayoutInfo Scroll)? FindScrollbar(VNode node, LayoutPoint point)
    {
        foreach (var child in node.Children.OrderBy(c => layouts.GetLayoutByVNodeIdOrDefault(c.ID)?.ZIndex ?? 0).Reverse())
        {
            if (FindScrollbar(child, point) is { } found)
            {
                return found;
            }
        }

        if (layouts.GetLayoutByVNodeIdOrDefault(node.ID) is { Scroll: { } scroll } layout
            && scroll.Track.Contains(point)
            && (layout.VisibleBounds is not { } visible || visible.Contains(point)))
        {
            return (node.ID, scroll);
        }

        return null;
    }

    // The wheel scrolls the innermost scroll container under the pointer that can still move in that direction, and chains outward
    // when it cannot. A handler that stops propagation shields the containers above it; preventDefault disables native scrolling.
    private void ScrollNatively(VNode root, string? id, int delta, DispatchResult dispatched)
    {
        if (delta == 0 || dispatched.DefaultPrevented || renderer.ScrollRegistry is not { } registry)
        {
            return;
        }

        foreach (var node in Path(root, id).AsEnumerable().Reverse())
        {
            if (layouts.GetLayoutByVNodeIdOrDefault(node.ID)?.Scroll is { ScrollId: { } scrollId })
            {
                var state = registry.Get(scrollId);
                var max = state.Metrics.MaxTop;
                if (delta > 0 ? state.Top < max : state.Top > 0)
                {
                    registry.SetTop(scrollId, Math.Clamp(state.Top + delta, 0, max), ScrollChangeSource.Engine);
                    return;
                }

                if (IsContained(node))
                {
                    return;
                }
            }

            if (node.ID == dispatched.StoppedAt)
            {
                return;
            }
        }
    }

    internal static bool IsContained(VNode node)
        => node.Attributes.TryGetValue("data-overscroll", out var value)
            && string.Equals(value, "contain", StringComparison.OrdinalIgnoreCase);

    private readonly record struct DispatchResult(string? StoppedAt, bool DefaultPrevented);

    private async Task<DispatchResult> DispatchAsync(VNode root, string? id, string name, TerminalMouseEvent input, CancellationToken token)
    {
        string? stoppedAt = null;
        var prevented = false;
        foreach (var node in FindHandlerPath(root, id, name).Reverse())
        {
            var handler = node.Events.First(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var bounds = layouts.GetLayoutByVNodeIdOrDefault(node.ID);
            MouseEventArgs args = input.Kind == TerminalMouseKind.Wheel ? new WheelEventArgs { DeltaY = input.DeltaY, DeltaMode = 1 } : new MouseEventArgs();
            args.Type = name[2..];
            args.ClientX = input.X;
            args.ClientY = input.Y;
            args.OffsetX = input.X - (bounds?.Left ?? 0);
            args.OffsetY = input.Y - (bounds?.Top ?? 0);
            args.Button = input.Button;
            args.Buttons = input.Kind is TerminalMouseKind.Up or TerminalMouseKind.Wheel || input.Button < 0 ? 0 : input.Button switch { 2 => 2, 1 => 4, _ => 1 };
            args.ShiftKey = input.Modifiers.HasFlag(ConsoleModifiers.Shift);
            args.CtrlKey = input.Modifiers.HasFlag(ConsoleModifiers.Control);
            args.AltKey = input.Modifiers.HasFlag(ConsoleModifiers.Alt);
            await dispatcher.DispatchAsync(handler.HandlerId, args, token).ConfigureAwait(false);
            prevented |= handler.Options.PreventDefault || HasFlag(node, "preventDefault", name);
            if (handler.Options.StopPropagation || HasFlag(node, "stopPropagation", name))
            {
                stoppedAt = node.ID;
                break;
            }
        }

        return new DispatchResult(stoppedAt, prevented);
    }

    // Razor compiles @onclick:stopPropagation and :preventDefault to attributes rather than event options.
    private static bool HasFlag(VNode node, string flag, string name)
        => node.Attributes.TryGetValue($"__internal_{flag}_{name}", out var value)
            && value is not null
            && !value.Equals("false", StringComparison.OrdinalIgnoreCase);

    private VNode? Hit(VNode node, int x, int y)
    {
        var bounds = layouts.GetLayoutByVNodeIdOrDefault(node.ID);
        var inside = bounds switch
        {
            // A node clipped by a scroll viewport only receives pointer events where it is actually visible.
            { VisibleBounds: { } visible } => visible.Contains(new LayoutPoint(x, y)),
            { Left: int left, Top: int top, Width: int width, Height: int height }
                => x >= left && y >= top && x < left + width && y < top + height,
            _ => true,
        };

        // Children are searched even outside the node: an absolutely positioned one (a dropdown) can lie beyond it.
        foreach (var child in node.Children.OrderBy(c => layouts.GetLayoutByVNodeIdOrDefault(c.ID)?.ZIndex ?? 0).Reverse())
        {
            var found = Hit(child, x, y);
            if (found is not null)
            {
                return found;
            }
        }
        return inside && bounds is not null && node.Kind == VNodeKind.Element ? node : null;
    }

    private static VNode? Find(VNode node, string id)
        => node.ID == id ? node : node.Children.Select(child => Find(child, id)).FirstOrDefault(found => found is not null);

    internal static List<VNode> Path(VNode node, string? id)
    {
        if (id is null)
        {
            return [];
        }

        if (node.ID == id)
        {
            return [node];
        }

        foreach (var child in node.Children)
        {
            var path = Path(child, id);
            if (path.Count == 0)
            {
                continue;
            }

            path.Insert(0, node);
            return path;
        }
        return [];
    }

    private static IEnumerable<VNode> FindHandlerPath(VNode root, string? id, string name)
        => Path(root, id).Where(n => n.Events.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
}
