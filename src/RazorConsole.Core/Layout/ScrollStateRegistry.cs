// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RazorConsole.Core.Extensions;

namespace RazorConsole.Core.Layout;

/// <summary>Content and viewport heights of a scroll container, measured by layout.</summary>
internal sealed record ScrollMetrics(int ContentHeight, int ViewportHeight)
{
    public static ScrollMetrics Empty { get; } = new(0, 0);

    /// <summary>Gets the largest valid scroll offset.</summary>
    public int MaxTop => Math.Max(0, ContentHeight - ViewportHeight);
}

/// <summary>Immutable snapshot of one scroll container's state.</summary>
internal sealed record ScrollState(int Top, bool Sticky, ScrollMetrics Metrics)
{
    public static ScrollState Default { get; } = new(0, false, ScrollMetrics.Empty);
}

internal enum ScrollChangeSource
{
    /// <summary>The offset was set from a component parameter; the owner already knows about it.</summary>
    Parameter,

    /// <summary>The offset was set by the engine: input, a controller or clamping during layout.</summary>
    Engine,
}

/// <summary>
/// Owns scroll state for every scroll container, keyed by scroll id.
/// </summary>
/// <remarks>
/// Threading: layout writes metrics from the layout path, input and controllers write offsets from the renderer
/// dispatcher, and readers (components, input) may be on any thread. Every entry is an immutable
/// <see cref="ScrollState"/>; writers swap it under <see cref="_gate"/> and readers take the current instance without locking.
/// Events are raised after the lock is released and may arrive on any thread.
/// </remarks>
internal sealed class ScrollStateRegistry(ILogger<ScrollStateRegistry>? logger = null)
{
    private readonly object _gate = new();
    private long _version;
    private readonly ConcurrentDictionary<string, ScrollState> _states = new(StringComparer.Ordinal);
    private readonly HashSet<string> _passIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedDuplicates = new(StringComparer.Ordinal);

    /// <summary>Raised when an offset changes for an engine-side reason. Arguments: scroll id and the new offset.</summary>
    public event Action<string, int>? TopChanged;

    /// <summary>Raised when the measured content or viewport height of a container changes.</summary>
    public event Action<string, ScrollMetrics>? MetricsChanged;

    /// <summary>Raised when state changed outside a Blazor render and the screen must be repainted.</summary>
    public event Action? RepaintRequested;

    /// <summary>
    /// Gets a counter that changes whenever an offset or follow-the-end flag is changed from outside layout.
    /// Layout's own clamping and metrics do not change it, so an unchanged value means a relayout would give the same result.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    public ScrollState Get(string scrollId)
        => _states.TryGetValue(scrollId, out var state) ? state : ScrollState.Default;

    /// <summary>Starts a layout pass; used to detect two containers claiming the same id.</summary>
    public void BeginLayoutPass()
    {
        lock (_gate)
        {
            _passIds.Clear();
        }
    }

    /// <summary>
    /// Sets the requested offset. The value is clamped to the known limits right away and again by the next layout.
    /// </summary>
    public void SetTop(string scrollId, int top, ScrollChangeSource source)
    {
        ScrollState previous;
        ScrollState next;
        lock (_gate)
        {
            previous = Get(scrollId);
            var max = previous.Metrics.ViewportHeight > 0 ? previous.Metrics.MaxTop : int.MaxValue;
            next = previous with { Top = Math.Clamp(top, 0, max) };
            _states[scrollId] = next;
            if (next != previous)
            {
                Interlocked.Increment(ref _version);
            }
        }

        if (next.Top == previous.Top)
        {
            return;
        }

        if (source == ScrollChangeSource.Engine)
        {
            TopChanged?.Invoke(scrollId, next.Top);
            RepaintRequested?.Invoke();
        }
    }

    public void SetSticky(string scrollId, bool sticky)
    {
        lock (_gate)
        {
            _states[scrollId] = Get(scrollId) with { Sticky = sticky };
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Records the metrics measured by layout and returns the offset to apply in this very pass, clamped to the new limits.
    /// </summary>
    public int ReportLayout(string scrollId, int contentHeight, int viewportHeight)
    {
        ScrollState previous;
        ScrollState next;
        var duplicate = false;
        lock (_gate)
        {
            duplicate = !_passIds.Add(scrollId) && _reportedDuplicates.Add(scrollId);
            previous = Get(scrollId);
            var metrics = new ScrollMetrics(Math.Max(0, contentHeight), Math.Max(0, viewportHeight));
            var top = previous.Sticky ? metrics.MaxTop : Math.Clamp(previous.Top, 0, metrics.MaxTop);
            next = new ScrollState(top, previous.Sticky, metrics);
            _states[scrollId] = next;
        }

        if (duplicate)
        {
            logger?.LogDuplicateScrollId(scrollId);
        }

        if (next.Metrics != previous.Metrics)
        {
            MetricsChanged?.Invoke(scrollId, next.Metrics);
        }

        if (next.Top != previous.Top)
        {
            TopChanged?.Invoke(scrollId, next.Top);
        }

        return next.Top;
    }

    public void Remove(string scrollId)
    {
        lock (_gate)
        {
            if (_states.TryRemove(scrollId, out _))
            {
                Interlocked.Increment(ref _version);
            }

            _reportedDuplicates.Remove(scrollId);
        }
    }

    /// <summary>Asks the host to relayout and repaint without a Blazor render.</summary>
    public void RequestRepaint()
        => RepaintRequested?.Invoke();
}
