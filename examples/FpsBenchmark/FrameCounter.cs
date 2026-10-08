// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;

namespace FpsBenchmark;

/// <summary>Counts frames that were actually rendered to the live display.</summary>
public sealed class FrameCounter
{
    private long _frames;
    private long _writes;
    public int Rows { get; init; }

    public long Frames => Interlocked.Read(ref _frames);

    public long StartTimestamp { get; } = Stopwatch.GetTimestamp();

    public void RecordFrame() => Interlocked.Increment(ref _frames);

    public long Writes => Interlocked.Read(ref _writes);

    public void RecordWrite() => Interlocked.Increment(ref _writes);
}
