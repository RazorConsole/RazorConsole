// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RazorConsole.Core.Rendering;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Fills <see cref="DiagnosticsState"/> from the renderer's snapshots, the way the focus manager follows them, so the
/// renderer itself knows nothing about diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// Each delivered snapshot becomes a frame. Its <see cref="DiagnosticsFrame.RenderSequence"/> is the snapshot's own
/// sequence, which is what <see cref="Focus.FocusManager.WaitForSnapshotAsync"/> waits for, so it does not matter which
/// observer is notified first.
/// </para>
/// <para>
/// Hosted, so it subscribes when the app starts. It does nothing when the options opt out of diagnostics
/// (<paramref name="state"/> is <see langword="null"/> then).
/// </para>
/// <para>
/// A resize repaints the display from a snapshot that is not delivered to observers. Instead of listening for it,
/// <see cref="EnsureCurrent"/> compares the latest frame's terminal size with the current one on a request, and only
/// when they differ builds a new snapshot and publishes that as a frame.
/// </para>
/// </remarks>
internal sealed partial class DiagnosticsSnapshotObserver(
    DiagnosticsState? state,
    ConsoleRenderer renderer,
    ITerminalViewport? viewport = null,
    ILogger<DiagnosticsSnapshotObserver>? logger = null) : IObserver<ConsoleRenderer.RenderSnapshot>, IHostedService, IDisposable
{
    private readonly Lock _sync = new();
    private IDisposable? _subscription;
    private bool _replaying;
    private long _lastDeliveredSequence;
    private long _latestSnapshotSequence;
    private bool _hasRenderedFrame;

    /// <summary>
    /// Subscribes to the renderer. The renderer replays its last snapshot right away.
    /// </summary>
    public void Start()
    {
        if (state is null)
        {
            return;
        }

        lock (_sync)
        {
            if (_subscription is not null)
            {
                return;
            }

            _replaying = true;
            try
            {
                _subscription = renderer.Subscribe(this);
            }
            finally
            {
                _replaying = false;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void OnNext(ConsoleRenderer.RenderSnapshot snapshot)
    {
        // Nothing was rendered yet: the renderer replays an empty snapshot to new observers.
        if (snapshot.Sequence == 0)
        {
            return;
        }

        lock (_sync)
        {
            // Nothing is mounted yet (the replay of a fresh renderer, or a snapshot built before the root rendered):
            // that is "no frame yet", not an app that renders nothing, so no frame is published and requests keep
            // waiting. Once a frame with a tree exists, a later empty snapshot is a real state and is published.
            if (snapshot.Root is null && !_hasRenderedFrame)
            {
                if (!_replaying)
                {
                    _lastDeliveredSequence = Math.Max(_lastDeliveredSequence, snapshot.Sequence);
                    state!.RecordDeliveredFrame();
                }

                return;
            }

            // The replay on subscribing is the renderer's last snapshot, which can be one built for a hit test or a
            // resize that no observer is ever told about. The focus manager never reaches its sequence, so the frame
            // names none to wait for. A snapshot that is delivered later replaces it with its own.
            var replayed = _replaying;
            if (!replayed)
            {
                _lastDeliveredSequence = Math.Max(_lastDeliveredSequence, snapshot.Sequence);
                state!.RecordDeliveredFrame();
            }

            // Snapshots can arrive out of order (a replay racing a delivery, or a refresh built meanwhile); the
            // frame never goes back to an older one.
            if (snapshot.Sequence >= _latestSnapshotSequence)
            {
                _latestSnapshotSequence = snapshot.Sequence;
                Publish(snapshot, replayed ? 0 : snapshot.Sequence, viewport?.Width ?? 0, viewport?.Height ?? 0);
            }
        }
    }

    public void OnError(Exception error) => state?.RecordError(error, "render");

    public void OnCompleted()
    {
    }

    /// <summary>
    /// Publishes a new frame when the terminal changed size since the latest one, so a request after a resize reads the
    /// layout and canvas for the new size.
    /// </summary>
    /// <remarks>
    /// The new snapshot is not delivered to the other observers, so the frame keeps waiting for the last delivered
    /// sequence: focus keys do not change with the size. A failure here is logged, and the request is answered from
    /// the latest frame: diagnostics never take the request, or the app, down.
    /// </remarks>
    public void EnsureCurrent()
    {
        if (state is null || viewport is null || state.LatestFrame is not { } latest
            || (latest.ViewportWidth == viewport.Width && latest.ViewportHeight == viewport.Height))
        {
            return;
        }

        // Read before refreshing: a resize during the refresh leaves the frame stale, and the next request fixes it.
        var width = viewport.Width;
        var height = viewport.Height;
        try
        {
            // Not under the lock: building the snapshot waits for the renderer's dispatcher.
            var snapshot = renderer.RefreshSnapshot();
            lock (_sync)
            {
                if (snapshot.Sequence >= _latestSnapshotSequence && (snapshot.Root is not null || _hasRenderedFrame))
                {
                    _latestSnapshotSequence = snapshot.Sequence;
                    Publish(snapshot, _lastDeliveredSequence, width, height);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // The app is shutting down; the latest frame is all there is.
        }
        catch (Exception ex)
        {
            if (logger is not null)
            {
                LogRefreshFailed(logger, ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }

    private void Publish(ConsoleRenderer.RenderSnapshot snapshot, long renderSequence, int width, int height)
    {
        _hasRenderedFrame |= snapshot.Root is not null;
        state!.PublishFrame(
            snapshot.Root,
            snapshot.Layouts,
            snapshot.LayoutParents,
            snapshot.Renderable,
            renderSequence,
            width,
            height);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Diagnostics could not rebuild the frame after a resize; answering from the previous one.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
