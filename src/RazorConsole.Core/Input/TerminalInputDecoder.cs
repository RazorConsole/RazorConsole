// Copyright (c) RazorConsole. All rights reserved.

using System.Text;

namespace RazorConsole.Core.Input;

/// <summary>Incremental VT input decoding, before any Console.ReadKey interpretation.</summary>
internal sealed class TerminalInputDecoder
{
    private readonly StringBuilder _pending = new();
    private readonly Queue<(ConsoleKeyInfo? Key, TerminalMouseEvent? Mouse)> _events = [];
    private long _escapeSince;
    public bool KeyAvailable => _events.TryPeek(out var item) && item.Key.HasValue;
    public ConsoleKeyInfo ReadKey() => _events.Dequeue().Key!.Value;
    public bool TryReadMouse(out TerminalMouseEvent mouse)
    {
        if (_events.TryPeek(out var item) && item.Mouse is { } value)
        {
            _events.Dequeue();
            mouse = value;
            return true;
        }
        mouse = default;
        return false;
    }

    public void Feed(ReadOnlySpan<char> input, long now)
    {
        if (_pending.Length == 0)
        {
            _escapeSince = now;
        }
        _pending.Append(input);
        while (_pending.Length > 0)
        {
            if (_pending[0] != '\u001b')
            {
                Character(_pending[0]);
                _pending.Remove(0, 1);
                continue;
            }
            if (_pending.Length == 1)
            {
                _escapeSince = now;
                break;
            }
            if (_pending[1] is not ('[' or 'O'))
            {
                Character(_pending[1], ConsoleModifiers.Alt);
                _pending.Remove(0, 2);
                continue;
            }
            var end = 2;
            while (end < _pending.Length && _pending[end] is not (>= '@' and <= '~') && _pending[end] != '\u001b')
            {
                end++;
            }
            if (end == _pending.Length)
            {
                if (_pending.Length > 256)
                {
                    // Bounded malformed packet; retain the prefix so its tail
                    // cannot leak into the text input on the next read.
                    _pending.Remove(2, _pending.Length - 2);
                }
                break;
            }
            if (_pending[end] == '\u001b')
            {
                _pending.Remove(0, end);
                continue;
            }
            var sequence = _pending.ToString(0, end + 1);
            _pending.Remove(0, end + 1);
            if (sequence.StartsWith("\u001b[<", StringComparison.Ordinal))
            {
                if (SgrMouseParser.TryParse(sequence, out var mouse))
                {
                    _events.Enqueue((null, mouse));
                }
                continue;
            }
            Sequence(sequence);
        }
    }

    public void FlushEscape(long now)
    {
        if (_pending.Length == 1 && _pending[0] == '\u001b' && now - _escapeSince >= 50)
        {
            Character('\u001b');
            _pending.Clear();
        }
    }

    private void Sequence(string sequence)
    {
        // Cursor-position reports are protocol replies, not F3 key presses.
        // No cursor queries are issued by the renderer; discard stale replies.
        // SS3 R remains the unambiguous F3 keyboard encoding.
        if (sequence.StartsWith("\u001b[", StringComparison.Ordinal) && sequence[^1] == 'R'
            && sequence[2..^1].Split(';') is [var row, var column]
            && int.TryParse(row.TrimStart('?'), out var cursorRow) && cursorRow > 0
            && int.TryParse(column, out var cursorColumn) && cursorColumn > 0)
        {
            return;
        }
        var fields = sequence[2..^1].Split(';');
        var modifiers = fields.Length > 1 && int.TryParse(fields[^1], out var modifier) ? modifier - 1 : 0;
        var flags = ((modifiers & 1) != 0 ? ConsoleModifiers.Shift : 0)
            | ((modifiers & 2) != 0 ? ConsoleModifiers.Alt : 0)
            | ((modifiers & 4) != 0 ? ConsoleModifiers.Control : 0);
        var key = sequence[^1] switch
        {
            'A' => ConsoleKey.UpArrow,
            'B' => ConsoleKey.DownArrow,
            'C' => ConsoleKey.RightArrow,
            'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            'P' => ConsoleKey.F1,
            'Q' => ConsoleKey.F2,
            'R' => ConsoleKey.F3,
            'S' => ConsoleKey.F4,
            'Z' => ConsoleKey.Tab,
            '~' => fields[0] switch
            {
                "1" or "7" => ConsoleKey.Home,
                "2" => ConsoleKey.Insert,
                "3" => ConsoleKey.Delete,
                "4" or "8" => ConsoleKey.End,
                "5" => ConsoleKey.PageUp,
                "6" => ConsoleKey.PageDown,
                "11" => ConsoleKey.F1,
                "12" => ConsoleKey.F2,
                "13" => ConsoleKey.F3,
                "14" => ConsoleKey.F4,
                "15" => ConsoleKey.F5,
                "17" => ConsoleKey.F6,
                "18" => ConsoleKey.F7,
                "19" => ConsoleKey.F8,
                "20" => ConsoleKey.F9,
                "21" => ConsoleKey.F10,
                "23" => ConsoleKey.F11,
                "24" => ConsoleKey.F12,
                _ => (ConsoleKey)0,
            },
            _ => (ConsoleKey)0,
        };
        if (sequence[^1] == 'Z')
        {
            flags |= ConsoleModifiers.Shift;
        }
        if (key != 0)
        {
            Enqueue('\0', key, flags);
        }
    }

    private void Character(char value, ConsoleModifiers flags = 0)
    {
        var key = value switch
        {
            '\r' or '\n' => ConsoleKey.Enter,
            '\t' => ConsoleKey.Tab,
            '\u001b' => ConsoleKey.Escape,
            '\b' or '\u007f' => ConsoleKey.Backspace,
            ' ' => ConsoleKey.Spacebar,
            >= 'a' and <= 'z' => ConsoleKey.A + (value - 'a'),
            >= 'A' and <= 'Z' => ConsoleKey.A + (value - 'A'),
            >= '0' and <= '9' => ConsoleKey.D0 + (value - '0'),
            >= '\u0001' and <= '\u001a' => ConsoleKey.A + (value - 1),
            _ => (ConsoleKey)0,
        };
        if (char.IsUpper(value))
        {
            flags |= ConsoleModifiers.Shift;
        }
        if (value is >= '\u0001' and <= '\u001a' && value is not ('\t' or '\r' or '\n' or '\b'))
        {
            flags |= ConsoleModifiers.Control;
        }
        Enqueue(value, key, flags);
    }
    private void Enqueue(char value, ConsoleKey key, ConsoleModifiers flags)
        => _events.Enqueue((new(value, key, flags.HasFlag(ConsoleModifiers.Shift), flags.HasFlag(ConsoleModifiers.Alt), flags.HasFlag(ConsoleModifiers.Control)), null));
}
