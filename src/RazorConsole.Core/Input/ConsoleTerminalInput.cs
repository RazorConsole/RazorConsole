// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

/// <summary>
/// The input of a real console: it reads keys and mouse reports from <see cref="IConsoleInput"/> and posts them as
/// events, grouping keys that are available together the way pastes have always been detected.
/// </summary>
/// <remarks>
/// A console that does not decode terminal sequences itself (input read through <see cref="Console.ReadKey(bool)"/>)
/// still gets SGR mouse reports decoded here when mouse events are enabled. A partial mouse report is never posted as
/// keys, so it cannot leak into a text input.
/// </remarks>
internal sealed class ConsoleTerminalInput(IConsoleInput console, ConsoleAppOptions? options = null) : ITerminalInput
{
    private bool MouseEnabled => options?.ConsoleLiveDisplayOptions.EnableMouseEvents == true;

    public async Task RunAsync(Action<TerminalInputEvent> post, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(post);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (console.TryReadMouse(out var mouse))
                {
                    post(new MouseInputEvent(mouse));
                    continue;
                }

                if (!console.KeyAvailable)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var key = console.ReadKey(intercept: true);
                if (!console.DecodesTerminalSequences && MouseEnabled
                    && (key.KeyChar == '\u001b' || (key.KeyChar == '[' && key.Modifiers.HasFlag(ConsoleModifiers.Alt))))
                {
                    await ReadEscapeAsync(key, post, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                post(TerminalInputEvents.Group(key, () => console.KeyAvailable, () => console.ReadKey(intercept: true)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // The console went away (closed, or input redirected and ended): this input ends.
        }
    }

    private async Task ReadEscapeAsync(ConsoleKeyInfo first, Action<TerminalInputEvent> post, CancellationToken cancellationToken)
    {
        var pending = new List<ConsoleKeyInfo> { first };
        var sequence = first.KeyChar == '[' ? "\u001b[" : "\u001b";
        var deadline = Environment.TickCount64 + 100;
        while (Environment.TickCount64 < deadline && sequence.Length < 64)
        {
            if (!console.KeyAvailable)
            {
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var next = console.ReadKey(true);
            pending.Add(next);
            sequence += next.KeyChar;
            if (!"\u001b[<".StartsWith(sequence, StringComparison.Ordinal) && !sequence.StartsWith("\u001b[<", StringComparison.Ordinal))
            {
                break;
            }

            if (sequence.Length > 3 && next.KeyChar is 'M' or 'm')
            {
                if (SgrMouseParser.TryParse(sequence, out var mouse))
                {
                    post(new MouseInputEvent(mouse));
                }

                return;
            }
        }

        // Never insert a partial mouse packet into the focused text input.
        if (sequence.StartsWith("\u001b[<", StringComparison.Ordinal))
        {
            return;
        }

        // Each key of an escape sequence that is not a mouse report is a key press of its own, as before.
        foreach (var key in pending)
        {
            post(new KeyInputEvent(key));
        }
    }
}
