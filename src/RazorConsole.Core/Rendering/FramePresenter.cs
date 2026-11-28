// Copyright (c) RazorConsole. All rights reserved.

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// Paints frames and presents them to an <see cref="ITerminal"/> from a single frame loop.
/// </summary>
/// <remarks>
/// <see cref="UpdateTarget"/> and <see cref="Refresh"/> only request a frame: they never block and never throw for
/// presentation errors. Requests go into a channel with room for one item, so requests made while a frame is being
/// presented are merged into the next frame, and the latest target wins. The frame loop is the only code that paints
/// and calls the terminal, and it logs presentation errors.
/// <para>
/// Every frame is painted from the current <see cref="FrameSource"/> into a <see cref="TerminalFrame"/> before it is
/// presented, so a refresh without a new target paints the same source again and shows its current state.
/// </para>
/// </remarks>
internal sealed partial class FramePresenter : ConsoleLiveDisplayContext.ILiveDisplayCanvas, IAsyncDisposable, IDisposable
{
    private readonly ITerminal _terminal;
    private readonly ITerminalFrameObserver[] _observers;
    private readonly ILogger _logger;
    private readonly Channel<bool> _frames = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly Lock _waitersSync = new();
    private readonly List<(long Sequence, TaskCompletionSource Completion)> _waiters = [];
    private readonly Task _frameLoop;

    // Owned by the frame loop.
    private FrameSource? _source;
    private TerminalFrame? _lastFrame;
    private bool _lastPresentFailed;
    private bool _lastRefreshedHandlerFailed;
    private bool _lastObserverFailed;

    // The newest target that the frame loop has not applied yet.
    private FrameSource? _pendingTarget;

    // Frame requests made so far, and the requests the frame loop has finished (presented or failed).
    private long _requestedSequence;
    private long _completedSequence;
    private int _disposeState;

