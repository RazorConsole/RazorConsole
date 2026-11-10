// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// What the diagnostics endpoint reads from a running app: the latest frame, counters, recent errors and logs.
/// </summary>
/// <remarks>
/// It exists only when diagnostics are enabled (see <see cref="DiagnosticsActivation"/>). Otherwise it is never
/// registered or created. The renderer knows nothing about it: <see cref="DiagnosticsSnapshotObserver"/> fills it from
/// the renderer's snapshots.
/// </remarks>
internal sealed class DiagnosticsState
{
    public const int ErrorCapacity = 50;
    public const int LogCapacity = 1000;

    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly ConditionalWeakTable<Exception, object?> _recordedErrors = [];
    private DiagnosticsFrame? _latestFrame;
    private long _frameSequence;
    private long _deliveredFrames;

    /// <summary>
    /// Gets the number of render snapshots delivered to the observer. Snapshots replaced before delivery and the ones
    /// built for a resize are not counted.
    /// </summary>
    public long Frames => Interlocked.Read(ref _deliveredFrames);

    public void RecordDeliveredFrame() => Interlocked.Increment(ref _deliveredFrames);

    public DiagnosticsRing<DiagnosticsErrorEntry> Errors { get; } = new(ErrorCapacity);

    public DiagnosticsRing<DiagnosticsLogEntry> Logs { get; } = new(LogCapacity);

    public TimeSpan Uptime => Stopwatch.GetElapsedTime(_startedTimestamp);

    /// <summary>
    /// Gets the latest published frame, or <see langword="null"/> before the first one.
    /// </summary>
    public DiagnosticsFrame? LatestFrame => Volatile.Read(ref _latestFrame);

    /// <summary>
    /// Publishes the tree and layout of one frame with a single reference swap, so readers never mix two frames.
    /// </summary>
    /// <remarks>A snapshot's tree and layout collections are fresh per snapshot and never changed later.</remarks>
    public DiagnosticsFrame PublishFrame(
        VNode? root,
        IReadOnlyCollection<VNodeLayoutInfo>? layouts,
        IReadOnlyDictionary<string, string?>? layoutParents,
        IRenderable? renderable = null,
        long renderSequence = 0,
        int viewportWidth = 0,
        int viewportHeight = 0)
    {
        var frame = new DiagnosticsFrame(
            Interlocked.Increment(ref _frameSequence), root, layouts, layoutParents, renderSequence, renderable, viewportWidth, viewportHeight);
        Volatile.Write(ref _latestFrame, frame);
        return frame;
    }

    /// <summary>
    /// Records an error once. The renderer rethrows from its exception handler, so the same exception can reach it
    /// again on its way up; the table remembers seen exceptions without keeping them alive.
    /// </summary>
    public void RecordError(Exception exception, string source)
    {
        if (!_recordedErrors.TryAdd(exception, null))
        {
            return;
        }

        Errors.Add(new DiagnosticsErrorEntry(
            DateTimeOffset.UtcNow,
            source,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.ToString()));
    }
}

/// <summary>
/// One rendered frame. Tree and layout always come from the same frame.
/// </summary>
/// <param name="Sequence">Increases by one per published frame, starting at 1.</param>
/// <param name="Root">The rendered tree, or <see langword="null"/> when nothing is mounted.</param>
/// <param name="Layouts">Layout per node, or <see langword="null"/> when nothing is mounted.</param>
/// <param name="LayoutParents">Layout parent per node ID, when the pipeline provides it.</param>
/// <param name="RenderSequence">
/// The render snapshot sequence observers must have processed to match this frame, or 0 when there is nothing to wait for.
/// Observers get snapshots asynchronously, so a frame can be published before the focus manager has seen it.
/// </param>
/// <param name="Renderable">What the display paints for this frame, or <see langword="null"/> when nothing is mounted.</param>
/// <param name="ViewportWidth">The terminal width when the frame was published, to tell whether a resize made it stale.</param>
/// <param name="ViewportHeight">The terminal height when the frame was published.</param>
internal sealed record DiagnosticsFrame(
    long Sequence,
    VNode? Root,
    IReadOnlyCollection<VNodeLayoutInfo>? Layouts,
    IReadOnlyDictionary<string, string?>? LayoutParents,
    long RenderSequence = 0,
    IRenderable? Renderable = null,
    int ViewportWidth = 0,
    int ViewportHeight = 0)
{
    private readonly Lazy<TerminalCanvas?> _canvas = new(
        () => Renderable is WidgetCanvasRenderable widgets ? widgets.PaintToCanvas() : null,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Paints the frame on first use and returns the same canvas afterwards, whichever thread asks. It is
    /// <see langword="null"/> when nothing is mounted.
    /// </summary>
    /// <remarks>
    /// This is a second paint of the frame's widgets, on the caller's thread: the display paints the same tree on its
    /// own, and widgets only read their state while painting. A frame that the display dropped can still be the one
    /// reported here; the PTY screen stays the source of truth for what the terminal shows.
    /// </remarks>
    public TerminalCanvas? PaintCanvas() => _canvas.Value;
}

/// <summary>
/// One caught error.
/// </summary>
/// <param name="Timestamp">When it was caught, in UTC.</param>
/// <param name="Source">Where it was caught: <c>render</c> for component errors, <c>snapshot</c> for frame building.</param>
/// <param name="Type">The exception's full type name.</param>
/// <param name="Message">The exception's message.</param>
/// <param name="Details">The exception with its stack trace and inner exceptions.</param>
internal sealed record DiagnosticsErrorEntry(
    DateTimeOffset Timestamp,
    string Source,
    string Type,
    string Message,
    string Details);
