// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

/// <summary>Routes a serialized VT input stream from an embedded terminal.</summary>
internal sealed class TerminalInputDispatcher(KeyboardEventManager keyboard, MouseEventManager mouse)
{
    private readonly TerminalInputDecoder _decoder = new();

    // Call with empty data after an idle interval to resolve a standalone Escape.
    // The host must serialize calls, including idle flushes and resize events.
    public async Task HandleAsync(string data, CancellationToken token = default)
    {
        var now = Environment.TickCount64;
        if (data.Length > 0)
        {
            _decoder.Feed(data, now);
        }
        _decoder.FlushEscape(now);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (_decoder.TryReadMouse(out var input))
            {
                await mouse.HandleAsync(input, token).ConfigureAwait(false);
            }
            else if (_decoder.KeyAvailable)
            {
                await keyboard.HandleKeyAsync(_decoder.ReadKey(), token).ConfigureAwait(false);
            }
            else
            {
                break;
            }
        }
    }
}
