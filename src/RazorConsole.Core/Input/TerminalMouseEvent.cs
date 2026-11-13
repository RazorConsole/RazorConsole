// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

public enum TerminalMouseKind { Down, Up, Move, Wheel }

/// <summary>Zero-based terminal-cell coordinates; buttons use DOM numbering.</summary>
public readonly record struct TerminalMouseEvent(
    TerminalMouseKind Kind, int X, int Y, int Button = 0,
    int DeltaY = 0, ConsoleModifiers Modifiers = 0);

internal static class MouseEventQueue
{
    /// <summary>
    /// Dequeues the mouse event at the head of the queue. A wheel event absorbs the wheel events queued right behind it
    /// at the same position and in the same direction, so a spin of the wheel is one scroll instead of one relayout per notch.
    /// </summary>
    public static bool TryDequeueMouse(Queue<(ConsoleKeyInfo? Key, TerminalMouseEvent? Mouse)> queue, out TerminalMouseEvent value)
    {
        if (!queue.TryPeek(out var head) || head.Mouse is not { } mouse)
        {
            value = default;
            return false;
        }

        queue.Dequeue();
        if (mouse.Kind == TerminalMouseKind.Wheel)
        {
            while (queue.TryPeek(out var next)
                && next.Mouse is { Kind: TerminalMouseKind.Wheel } following
                && following.X == mouse.X
                && following.Y == mouse.Y
                && Math.Sign(following.DeltaY) == Math.Sign(mouse.DeltaY)
                && following.Modifiers == mouse.Modifiers)
            {
                queue.Dequeue();
                mouse = mouse with { DeltaY = mouse.DeltaY + following.DeltaY };
            }
        }

        value = mouse;
        return true;
    }
}

internal static class SgrMouseParser
{
    public static bool TryParse(string sequence, out TerminalMouseEvent value)
    {
        value = default;
        if (!sequence.StartsWith("\u001b[<", StringComparison.Ordinal)
            || sequence.Length < 9 || sequence[^1] is not ('M' or 'm'))
        {
            return false;
        }

        var fields = sequence[3..^1].Split(';');
        if (fields.Length != 3 || !int.TryParse(fields[0], out var flags)
            || !int.TryParse(fields[1], out var column) || !int.TryParse(fields[2], out var row)
            || flags < 0 || flags > 127 || column <= 0 || row <= 0)
        {
            return false;
        }

        var modifiers = ((flags & 4) != 0 ? ConsoleModifiers.Shift : 0)
            | ((flags & 8) != 0 ? ConsoleModifiers.Alt : 0)
            | ((flags & 16) != 0 ? ConsoleModifiers.Control : 0);
        var button = (flags & 3) switch { 1 => 1, 2 => 2, 3 => -1, _ => 0 };
        var wheel = (flags & 64) != 0;
        if (wheel)
        {
            button = 0;
        }
        if (wheel && (flags & 3) > 1)
        {
            return false;
        }

        var kind = wheel ? TerminalMouseKind.Wheel : sequence[^1] == 'm'
            ? TerminalMouseKind.Up : (flags & 32) != 0 ? TerminalMouseKind.Move : TerminalMouseKind.Down;
        value = new(kind, column - 1, row - 1, button, wheel ? ((flags & 1) == 0 ? -3 : 3) : 0, modifiers);
        return true;
    }
}
