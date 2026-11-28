// Copyright (c) RazorConsole. All rights reserved.

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core.Extensions;

namespace RazorConsole.Core.Input;

/// <summary>
/// The one place input reaches the app: it takes events from the terminal's input and input submitted by code, in the
/// order they arrived, and dispatches them one at a time to the keyboard and mouse managers.
/// </summary>
/// <remarks>
/// <para>
/// The terminal's input (<see cref="ITerminalInput"/>, the console in a real app) and submitted input
/// (<see cref="Submit"/>: the diagnostics endpoint, the headless test terminal, the browser host) share one queue, so
/// an injected key never overtakes a key typed before it.
/// </para>
/// <para>
/// Dispatching an event returns once its handlers have started, as it always has: the router does not wait for an
/// <c>async</c> handler to finish before the next input.
/// </para>
/// </remarks>
internal sealed class InputRouter
{
    private readonly KeyboardEventManager _keyboard;
    private readonly MouseEventManager? _mouse;
    private readonly ILogger _logger;
    private readonly Channel<(TerminalInputEvent? Event, InputSubmission? Submission)> _queue =
        Channel.CreateUnbounded<(TerminalInputEvent?, InputSubmission?)>(new UnboundedChannelOptions { SingleReader = true });
    private int _state; // 0: not started, 1: running, 2: stopped

    public InputRouter(KeyboardEventManager keyboard, MouseEventManager? mouse = null, ILogger<InputRouter>? logger = null)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _mouse = mouse;
        _logger = logger ?? NullLogger<InputRouter>.Instance;
    }

    /// <summary>
    /// Queues <paramref name="events"/> behind the input already waiting. Submitted before <see cref="RunAsync"/>
    /// starts, it waits for it; after the router stopped, it fails with <see cref="InputUnavailableException"/>.
    /// </summary>
    public InputSubmission Submit(IReadOnlyList<TerminalInputEvent> events)
    {
        var submission = new InputSubmission(events);
        if (Volatile.Read(ref _state) == 2 || !_queue.Writer.TryWrite((null, submission)))
        {
            submission.Fail(new InputUnavailableException(InputUnavailableReason.Stopped));
        }

        return submission;
    }

    /// <summary>
    /// Dispatches input until <paramref name="cancellationToken"/> is cancelled, or until <paramref name="input"/> ends
    /// (the terminal went away). Then fails the submissions still waiting, and every later one.
    /// </summary>
    /// <param name="input">The terminal's input, or <see langword="null"/> when all input is submitted.</param>
    /// <param name="cancellationToken">Stops the router.</param>
    public async Task RunAsync(ITerminalInput? input, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidOperationException("The input router is already running or has stopped.");
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reading = input is null
            ? Task.CompletedTask
            : ReadInputAsync(input, stop);
        try
        {
            await DispatchAllAsync(stop.Token).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _state, 2);
            _queue.Writer.TryComplete();
            await stop.CancelAsync().ConfigureAwait(false);
            await reading.ConfigureAwait(false);
            while (_queue.Reader.TryRead(out var item))
            {
                item.Submission?.Fail(new InputUnavailableException(InputUnavailableReason.Stopped));
            }
        }
    }

    // When the terminal's input ends, the router stops, as the console loop always did.
    private async Task ReadInputAsync(ITerminalInput input, CancellationTokenSource stop)
    {
        try
        {
            await input.RunAsync(e => _queue.Writer.TryWrite((e, null)), stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogTransientKeyboardInputFailure(ex);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task DispatchAllAsync(CancellationToken token)
    {
        try
        {
            await foreach (var (inputEvent, submission) in _queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                if (submission is not null)
                {
                    await DispatchSubmissionAsync(submission, token).ConfigureAwait(false);
                }
                else if (!await DispatchTerminalEventAsync(inputEvent!, token).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    // Returns false when the app can no longer take input, which stops the router as it stopped the console loop.
    private async Task<bool> DispatchTerminalEventAsync(TerminalInputEvent inputEvent, CancellationToken token)
    {
        try
        {
            await DispatchAsync(inputEvent, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is (InvalidOperationException and not InputUnavailableException) or IOException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogTransientKeyboardInputFailure(ex);
            await Task.Delay(200, token).ConfigureAwait(false);
            return true;
        }
    }

    // Submitted input never stops the router; its failure is reported to whoever submitted it.
    private async Task DispatchSubmissionAsync(InputSubmission submission, CancellationToken token)
    {
        if (!submission.TryStart())
        {
            // Withdrawn while waiting.
            return;
        }

        try
        {
            foreach (var inputEvent in submission.Events)
            {
                await DispatchAsync(inputEvent, token).ConfigureAwait(false);
            }

            submission.Complete();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            submission.Fail(new InputUnavailableException(InputUnavailableReason.Stopped));
            throw;
        }
        catch (Exception ex)
        {
            submission.Fail(ex);
        }
    }

    private Task DispatchAsync(TerminalInputEvent inputEvent, CancellationToken token)
        => inputEvent switch
        {
            KeyInputEvent key => _keyboard.HandleKeyAsync(key.Key, token),
            KeyBatchInputEvent batch => _keyboard.HandleKeyBatchAsync(batch.Keys, token),
            MouseInputEvent mouse => _mouse is null
                ? throw new InputUnavailableException(InputUnavailableReason.MouseDisabled)
                : _mouse.HandleAsync(mouse.Mouse, token),
            _ => throw new ArgumentOutOfRangeException(nameof(inputEvent), inputEvent, "Unknown input event."),
        };
}