    /// <summary>
    /// Starts a frame loop that presents to <paramref name="terminal"/> and hands every presented frame to
    /// <paramref name="observers"/>. The presenter owns the terminal and disposes it when it is disposed.
    /// </summary>
    public FramePresenter(ITerminal terminal, ILogger? logger = null, IEnumerable<ITerminalFrameObserver>? observers = null)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _observers = observers?.ToArray() ?? [];
        _logger = logger ?? NullLogger.Instance;
        _frameLoop = Task.Run(FrameLoopAsync);
    }

    /// <summary>
    /// Occurs on the frame loop after a frame was presented.
    /// </summary>
    public event Action? Refreshed;

    /// <summary>
    /// The frame the frame loop presented last, or <see langword="null"/> before the first frame was presented.
    /// </summary>
    internal TerminalFrame? LastFrame => Volatile.Read(ref _lastFrame);

    /// <summary>
    /// Requests a frame painted from <paramref name="source"/>. A <see langword="null"/> source is ignored.
    /// </summary>
    public void UpdateTarget(FrameSource? source)
    {
        if (source is null)
        {
            return;
        }

        Volatile.Write(ref _pendingTarget, source);
        RequestFrame();
    }

    /// <summary>
    /// Requests a frame of the current target. Ignored until a target was set.
    /// </summary>
    public void Refresh() => RequestFrame();

    /// <summary>
    /// Returns a task that completes once every frame requested before the call has been presented, or has failed.
    /// </summary>
    public Task WhenIdleAsync(CancellationToken cancellationToken = default)
    {
        var target = Interlocked.Read(ref _requestedSequence);
        if (Interlocked.Read(ref _completedSequence) >= target || _frameLoop.IsCompleted)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waitersSync)
        {
            // Checked again under the lock, because the frame loop releases waiters under it.
            if (Interlocked.Read(ref _completedSequence) >= target || _frameLoop.IsCompleted)
            {
                return Task.CompletedTask;
            }

            _waiters.Add((target, completion));
        }

        return completion.Task.WaitAsync(cancellationToken);
    }

    private void RequestFrame()
    {
        Interlocked.Increment(ref _requestedSequence);

        // Fails when a request is already waiting (it is folded into that one) or after disposal.
        _frames.Writer.TryWrite(true);
    }

    private async Task FrameLoopAsync()
    {
        try
        {
            await foreach (var _ in _frames.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                // Every request counted up to here is served by this frame.
                var sequence = Interlocked.Read(ref _requestedSequence);

                // After disposal starts, the channel still holds at most one request. It is still presented when the
                // terminal keeps the last frame after it is disposed; otherwise it would only flash, so it is skipped.
                if (Volatile.Read(ref _disposeState) == 0 || _terminal.KeepsLastFrameAfterDispose)
                {
                    await PresentFrameAsync(sequence).ConfigureAwait(false);
                }

                CompleteWaiters(sequence);
            }
        }
        finally
        {
            CompleteWaiters(long.MaxValue);
        }
    }

    private async Task PresentFrameAsync(long sequence)
    {
        if (await TryPresentCurrentFrameAsync(sequence).ConfigureAwait(false) is not { } frame)
        {
            return;
        }

        NotifyObservers(frame);

        // Raised outside the presentation's try, so a failing handler is not reported as a presentation failure and
        // does not touch its failure state. Its failures are logged once per run, like presentation failures.
        try
        {
            Refreshed?.Invoke();
            _lastRefreshedHandlerFailed = false;
        }
        catch (Exception ex)
        {
            if (!_lastRefreshedHandlerFailed)
            {
                _lastRefreshedHandlerFailed = true;
                LogRefreshedHandlerFailed(_logger, ex);
            }
        }
    }

    /// <summary>
    /// Applies the pending target, paints a frame from it and presents that frame. Returns the frame when it was
    /// presented.
    /// </summary>
    private async ValueTask<TerminalFrame?> TryPresentCurrentFrameAsync(long sequence)
    {
        try
        {
            var target = Interlocked.Exchange(ref _pendingTarget, null);
            if (target is not null)
            {
                _source = target;
            }

            if (_source is null)
            {
                return null;
            }

            // Painted inside the try, like the presentation: a source that fails to paint is reported and retried with
            // the next frame, the same as a failed presentation.
            var frame = new TerminalFrame(sequence, _source, _source.Paint());
            await _terminal.PresentAsync(frame).ConfigureAwait(false);
            Volatile.Write(ref _lastFrame, frame);
            if (_lastPresentFailed)
            {
                _lastPresentFailed = false;
                LogPresentRecovered(_logger);
            }

            return frame;
        }
        catch (Exception ex)
        {
            // The terminal redraws everything with the next frame. Log the first failure of a run of failures only,
            // so a broken output does not flood the log at the frame rate.
            if (!_lastPresentFailed)
            {
                _lastPresentFailed = true;
                LogPresentFailed(_logger, ex);
            }

            return null;
        }
    }

    // Outside the presentation's try, like the Refreshed handlers: an observer failure is not a presentation failure.
    // Every observer sees every frame even when one of them throws; failures are logged once per run.
    private void NotifyObservers(TerminalFrame frame)
    {
        var failed = false;
        foreach (var observer in _observers)
        {
            try
            {
                observer.OnFramePresented(frame);
            }
            catch (Exception ex)
            {
                failed = true;
                if (!_lastObserverFailed)
                {
                    _lastObserverFailed = true;
                    LogObserverFailed(_logger, ex);
                }
            }
        }

        if (!failed)
        {
            _lastObserverFailed = false;
        }
    }

    private void CompleteWaiters(long sequence)
    {
        List<TaskCompletionSource>? completed = null;
        lock (_waitersSync)
        {
            if (sequence > Interlocked.Read(ref _completedSequence))
            {
                Interlocked.Exchange(ref _completedSequence, sequence);
            }

            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Sequence <= sequence)
                {
                    (completed ??= []).Add(_waiters[i].Completion);
                    _waiters.RemoveAt(i);
                }
            }
        }

        if (completed is not null)
        {
            foreach (var completion in completed)
            {
                completion.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Stops the frame loop, waits for the frame being presented, and then disposes the terminal, so nothing is
    /// presented after it. A frame requested but not started is still presented when the terminal keeps the last frame
    /// after it is disposed, and dropped otherwise.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _frames.Writer.TryComplete();
        await _frameLoop.ConfigureAwait(false);
        await _terminal.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the presenter synchronously. On a single-threaded runtime (the browser) use <see cref="DisposeAsync"/>,
    /// because the frame loop cannot finish while this call blocks the only thread.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    [LoggerMessage(Level = LogLevel.Error, Message = "Presenting a frame to the terminal failed. Later failures are not logged until a frame is presented again.")]
    private static partial void LogPresentFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Presenting frames to the terminal works again.")]
    private static partial void LogPresentRecovered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "A frame observer threw after a frame was presented. Later failures are not logged until every observer succeeds.")]
    private static partial void LogObserverFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A Refreshed handler threw after a frame was presented. Later failures are not logged until a handler run succeeds.")]
    private static partial void LogRefreshedHandlerFailed(ILogger logger, Exception exception);
}
