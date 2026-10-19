// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text;
using RazorConsole.Core.Input;

namespace RazorConsole.Testing.Vt;

/// <summary>
/// Encodes keys, text and mouse events the way an xterm-compatible terminal sends them to the application.
/// </summary>
internal static class VtInputEncoder
{
    private const string Escape = "\u001b";

    public static string EncodeKey(ConsoleKeyInfo key, bool applicationCursorKeys)
    {
        var modifiers = key.Modifiers;
        var parameter = 1
            + ((modifiers & ConsoleModifiers.Shift) != 0 ? 1 : 0)
            + ((modifiers & ConsoleModifiers.Alt) != 0 ? 2 : 0)
            + ((modifiers & ConsoleModifiers.Control) != 0 ? 4 : 0);

        var special = key.Key switch
        {
            ConsoleKey.UpArrow => Cursor('A', parameter, applicationCursorKeys),
            ConsoleKey.DownArrow => Cursor('B', parameter, applicationCursorKeys),
            ConsoleKey.RightArrow => Cursor('C', parameter, applicationCursorKeys),
            ConsoleKey.LeftArrow => Cursor('D', parameter, applicationCursorKeys),
            ConsoleKey.Home => Cursor('H', parameter, applicationCursorKeys),
            ConsoleKey.End => Cursor('F', parameter, applicationCursorKeys),
            ConsoleKey.Insert => Tilde(2, parameter),
            ConsoleKey.Delete => Tilde(3, parameter),
            ConsoleKey.PageUp => Tilde(5, parameter),
            ConsoleKey.PageDown => Tilde(6, parameter),
            ConsoleKey.F1 => Function('P', parameter),
            ConsoleKey.F2 => Function('Q', parameter),
            ConsoleKey.F3 => Function('R', parameter),
            ConsoleKey.F4 => Function('S', parameter),
            ConsoleKey.F5 => Tilde(15, parameter),
            ConsoleKey.F6 => Tilde(17, parameter),
            ConsoleKey.F7 => Tilde(18, parameter),
            ConsoleKey.F8 => Tilde(19, parameter),
            ConsoleKey.F9 => Tilde(20, parameter),
            ConsoleKey.F10 => Tilde(21, parameter),
            ConsoleKey.F11 => Tilde(23, parameter),
            ConsoleKey.F12 => Tilde(24, parameter),
            ConsoleKey.Tab when (modifiers & ConsoleModifiers.Shift) != 0 => Escape + "[Z",
            _ => null,
        };

        if (special is not null)
        {
            return special;
        }

        var text = key.Key switch
        {
            ConsoleKey.Enter => "\r",
            ConsoleKey.Tab => "\t",
            ConsoleKey.Backspace => "\u007f",
            ConsoleKey.Escape => Escape,
            _ => KeyText(key),
        };

        return (modifiers & ConsoleModifiers.Alt) != 0 ? Escape + text : text;
    }

    public static string EncodeMouse(TerminalMouseEvent input)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(input.X);
        ArgumentOutOfRangeException.ThrowIfNegative(input.Y);

        var flags = input.Kind == TerminalMouseKind.Wheel
            ? (input.DeltaY < 0 ? 64 : 65)
            : input.Button switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                -1 => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(input), input.Button, "Mouse buttons are 0 (left), 1 (middle), 2 (right) or -1 (none)."),
            };

        if (input.Kind == TerminalMouseKind.Move)
        {
            flags += 32;
        }

        flags += ((input.Modifiers & ConsoleModifiers.Shift) != 0 ? 4 : 0)
            + ((input.Modifiers & ConsoleModifiers.Alt) != 0 ? 8 : 0)
            + ((input.Modifiers & ConsoleModifiers.Control) != 0 ? 16 : 0);

        var final = input.Kind == TerminalMouseKind.Up ? 'm' : 'M';
        return string.Create(CultureInfo.InvariantCulture, $"{Escape}[<{flags};{input.X + 1};{input.Y + 1}{final}");
    }

    public static string EncodePaste(string text, bool bracketedPaste)
    {
        // A terminal sends line breaks in pasted text as CR.
        var normalized = text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        return bracketedPaste ? Escape + "[200~" + normalized + Escape + "[201~" : normalized;
    }

    private static string KeyText(ConsoleKeyInfo key)
    {
        var character = key.KeyChar;
        if ((key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            if (key.Key is >= ConsoleKey.A and <= ConsoleKey.Z)
            {
                return ((char)(key.Key - ConsoleKey.A + 1)).ToString();
            }

            if (key.Key == ConsoleKey.Spacebar)
            {
                return "\0";
            }
        }

        if (character != '\0')
        {
            return character.ToString();
        }

        if (key.Key == ConsoleKey.Spacebar)
        {
            return " ";
        }

        if (key.Key is >= ConsoleKey.A and <= ConsoleKey.Z)
        {
            var letter = (char)('a' + (key.Key - ConsoleKey.A));
            return ((key.Modifiers & ConsoleModifiers.Shift) != 0 ? char.ToUpperInvariant(letter) : letter).ToString();
        }

        if (key.Key is >= ConsoleKey.D0 and <= ConsoleKey.D9)
        {
            return ((char)('0' + (key.Key - ConsoleKey.D0))).ToString();
        }

        throw new ArgumentException($"The key {key.Key} has no terminal encoding. Pass its character or use SendTerminalInputAsync.", nameof(key));
    }

    private static string Cursor(char final, int parameter, bool applicationCursorKeys)
        => parameter > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{Escape}[1;{parameter}{final}")
            : applicationCursorKeys ? Escape + "O" + final : Escape + "[" + final;

    private static string Function(char final, int parameter)
        => parameter > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{Escape}[1;{parameter}{final}")
            : Escape + "O" + final;

    private static string Tilde(int code, int parameter)
        => parameter > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{Escape}[{code};{parameter}~")
            : string.Create(CultureInfo.InvariantCulture, $"{Escape}[{code}~");

    internal static byte[] ToBytes(string text) => Encoding.UTF8.GetBytes(text);
}
