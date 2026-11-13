// Copyright (c) RazorConsole. All rights reserved.

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Core;

/// <summary>
/// Writes frames to the console from a single frame loop.
/// </summary>
/// <remarks>
/// <see cref="UpdateTarget"/> and <see cref="Refresh"/> only request a frame: they never block and never throw for
/// write errors. Requests go into a channel with room for one item, so requests made while a frame is being written
/// are merged into the next frame, and the latest target wins. The frame loop is the only code that writes to the
/// console, and it logs write errors.
/// </remarks>
internal sealed partial class LiveDisplayCanvas : ConsoleLiveDisplayContext.ILiveDisplayCanvas, IAsyncDisposable, IDisposable
{
    private readonly ConsoleLiveDisplayOptions _options;
    private readonly IAnsiConsole _ansiConsole;
    private readonly ILogger _logger;
    private readonly bool _useAlternateScreenBuffer;
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
    private DiffRenderable? _current;
    private bool _alternateScreenBufferActive;
    private bool _lastWriteFailed;

    // The newest target that the frame loop has not applied yet.
    private IRenderable? _pendingTarget;

    // Frame requests made so far, and the requests the frame loop has finished (written or failed).
    private long _requestedSequence;
    private long _completedSequence;
    private int _disposeState;

    public LiveDisplayCanvas(ConsoleLiveDisplayOptions options, IAnsiConsole ansiConsole, ILogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ansiConsole = ansiConsole ?? throw new ArgumentNullException(nameof(ansiConsole));
        _logger = logger ?? NullLogger.Instance;
        _useAlternateScreenBuffer = options.UseAlternateScreenBuffer || options.EnableMouseEvents;
        _frameLoop = Task.Run(FrameLoopAsync);
    }

    /// <summary>
    /// Occurs on the frame loop after a frame was written.
    /// </summary>
    public event Action? Refreshed;

    /// <summary>
    /// Requests a frame showing <paramref name="renderable"/>. A <see langword="null"/> target is ignored.
    /// </summary>
    public void UpdateTarget(IRenderable? renderable)
    {
        if (renderable is null)
        {
            return;
        }

        Volatile.Write(ref _pendingTarget, renderable);
        RequestFrame();
    }

    /// <summary>
    /// Requests a frame of the current target. Ignored until a target was set.
    /// </summary>
    public void Refresh() => RequestFrame();

    /// <summary>
    /// Returns a task that completes once every frame requested before the call has been written, or has failed.
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
                if (Volatile.Read(ref _disposeState) == 0)
                {
                    WriteFrame();
                }

                CompleteWaiters(sequence);
            }
        }
        finally
        {
            CompleteWaiters(long.MaxValue);
        }
    }

    private void WriteFrame()
    {
        try
        {
            var target = Interlocked.Exchange(ref _pendingTarget, null);
            if (target is not null)
            {
                if (_current is null)
                {
                    _current = new DiffRenderable(target, hideCursor: _options.HideCursor);
                }
                else
                {
                    _current.UpdateRenderable(target);
                }
            }

            if (_current is null)
            {
                return;
            }

            EnterAlternateScreenBufferIfNeeded();
            WriteCurrent();
            if (_lastWriteFailed)
            {
                _lastWriteFailed = false;
                LogWriteRecovered(_logger);
            }

            Refreshed?.Invoke();
        }
        catch (Exception ex)
        {
            // WriteCurrent reset the diff state, so the next frame redraws everything. Log the first failure of a
            // run of failures only, so a broken output does not flood the log at the frame rate.
            if (!_lastWriteFailed)
            {
                _lastWriteFailed = true;
                LogWriteFailed(_logger, ex);
            }
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

    private void WriteCurrent()
    {
        try
        {
            try
            {
                if (_options.EnableSynchronizedUpdates)
                {
                    _ansiConsole.Write(new ControlCode(SM(SynchronizedOutput)));
                }

                _ansiConsole.Write(_current!);
            }
            finally
            {
                if (_options.EnableSynchronizedUpdates)
                {
                    _ansiConsole.Write(new ControlCode(RM(SynchronizedOutput)));
                }
            }
        }
        catch
        {
            _current?.Invalidate();
            throw;
        }
    }

    /// <summary>
    /// Stops the frame loop, waits for the frame being written, and then leaves the alternate screen buffer, so no
    /// frame is written after it. Frames requested but not started are dropped.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _frames.Writer.TryComplete();
        await _frameLoop.ConfigureAwait(false);
        LeaveAlternateScreenBuffer();
    }

    /// <summary>
    /// Disposes the canvas synchronously. On a single-threaded runtime (the browser) use <see cref="DisposeAsync"/>,
    /// because the frame loop cannot finish while this call blocks the only thread.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void EnterAlternateScreenBufferIfNeeded()
    {
        if (!_useAlternateScreenBuffer || _alternateScreenBufferActive)
        {
            return;
        }

        _ansiConsole.Write(new ControlCode(SM(DECALTSCR)));
        if (_options.EnableMouseEvents)
        {
            _ansiConsole.Write(new ControlCode("\u001b[?1003h\u001b[?1006h"));
        }
        _alternateScreenBufferActive = true;
    }

    private void LeaveAlternateScreenBuffer()
    {
        if (!_alternateScreenBufferActive)
        {
            return;
        }

        if (_options.EnableMouseEvents)
        {
            _ansiConsole.Write(new ControlCode("\u001b[?1003l\u001b[?1006l"));
        }
        _ansiConsole.Write(new ControlCode(RM(DECALTSCR)));
        _alternateScreenBufferActive = false;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Writing a frame to the console failed. Later failures are not logged until a frame is written again.")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Writing frames to the console works again.")]
    private static partial void LogWriteRecovered(ILogger logger);
}
