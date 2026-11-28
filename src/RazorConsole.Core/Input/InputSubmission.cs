// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

/// <summary>
/// Input handed to the <see cref="InputRouter"/> by code rather than read from the terminal: the diagnostics endpoint,
/// the headless test terminal, the browser host.
/// </summary>
/// <remarks>
/// It waits behind input already queued. <see cref="Completion"/> completes once the handlers of all its events have
/// started (as for terminal input, an <c>async</c> handler is not awaited to the end), and faults with the error of the
/// event that failed. Until the router starts it, it can be withdrawn with <see cref="TryCancel"/>.
/// </remarks>
internal sealed class InputSubmission
{
    private const int Waiting = 0;
    private const int Started = 1;
    private const int Cancelled = 2;

    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;

    public InputSubmission(IReadOnlyList<TerminalInputEvent> events)
    {
        Events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public IReadOnlyList<TerminalInputEvent> Events { get; }

    public Task Completion => _completion.Task;

    /// <summary>
    /// Withdraws the input if the router has not started it. Returns <see langword="false"/> when it already started.
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

    internal bool TryStart() => Interlocked.CompareExchange(ref _state, Started, Waiting) == Waiting;

    internal void Complete() => _completion.TrySetResult();

    internal void Fail(Exception exception) => _completion.TrySetException(exception);
}

/// <summary>Why input could not be dispatched.</summary>
internal enum InputUnavailableReason
{
    /// <summary>The input router is not running: the app stopped, or its terminal input ended.</summary>
    Stopped,

    /// <summary>A mouse event was sent to an app that does not handle the mouse.</summary>
    MouseDisabled,
}

/// <summary>Input could not be dispatched; <see cref="Reason"/> says why.</summary>
internal sealed class InputUnavailableException(InputUnavailableReason reason)
    : InvalidOperationException(reason == InputUnavailableReason.MouseDisabled
        ? "The app does not handle mouse input."
        : "The app's input loop is not running.")
{
    public InputUnavailableReason Reason { get; } = reason;
}
