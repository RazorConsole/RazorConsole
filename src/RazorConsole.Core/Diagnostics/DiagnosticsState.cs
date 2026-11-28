// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// What the diagnostics endpoint reads from a running app: the latest frame, counters, recent errors and logs.
/// </summary>
/// <remarks>
/// It exists only when diagnostics are enabled (see <see cref="DiagnosticsActivation"/>). Otherwise it is never
/// registered or created. The renderer knows nothing about it: <see cref="DiagnosticsSnapshotObserver"/> fills it from
/// the renderer's snapshots, and <see cref="DiagnosticsFrameObserver"/> records the frames the display presents.
/// </remarks>
internal sealed class DiagnosticsState
{
    public const int ErrorCapacity = 50;
    public const int LogCapacity = 1000;

    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly ConditionalWeakTable<Exception, object?> _recordedErrors = [];
    private DiagnosticsFrame? _latestFrame;
    private TerminalFrame? _presentedFrame;
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
    /// Records the frame the display presented last, so a request reads the display's own cells instead of painting
    /// the widgets again.
    /// </summary>
    public void RecordPresentedFrame(TerminalFrame frame) => Volatile.Write(ref _presentedFrame, frame);

    /// <summary>
    /// Publishes the tree and layout of one frame with a single reference swap, so readers never mix two frames.
    /// </summary>
    /// <remarks>A snapshot's tree and layout collections are fresh per snapshot and never changed later.</remarks>
    public DiagnosticsFrame PublishFrame(
        VNode? root,
        IReadOnlyCollection<VNodeLayoutInfo>? layouts,
        IReadOnlyDictionary<string, string?>? layoutParents,
        FrameSource? source = null,
        long renderSequence = 0,
        int viewportWidth = 0,
        int viewportHeight = 0)
    {
        var frame = new DiagnosticsFrame(
            Interlocked.Increment(ref _frameSequence), root, layouts, layoutParents, renderSequence, source, viewportWidth, viewportHeight)
        {
            PresentedCanvas = PresentedCanvasFor,
        };
        Volatile.Write(ref _latestFrame, frame);
        return frame;
    }

    // The cells the display presented last for this source, or null when its latest frame came from another source (or
    // none was presented yet).
    private TerminalCanvas? PresentedCanvasFor(FrameSource source)
        => Volatile.Read(ref _presentedFrame) is { } presented && ReferenceEquals(presented.Source, source)
            ? presented.Canvas
            : null;

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
/// <param name="Source">What the display paints for this frame, or <see langword="null"/> when nothing is mounted.</param>
/// <param name="ViewportWidth">The terminal width when the frame was published, to tell whether a resize made it stale.</param>
/// <param name="ViewportHeight">The terminal height when the frame was published.</param>
internal sealed record DiagnosticsFrame(
    long Sequence,
    VNode? Root,
    IReadOnlyCollection<VNodeLayoutInfo>? Layouts,
    IReadOnlyDictionary<string, string?>? LayoutParents,
    long RenderSequence = 0,
    FrameSource? Source = null,
    int ViewportWidth = 0,
    int ViewportHeight = 0)
{
    private readonly Lazy<TerminalCanvas?> _canvas = new(
        () => Source?.Paint(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Looks up the cells the display presented last for <see cref="Source"/>, or <see langword="null"/> when it has not
    /// presented that source. Set by <see cref="DiagnosticsState.PublishFrame"/>.
    /// </summary>
    internal Func<FrameSource, TerminalCanvas?>? PresentedCanvas { get; init; }

    /// <summary>
    /// Returns the frame's cells: the ones the display presented last for this frame's source, or, when the display has
    /// not presented it, a canvas painted on first use and returned afterwards, whichever thread asks. It is
    /// <see langword="null"/> when nothing is mounted.
    /// </summary>
    /// <remarks>
    /// The display presents a frame shortly after its snapshot is published, and only when the tree changed or a repaint
    /// was requested, so a request can arrive before the display presented this source, or for a source it never
    /// presents. Only then are the widgets painted here, on the caller's thread; widgets only read their state while
    /// painting. The PTY screen stays the source of truth for what the terminal shows.
    /// </remarks>
    public TerminalCanvas? PaintCanvas()
        => Source is { } source && PresentedCanvas?.Invoke(source) is { } presented ? presented : _canvas.Value;
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
