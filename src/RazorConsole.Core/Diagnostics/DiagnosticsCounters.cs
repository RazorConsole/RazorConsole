// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Frame counters, updated without locks from the render and output threads.
/// </summary>
internal sealed class DiagnosticsCounters
{
    private long _frames;
    private long _droppedFrames;
    private long _lastRenderTicks;
    private long _totalRenderTicks;
    private long _maxRenderTicks;

    /// <summary>
    /// Records one built frame and how long building it took, in <see cref="Stopwatch"/> ticks.
    /// </summary>
    public void RecordFrame(long elapsedTicks)
    {
        Interlocked.Increment(ref _frames);
        Interlocked.Exchange(ref _lastRenderTicks, elapsedTicks);
        Interlocked.Add(ref _totalRenderTicks, elapsedTicks);

        var max = Interlocked.Read(ref _maxRenderTicks);
        while (elapsedTicks > max)
        {
            var previous = Interlocked.CompareExchange(ref _maxRenderTicks, elapsedTicks, max);
            if (previous == max)
            {
                break;
            }

            max = previous;
        }
    }

    /// <summary>
    /// Records a frame the output skipped because the previous write was still in progress.
    /// </summary>
    public void RecordDroppedFrame() => Interlocked.Increment(ref _droppedFrames);

    /// <summary>
    /// Reads the counters. Each value is exact; values may come from moments a frame apart.
    /// </summary>
    public DiagnosticsCountersSnapshot Read()
    {
        var frames = Interlocked.Read(ref _frames);
        var total = Interlocked.Read(ref _totalRenderTicks);
        return new DiagnosticsCountersSnapshot(
            frames,
            Interlocked.Read(ref _droppedFrames),
            ToMilliseconds(Interlocked.Read(ref _lastRenderTicks)),
            frames == 0 ? 0 : ToMilliseconds(total) / frames,
            ToMilliseconds(Interlocked.Read(ref _maxRenderTicks)));
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}

internal readonly record struct DiagnosticsCountersSnapshot(
    long Frames,
    long DroppedFrames,
    double LastRenderMs,
    double AverageRenderMs,
    double MaxRenderMs);
