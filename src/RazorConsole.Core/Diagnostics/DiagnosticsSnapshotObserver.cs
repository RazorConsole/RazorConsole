// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Hosting;
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
internal sealed class DiagnosticsSnapshotObserver(
    DiagnosticsState? state,
    ConsoleRenderer renderer,
    ITerminalViewport? viewport = null) : IObserver<ConsoleRenderer.RenderSnapshot>, IHostedService, IDisposable
{
    private readonly Lock _sync = new();
    private IDisposable? _subscription;
    private long _lastDeliveredSequence;

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
            _subscription ??= renderer.Subscribe(this);
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
            _lastDeliveredSequence = Math.Max(_lastDeliveredSequence, snapshot.Sequence);
            state!.RecordDeliveredFrame();
            Publish(snapshot, snapshot.Sequence, viewport?.Width ?? 0, viewport?.Height ?? 0);
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
    /// sequence: focus keys do not change with the size.
    /// </remarks>
    public void EnsureCurrent()
    {
        if (state is null || viewport is null || state.LatestFrame is not { } latest
            || (latest.ViewportWidth == viewport.Width && latest.ViewportHeight == viewport.Height))
        {
            return;
        }

        lock (_sync)
        {
            // Read before refreshing: a resize during the refresh leaves the frame stale, and the next request fixes it.
            var width = viewport.Width;
            var height = viewport.Height;
            if (state.LatestFrame is not { } current || (current.ViewportWidth == width && current.ViewportHeight == height))
            {
                return;
            }

            try
            {
                Publish(renderer.RefreshSnapshot(), _lastDeliveredSequence, width, height);
            }
            catch (ObjectDisposedException)
            {
                // The app is shutting down; the latest frame is all there is.
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
        => state!.PublishFrame(
            snapshot.Root,
            snapshot.Layouts,
            snapshot.LayoutParents,
            snapshot.Renderable,
            renderSequence,
            width,
            height);
}
