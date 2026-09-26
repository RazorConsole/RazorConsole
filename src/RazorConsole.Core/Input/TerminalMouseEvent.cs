// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

public enum TerminalMouseKind { Down, Up, Move, Wheel }

/// <summary>Zero-based terminal-cell coordinates; buttons use DOM numbering.</summary>
public readonly record struct TerminalMouseEvent(
    TerminalMouseKind Kind, int X, int Y, int Button = 0,
    int DeltaY = 0, ConsoleModifiers Modifiers = 0);

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
