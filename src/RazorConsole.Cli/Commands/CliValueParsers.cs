// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Testing.Vt;

namespace RazorConsole.Cli.Commands;

/// <summary>
/// Parsers for the value syntaxes of the CLI: durations, keys, regions, escaped terminal input and session names.
/// Each returns <see langword="false"/> with a message instead of throwing, so the message becomes a usage error.
/// </summary>
internal static class CliValueParsers
{
    /// <summary>
    /// The longest accepted duration. Anything longer is almost certainly a typo.
    /// </summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(1);

    public const int MaxSessionNameLength = 64;

    private static readonly Dictionary<string, ConsoleKey> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Up"] = ConsoleKey.UpArrow,
        ["Down"] = ConsoleKey.DownArrow,
        ["Left"] = ConsoleKey.LeftArrow,
        ["Right"] = ConsoleKey.RightArrow,
        ["Esc"] = ConsoleKey.Escape,
        ["Space"] = ConsoleKey.Spacebar,
        ["Return"] = ConsoleKey.Enter,
        ["Del"] = ConsoleKey.Delete,
        ["Ins"] = ConsoleKey.Insert,
        ["PgUp"] = ConsoleKey.PageUp,
        ["PgDn"] = ConsoleKey.PageDown,
    };

    private static readonly (string Prefix, ConsoleModifiers Modifier)[] ModifierPrefixes =
    [
        ("Ctrl+", ConsoleModifiers.Control),
        ("Control+", ConsoleModifiers.Control),
        ("Alt+", ConsoleModifiers.Alt),
        ("Shift+", ConsoleModifiers.Shift),
    ];

    /// <summary>
    /// Parses <c>500ms</c>, <c>2s</c>, <c>1.5s</c> or <c>1m</c>.
    /// </summary>
    public static bool TryParseDuration(string text, out TimeSpan value, [NotNullWhen(false)] out string? error)
    {
        value = default;
        var unitLength = text.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ? 2
            : text.EndsWith('s') || text.EndsWith('S') || text.EndsWith('m') || text.EndsWith('M') ? 1
            : 0;
        var number = text[..^unitLength];
        if (unitLength == 0
            || number.Length == 0
            || !number.All(static c => char.IsAsciiDigit(c) || c == '.')
            || !double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            error = $"'{text}' is not a duration. Use a number with ms, s or m, for example 500ms, 2s or 1m.";
            return false;
        }

        var milliseconds = text[^unitLength..].ToLowerInvariant() switch
        {
            "ms" => amount,
            "s" => amount * 1000,
            _ => amount * 60_000,
        };

        if (milliseconds > MaxDuration.TotalMilliseconds)
        {
            error = $"'{text}' is longer than the maximum of {MaxDuration.TotalHours:0} hours.";
            return false;
        }

        value = TimeSpan.FromMilliseconds(Math.Round(milliseconds));
        error = null;
        return true;
    }

    /// <summary>
    /// Parses <c>[Ctrl+][Alt+][Shift+]&lt;Name|char&gt;</c>. Names are <see cref="ConsoleKey"/> names
    /// (case-insensitive) or short aliases such as <c>Up</c>, <c>Esc</c> and <c>Space</c>; a single character is
    /// typed as itself. Keys that have no terminal encoding are rejected.
    /// </summary>
    public static bool TryParseKey(string text, out ConsoleKeyInfo value, [NotNullWhen(false)] out string? error)
    {
        value = default;
        var rest = text;
        ConsoleModifiers modifiers = 0;
        var matched = true;
        while (matched)
        {
            matched = false;
            foreach (var (prefix, modifier) in ModifierPrefixes)
            {
                if (rest.Length > prefix.Length && rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= modifier;
                    rest = rest[prefix.Length..];
                    matched = true;
                }
            }
        }

        ConsoleKey key;
        char keyChar;
        if (rest.Length == 1)
        {
            var character = rest[0];
            if (char.IsAsciiLetter(character))
            {
                key = ConsoleKey.A + (char.ToLowerInvariant(character) - 'a');

                // A bare capital types itself; after a modifier the letter names the key, so Ctrl+C has no Shift.
                if (char.IsAsciiLetterUpper(character) && modifiers == 0)
                {
                    modifiers |= ConsoleModifiers.Shift;
                }

                keyChar = (modifiers & ConsoleModifiers.Shift) != 0 ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character);
            }
            else if (char.IsAsciiDigit(character))
            {
                key = ConsoleKey.D0 + (character - '0');
                keyChar = character;
            }
            else if (character == ' ')
            {
                key = ConsoleKey.Spacebar;
                keyChar = ' ';
            }
            else if (char.IsControl(character))
            {
                error = $"'{EscapeForMessage(text)}' is a control character. Use a key name such as Enter, or the raw verb.";
                return false;
            }
            else
            {
                key = default;
                keyChar = character;
            }
        }
        else if (KeyAliases.TryGetValue(rest, out key)
            || (rest.Length > 0 && char.IsAsciiLetter(rest[0]) && Enum.TryParse(rest, ignoreCase: true, out key) && Enum.IsDefined(key)))
        {
            keyChar = key switch
            {
                ConsoleKey.Enter => '\r',
                ConsoleKey.Tab => '\t',
                ConsoleKey.Escape => '\u001b',
                ConsoleKey.Backspace => '\b',
                ConsoleKey.Spacebar => ' ',
                _ => '\0',
            };
        }
        else
        {
            error = $"'{text}' is not a key. Use a key name such as Enter, Tab, Up or F5, or one character, with optional Ctrl+, Alt+ and Shift+.";
            return false;
        }

        if ((modifiers & ConsoleModifiers.Control) != 0 && key is >= ConsoleKey.A and <= ConsoleKey.Z)
        {
            keyChar = (char)(key - ConsoleKey.A + 1);
        }

        var info = new ConsoleKeyInfo(
            keyChar,
            key,
            (modifiers & ConsoleModifiers.Shift) != 0,
            (modifiers & ConsoleModifiers.Alt) != 0,
            (modifiers & ConsoleModifiers.Control) != 0);
        try
        {
            VtInputEncoder.EncodeKey(info, applicationCursorKeys: false);
        }
        catch (ArgumentException)
        {
            error = $"The key '{text}' cannot be sent to a terminal. Use the raw verb for other input.";
            return false;
        }

        value = info;
        error = null;
        return true;
    }

    /// <summary>
    /// Parses a region <c>x,y,width,height</c>.
    /// </summary>
    public static bool TryParseRegion(string text, [NotNullWhen(true)] out DiagnosticsRegion? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        var parts = text.Split(',');
        var numbers = new int[4];
        if (parts.Length != 4
            || !parts.Select((part, i) => int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])).All(static ok => ok)
            || numbers[2] < 1
            || numbers[3] < 1)
        {
            error = $"'{text}' is not a region. Use x,y,width,height with a width and height of at least 1, for example 0,0,40,10.";
            return false;
        }

        value = new DiagnosticsRegion(numbers[0], numbers[1], numbers[2], numbers[3]);
        error = null;
        return true;
    }

    /// <summary>
    /// Decodes <c>\e</c>, <c>\xHH</c>, <c>\uHHHH</c>, <c>\r</c>, <c>\n</c>, <c>\t</c>, <c>\0</c> and <c>\\</c>.
    /// </summary>
    public static bool TryDecodeEscapes(string text, [NotNullWhen(true)] out string? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\')
            {
                builder.Append(text[i]);
                continue;
            }

            if (i + 1 >= text.Length)
            {
                error = "The input ends with a lone backslash. Write \\\\ for a backslash.";
                return false;
            }

            var escape = text[++i];
            switch (escape)
            {
                case 'e':
                    builder.Append('\u001b');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case 'x' or 'u':
                    var digits = escape == 'x' ? 2 : 4;
                    if (i + digits >= text.Length
                        || !int.TryParse(text.AsSpan(i + 1, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                    {
                        error = $"\\{escape} needs {digits} hex digits.";
                        return false;
                    }

                    builder.Append((char)code);
                    i += digits;
                    break;
                default:
                    error = $"Unknown escape \\{escape}. Use \\e, \\xHH, \\uHHHH, \\r, \\n, \\t, \\0 or \\\\.";
                    return false;
            }
        }

        value = builder.ToString();
        error = null;
        return true;
    }

    /// <summary>
    /// Parses <c>NAME=value</c>. The value may be empty; the name may not.
    /// </summary>
    public static bool TryParseEnvironmentVariable(string text, out KeyValuePair<string, string> value, [NotNullWhen(false)] out string? error)
    {
        value = default;
        var separator = text.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            error = $"'{text}' is not an environment variable. Use NAME=value.";
            return false;
        }

        value = new(text[..separator], text[(separator + 1)..]);
        error = null;
        return true;
    }

    /// <summary>
    /// Parses <c>left</c>, <c>middle</c> or <c>right</c> into DOM button numbering.
    /// </summary>
    public static bool TryParseButton(string text, out int value, [NotNullWhen(false)] out string? error)
    {
        value = text.ToLowerInvariant() switch
        {
            "left" => 0,
            "middle" => 1,
            "right" => 2,
            _ => -1,
        };

        error = value < 0 ? $"'{text}' is not a mouse button. Use left, middle or right." : null;
        return value >= 0;
    }

    /// <summary>
    /// Session names become file names in the state directory: a letter or digit, then letters, digits,
    /// <c>.</c>, <c>_</c> or <c>-</c>, at most 64 characters.
    /// </summary>
    public static bool TryValidateSessionName(string text, [NotNullWhen(false)] out string? error)
    {
        if (text.Length is 0 or > MaxSessionNameLength
            || !char.IsAsciiLetterOrDigit(text[0])
            || !text.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            error = $"'{text}' is not a session name. Use up to {MaxSessionNameLength} letters, digits, '.', '_' or '-', starting with a letter or digit.";
            return false;
        }

        error = null;
        return true;
    }

    private static string EscapeForMessage(string text)
        => string.Concat(text.Select(static c => char.IsControl(c) ? $"\\x{(int)c:x2}" : c.ToString()));
}
