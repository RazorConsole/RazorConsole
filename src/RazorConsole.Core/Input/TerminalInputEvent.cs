// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

/// <summary>
/// One piece of input from a terminal, already decoded: what <see cref="InputRouter"/> dispatches to the app.
/// </summary>
internal abstract record TerminalInputEvent;

/// <summary>A single key press.</summary>
internal sealed record KeyInputEvent(ConsoleKeyInfo Key) : TerminalInputEvent;

/// <summary>
/// Keys that arrived together, as from a paste or fast typing.
/// </summary>
/// <remarks>
/// Unless the focused element manages its own input, a run of printable keys and Backspace is applied to the focused
/// input as one <c>oninput</c>, and a key that is neither ends the run and is handled as a key press; the keys after it
/// are handled the same way. When the focused element manages its input, every key is a key press.
/// </remarks>
internal sealed record KeyBatchInputEvent(IReadOnlyList<ConsoleKeyInfo> Keys) : TerminalInputEvent;

/// <summary>A mouse event, in zero-based terminal cells.</summary>
internal sealed record MouseInputEvent(TerminalMouseEvent Mouse) : TerminalInputEvent;

/// <summary>
/// Turns keys and VT input into input events.
/// </summary>
internal static class TerminalInputEvents
{
    /// <summary>The most printable keys applied as one <c>oninput</c>.</summary>
    public const int MaxBatchSize = 1000;

    /// <summary>Whether a key is text that a batch applies to the focused input: a printable key or Backspace.</summary>
    public static bool IsBatchable(ConsoleKeyInfo key)
        => (!char.IsControl(key.KeyChar) && key.KeyChar != '\0') || key.Key == ConsoleKey.Backspace;

    /// <summary>
    /// Groups <paramref name="first"/> with the keys available right after it. A printable first key gathers available
    /// keys up to and including the first one that is not printable, as the console has always done; any other first
    /// key is a key press of its own.
    /// </summary>
    public static TerminalInputEvent Group(ConsoleKeyInfo first, Func<bool> keyAvailable, Func<ConsoleKeyInfo> readKey)
    {
        if (!IsBatchable(first) || !keyAvailable())
        {
            return new KeyInputEvent(first);
        }

        var keys = new List<ConsoleKeyInfo> { first };
        var batched = 1;
        while (batched < MaxBatchSize && keyAvailable())
        {
            var next = readKey();
            keys.Add(next);
            if (!IsBatchable(next))
            {
                break;
            }

            batched++;
        }

        return new KeyBatchInputEvent(keys);
    }

    /// <summary>
    /// Decodes VT input (keys, escape sequences and SGR mouse reports) into events, one per key press and mouse report.
    /// </summary>
    /// <remarks>
    /// Unlike console input, keys decoded here are never grouped into a <see cref="KeyBatchInputEvent"/>: VT input from
    /// the browser, the diagnostics endpoint and headless tests has always been pressed key by key.
    /// </remarks>
    /// <param name="decoder">The decoder of this input stream; it keeps an unfinished escape sequence for the next call.</param>
    /// <param name="data">The input. Empty data after an idle interval resolves a standalone Escape.</param>
    /// <param name="endOfInput">The data is complete (injected input), so a trailing Escape is a key press now.</param>
    public static List<TerminalInputEvent> Decode(TerminalInputDecoder decoder, string data, bool endOfInput)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(data);
        var now = Environment.TickCount64;
        if (data.Length > 0)
        {
            decoder.Feed(data, now);
        }

        decoder.FlushEscape(endOfInput ? long.MaxValue : now);
        var events = new List<TerminalInputEvent>();
        while (true)
        {
            if (decoder.TryReadMouse(out var mouse))
            {
                events.Add(new MouseInputEvent(mouse));
            }
            else if (decoder.KeyAvailable)
            {
                events.Add(new KeyInputEvent(decoder.ReadKey()));
            }
            else
            {
                return events;
            }
        }
    }
}
