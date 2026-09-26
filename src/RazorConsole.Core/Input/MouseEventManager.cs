// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components.Web;
using RazorConsole.Core.Focus;
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
    public string LastDispatch { get; private set; } = "No mouse input";

    public async Task HandleAsync(TerminalMouseEvent input, CancellationToken token = default)
    {
        var snapshot = renderer.RefreshSnapshot();
        if (snapshot.Root is null)
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
        await DispatchAsync(snapshot.Root, target?.ID, name, input, token).ConfigureAwait(false);
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

    private async Task DispatchAsync(VNode root, string? id, string name, TerminalMouseEvent input, CancellationToken token)
    {
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
            if (handler.Options.StopPropagation)
            {
                break;
            }
        }
    }

    private VNode? Hit(VNode node, int x, int y)
    {
        var bounds = layouts.GetLayoutByVNodeIdOrDefault(node.ID);
        if (bounds is { Left: int left, Top: int top, Width: int width, Height: int height }
            && (x < left || y < top || x >= left + width || y >= top + height))
        {
            return null;
        }

        foreach (var child in node.Children.OrderBy(c => layouts.GetLayoutByVNodeIdOrDefault(c.ID)?.ZIndex ?? 0).Reverse())
        {
            var found = Hit(child, x, y);
            if (found is not null)
            {
                return found;
            }
        }
        return bounds is not null && node.Kind == VNodeKind.Element ? node : null;
    }

    private static VNode? Find(VNode node, string id)
        => node.ID == id ? node : node.Children.Select(child => Find(child, id)).FirstOrDefault(found => found is not null);

    private static List<VNode> Path(VNode node, string? id)
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
