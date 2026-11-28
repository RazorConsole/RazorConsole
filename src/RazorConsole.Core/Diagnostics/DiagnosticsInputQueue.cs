// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Input;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Bounds the input the diagnostics endpoint injects and hands it to the app's <see cref="InputRouter"/>, which
/// dispatches it behind input already waiting, the same way as terminal input.
/// </summary>
/// <remarks>
/// Injected data is decoded like input from a VT terminal, one key press per key, and is complete, so a trailing Escape
/// is a key press at once. Router failures surface as protocol errors on <see cref="DiagnosticsInputItem.Completion"/>.
/// </remarks>
internal sealed class DiagnosticsInputQueue(InputRouter router)
{
    public const int MaxPending = 32;

    private readonly TerminalInputDecoder _decoder = new();
    private int _pending;

    public DiagnosticsInputItem Enqueue(string? data, IReadOnlyList<TerminalMouseEvent>? mouse)
    {
        if (Interlocked.Increment(ref _pending) > MaxPending)
        {
            Interlocked.Decrement(ref _pending);
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.QueueOverflow,
                $"{MaxPending} injected inputs are already waiting for the app.");
        }

        var events = new List<TerminalInputEvent>();
        if (data is not null)
        {
            lock (_decoder)
            {
                events.AddRange(TerminalInputEvents.Decode(_decoder, data, endOfInput: true));
            }
        }

        if (mouse is not null)
        {
            events.AddRange(mouse.Select(static m => new MouseInputEvent(m)));
        }

        var submission = router.Submit(events);
        _ = submission.Completion.ContinueWith(
            _ => Interlocked.Decrement(ref _pending),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return new DiagnosticsInputItem(submission);
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
}

/// <summary>
/// One injected input: completes once its handlers have started, faults with a <see cref="DiagnosticsProtocolException"/>
/// when the app cannot take it, and is cancelled when withdrawn.
/// </summary>
internal sealed class DiagnosticsInputItem
{
    private readonly InputSubmission _submission;

    public DiagnosticsInputItem(InputSubmission submission)
    {
        _submission = submission;
        Completion = MapAsync(submission.Completion);
    }

    public Task Completion { get; }

    /// <summary>Withdraws the input if the app has not started it. Returns <see langword="false"/> when it already started.</summary>
    public bool TryCancel() => _submission.TryCancel();

    private static async Task MapAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (InputUnavailableException ex)
        {
            throw DiagnosticsInputQueue.Unavailable(ex.Reason == InputUnavailableReason.MouseDisabled
                ? DiagnosticsUnavailableReasons.MouseDisabled
                : DiagnosticsUnavailableReasons.InputStopped);
        }
    }
}
