// Copyright (c) RazorConsole. All rights reserved.

using System.Text;

namespace RazorConsole.Cli.Host;

/// <summary>
/// The end of a session's terminal output as plain text, for the error of a process that has exited: where .NET
/// prints an unhandled exception, and so the only place that says why an app crashed.
/// </summary>
internal static class OutputTail
{
    public const int MaxLines = 40;
    public const int MaxCharacters = 4096;

    // Enough raw output for MaxLines lines once escape sequences and screen redraws are gone.
    private const int RawBytes = 64 * 1024;

    /// <summary>
    /// The tail of the ring's output, or <see langword="null"/> when it has no text.
    /// </summary>
    public static string? From(OutputRing ring)
    {
        var (_, _, data) = ring.Read(Math.Max(0, ring.End - RawBytes), RawBytes);
        return Clean(data);
    }

    /// <summary>
    /// Removes terminal control sequences, keeps the last <see cref="MaxLines"/> non-empty lines and at most
    /// <see cref="MaxCharacters"/> characters of them, or returns <see langword="null"/> when nothing is left.
    /// </summary>
    /// <remarks>
    /// A pseudoterminal (ConPTY on Windows) positions the cursor instead of writing line breaks, so a cursor position
    /// ends a line, like a carriage return that is not followed by a line feed.
    /// </remarks>
    internal static string? Clean(string raw)
    {
        var lines = new List<string>();
        var line = new StringBuilder();

        void EndLine()
        {
            var text = line.ToString().TrimEnd();
            line.Clear();
            if (text.Length > 0)
            {
                lines.Add(text);
            }
        }

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            switch (c)
            {
                case '\u001b':
                    i = SkipSequence(raw, i, out var endsLine);
                    if (endsLine)
                    {
                        EndLine();
                    }

                    break;
                case '\n':
                    EndLine();
                    break;
                case '\r':
                    if (i + 1 >= raw.Length || raw[i + 1] != '\n')
                    {
                        EndLine();
                    }

                    break;
                case '\t':
                    line.Append(c);
                    break;
                default:
                    if (c >= ' ' && c != '\u007f')
                    {
                        line.Append(c);
                    }

                    break;
            }
        }

        EndLine();
        if (lines.Count == 0)
        {
            return null;
        }

        var tail = string.Join('\n', lines.Skip(Math.Max(0, lines.Count - MaxLines)));
        if (tail.Length > MaxCharacters)
        {
            var start = tail.Length - MaxCharacters;
            if (char.IsLowSurrogate(tail[start]))
            {
                start++;
            }

            tail = tail[start..];
        }

        return tail;
    }

    // Returns the index of the last character of the sequence that starts with the escape at start.
    private static int SkipSequence(string raw, int start, out bool endsLine)
    {
        endsLine = false;
        var next = start + 1;
        if (next >= raw.Length)
        {
            return start;
        }

        switch (raw[next])
        {
            case '[':
                // Parameters and intermediates, then a final byte 0x40-0x7E. H and f position the cursor.
                for (var i = next + 1; i < raw.Length; i++)
                {
                    if (raw[i] is >= '@' and <= '~')
                    {
                        endsLine = raw[i] is 'H' or 'f';
                        return i;
                    }
                }

                return raw.Length - 1;
            case ']':
            case 'P':
            case 'X':
            case '^':
            case '_':
                // A string that ends with BEL or ESC \.
                for (var i = next + 1; i < raw.Length; i++)
                {
                    if (raw[i] == '\u0007')
                    {
                        return i;
                    }

                    if (raw[i] == '\u001b' && i + 1 < raw.Length && raw[i + 1] == '\\')
                    {
                        return i + 1;
                    }
                }

                return raw.Length - 1;
            case '(' or ')' or '*' or '+' or '#':
                // Designate a character set, or a line attribute: one more character.
                return Math.Min(next + 1, raw.Length - 1);
            default:
                return next;
        }
    }
}
