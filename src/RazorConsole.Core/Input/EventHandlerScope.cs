// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

/// <summary>
/// Collects the event handlers that input dispatched within the current async flow, so a caller
/// (the test harness) can wait for them to finish while the input loop itself never waits.
/// </summary>
internal sealed class EventHandlerScope
{
    private static readonly AsyncLocal<EventHandlerScope?> s_current = new();

    private readonly Lock _sync = new();
    private readonly List<Task> _handlers = [];

    private EventHandlerScope()
    {
    }

    /// <summary>
    /// Starts collecting handlers dispatched by the calling async flow and the methods it calls.
    /// </summary>
    public static EventHandlerScope Begin()
    {
        var scope = new EventHandlerScope();
        s_current.Value = scope;
        return scope;
    }

    /// <summary>
    /// Adds a running handler's completion to the current scope, when there is one.
    /// </summary>
    /// <param name="completion">A task that never faults.</param>
    public static void Track(Task completion)
    {
        if (completion.IsCompleted)
        {
            return;
        }

        var scope = s_current.Value;
        if (scope is null)
        {
            return;
        }

        lock (scope._sync)
        {
            scope._handlers.Add(completion);
        }
    }

    /// <summary>
    /// Waits for every handler collected so far, including ones added while waiting.
    /// </summary>
    /// <returns><see langword="true"/> when at least one handler was still running.</returns>
    public async Task<bool> WhenAllAsync(CancellationToken cancellationToken)
    {
        var seen = 0;
        while (true)
        {
            Task[] pending;
            lock (_sync)
            {
                if (_handlers.Count == seen)
                {
                    return seen > 0;
                }

                pending = _handlers.GetRange(seen, _handlers.Count - seen).ToArray();
                seen = _handlers.Count;
            }

            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
