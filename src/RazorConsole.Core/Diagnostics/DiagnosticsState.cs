// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// What the diagnostics endpoint reads from a running app: the latest frame, counters, recent errors and logs.
/// </summary>
/// <remarks>
/// It exists only when diagnostics are enabled (see <see cref="DiagnosticsActivation"/>). Otherwise it is never
/// registered or created, and every hook in the renderer is a null check.
/// </remarks>
internal sealed class DiagnosticsState
{
    public const int ErrorCapacity = 50;
    public const int LogCapacity = 1000;

    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly ConditionalWeakTable<Exception, object?> _recordedErrors = [];
    private DiagnosticsFrame? _latestFrame;
    private DiagnosticsScreen? _latestScreen;
    private long _frameSequence;

    public DiagnosticsCounters Counters { get; } = new();

    public DiagnosticsRing<DiagnosticsErrorEntry> Errors { get; } = new(ErrorCapacity);

    public DiagnosticsRing<DiagnosticsLogEntry> Logs { get; } = new(LogCapacity);

    public TimeSpan Uptime => Stopwatch.GetElapsedTime(_startedTimestamp);

    /// <summary>
    /// Gets the latest published frame, or <see langword="null"/> before the first one.
    /// </summary>
    public DiagnosticsFrame? LatestFrame => Volatile.Read(ref _latestFrame);

    /// <summary>
    /// Gets the canvas painted last, with the frame it shows, or <see langword="null"/> before the first paint and
    /// always on the legacy pipeline.
    /// </summary>
    /// <remarks>
    /// It can trail <see cref="LatestFrame"/>: frames built for hit tests or dropped by the display are never painted.
    /// </remarks>
    public DiagnosticsScreen? LatestScreen => Volatile.Read(ref _latestScreen);

    /// <summary>
    /// Publishes the tree and layout of one frame with a single reference swap, so readers never mix two frames.
    /// </summary>
    /// <remarks>The renderer passes a fresh tree and fresh layout collections per frame and never changes them later.</remarks>
    public DiagnosticsFrame PublishFrame(
        VNode? root,
        IReadOnlyCollection<VNodeLayoutInfo>? layouts,
        IReadOnlyDictionary<string, string?>? layoutParents)
    {
        var frame = new DiagnosticsFrame(Interlocked.Increment(ref _frameSequence), root, layouts, layoutParents);
        Volatile.Write(ref _latestFrame, frame);
        return frame;
    }

    /// <summary>
    /// Publishes a painted canvas with the frame it was painted from, with a single reference swap.
    /// </summary>
    /// <remarks>Called on the display thread after painting; the canvas is never written again.</remarks>
    public void PublishScreen(DiagnosticsFrame frame, TerminalCanvas canvas)
        => Volatile.Write(ref _latestScreen, new DiagnosticsScreen(frame, canvas));

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
/// <param name="Layouts">Layout per node, or <see langword="null"/> on the legacy pipeline.</param>
/// <param name="LayoutParents">Layout parent per node ID, when the pipeline provides it.</param>
internal sealed record DiagnosticsFrame(
    long Sequence,
    VNode? Root,
    IReadOnlyCollection<VNodeLayoutInfo>? Layouts,
    IReadOnlyDictionary<string, string?>? LayoutParents);

/// <summary>
/// The renderer's canvas as last painted, with the frame whose tree and layout it shows.
/// </summary>
internal sealed record DiagnosticsScreen(DiagnosticsFrame Frame, TerminalCanvas Canvas);

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
