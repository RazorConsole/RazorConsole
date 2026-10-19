// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;

namespace RazorConsole.Testing;

/// <summary>
/// In-memory console input, so a headless terminal never reads the real console.
/// Keys are queued only while <see cref="HeadlessTerminal.PasteAsync"/> runs.
/// </summary>
internal sealed class HeadlessConsoleInput : IConsoleInput
{
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    private readonly object _sync = new();

    public bool KeyAvailable
    {
        get { lock (_sync) { return _keys.Count > 0; } }
    }

    public bool DecodesTerminalSequences => true;

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        lock (_sync)
        {
            return _keys.Count > 0
                ? _keys.Dequeue()
                : throw new InvalidOperationException("No headless console input is available.");
        }
    }

    public void Enqueue(IEnumerable<ConsoleKeyInfo> keys)
    {
        lock (_sync)
        {
            foreach (var key in keys)
            {
                _keys.Enqueue(key);
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _keys.Clear();
        }
    }
}
