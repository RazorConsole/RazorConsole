// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Input;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Input injected through the diagnostics endpoint, waiting for the app's input loop.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="KeyboardEventManager.RunAsync"/> is the only reader of the console and the only caller of the key and
/// mouse handlers, whose state is not synchronized. It drains this queue between real inputs, so injected input is
/// serialized with real input and runs through the same handlers.
/// </para>
/// <para>
/// At most <see cref="MaxPending"/> items wait at once. After the input loop has ended, waiting and new items fail
/// with <see cref="DiagnosticsErrorCodes.InputUnavailable"/>.
/// </para>
/// </remarks>
internal sealed class DiagnosticsInputQueue
{
    public const int MaxPending = 32;

    private readonly ConcurrentQueue<DiagnosticsInputItem> _items = new();
    private readonly SemaphoreSlim _signal = new(0);
    private int _pending;
    private volatile bool _closed;

    /// <summary>
    /// Queues an item for the input loop. Await <see cref="DiagnosticsInputItem.Completion"/> for its handlers.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException">The loop has ended, or <see cref="MaxPending"/> items wait.</exception>
    public DiagnosticsInputItem Enqueue(string? data, IReadOnlyList<TerminalMouseEvent>? mouse)
    {
        if (_closed)
        {
            throw Unavailable();
        }

        if (Interlocked.Increment(ref _pending) > MaxPending)
        {
            Interlocked.Decrement(ref _pending);
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.QueueOverflow,
                $"{MaxPending} injected inputs are already waiting for the app.");
        }

        var item = new DiagnosticsInputItem(data, mouse);
        _items.Enqueue(item);
        _signal.Release();

        // The loop may have ended between the check above and the enqueue.
        if (_closed)
        {
            FailWaiting();
        }

        return item;
    }

    internal bool TryDequeue(out DiagnosticsInputItem item)
    {
        if (!_items.TryDequeue(out item!))
        {
            return false;
        }

        Interlocked.Decrement(ref _pending);
        return true;
    }

    /// <summary>
    /// Waits until an item is queued or the timeout elapses, whichever comes first.
    /// </summary>
    internal Task WaitAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        => _signal.WaitAsync(millisecondsTimeout, cancellationToken);

    /// <summary>
    /// Called once the input loop has ended: fails waiting items and refuses new ones.
    /// </summary>
    internal void Close()
    {
        _closed = true;
        FailWaiting();
    }

    internal static DiagnosticsProtocolException Unavailable(string reason = DiagnosticsUnavailableReasons.InputStopped)
        => new(
            DiagnosticsErrorCodes.InputUnavailable,
            reason == DiagnosticsUnavailableReasons.MouseDisabled
                ? "The app does not handle mouse input."
                : "The app's input loop is not running.",
            errorData: JsonSerializer.SerializeToElement(
                new DiagnosticsUnavailableData(reason),
                DiagnosticsJsonContext.Default.DiagnosticsUnavailableData));

    private void FailWaiting()
    {
        while (TryDequeue(out var item))
        {
            item.Fail(Unavailable());
        }
    }
}

/// <summary>
/// One injected input: VT data decoded like terminal input, or mouse events, or both (data first).
/// </summary>
internal sealed class DiagnosticsInputItem(string? data, IReadOnlyList<TerminalMouseEvent>? mouse)
{
    private const int Waiting = 0;
    private const int Started = 1;
    private const int Cancelled = 2;

    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;

    public string? Data { get; } = data;

    public IReadOnlyList<TerminalMouseEvent>? Mouse { get; } = mouse;

    /// <summary>
    /// Completes after the input's handlers have run, or fails when it cannot be delivered.
    /// </summary>
    public Task Completion => _completion.Task;

    /// <summary>
    /// Called by the input loop before dispatching. False when the item was cancelled while waiting.
    /// </summary>
    internal bool TryStart() => Interlocked.CompareExchange(ref _state, Started, Waiting) == Waiting;

    /// <summary>
    /// Withdraws the item if the input loop has not started it. False when its handlers already run or ran.
    /// </summary>
    public bool TryCancel()
    {
        if (Interlocked.CompareExchange(ref _state, Cancelled, Waiting) != Waiting)
        {
            return false;
        }

        _completion.TrySetCanceled();
        return true;
    }

    internal void Complete() => _completion.TrySetResult();

    internal void Fail(Exception exception) => _completion.TrySetException(exception);
}
