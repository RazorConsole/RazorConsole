// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Diagnostics;

public sealed class DiagnosticsStateTests
{
    [Fact]
    public void Ring_KeepsTheNewestItemsOldestFirst()
    {
        var ring = new DiagnosticsRing<string>(3);
        ring.Read().ShouldBeEmpty();
        ring.LastSequence.ShouldBe(0);

        foreach (var item in new[] { "a", "b", "c", "d", "e" })
        {
            ring.Add(item);
        }

        ring.LastSequence.ShouldBe(5);
        ring.Read().ShouldBe([new(3, "c"), new(4, "d"), new(5, "e")]);
    }

    [Fact]
    public void Ring_ReadAfterASequenceReturnsOnlyNewerItems()
    {
        var ring = new DiagnosticsRing<int>(4);
        for (var i = 1; i <= 6; i++)
        {
            ring.Add(i * 10);
        }

        ring.Read(afterSequence: 4).ShouldBe([new(5, 50), new(6, 60)]);
        ring.Read(afterSequence: 6).ShouldBeEmpty();

        // Older than what is kept: everything kept, and the gap from 1 to 3 shows what was dropped.
        ring.Read(afterSequence: 0).Select(e => e.Sequence).ShouldBe([3L, 4L, 5L, 6L]);
    }

    [Fact]
    public async Task Ring_ConcurrentAddsGetUniqueSequences()
    {
        var ring = new DiagnosticsRing<int>(10_000);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(
            () =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    ring.Add(i);
                }
            },
            TestContext.Current.CancellationToken)));

        ring.Read().Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, 8000).Select(i => (long)i));
    }

    [Fact]
    public void Counters_TrackFramesRenderTimeAndDroppedFrames()
    {
        var counters = new DiagnosticsCounters();
        counters.Read().ShouldBe(new DiagnosticsCountersSnapshot(0, 0, 0, 0, 0));

        var ms = Stopwatch.Frequency / 1000;
        counters.RecordFrame(4 * ms);
        counters.RecordFrame(10 * ms);
        counters.RecordFrame(1 * ms);
        counters.RecordDroppedFrame();

        var snapshot = counters.Read();
        snapshot.Frames.ShouldBe(3);
        snapshot.DroppedFrames.ShouldBe(1);
        snapshot.LastRenderMs.ShouldBe(1, tolerance: 0.01);
        snapshot.AverageRenderMs.ShouldBe(5, tolerance: 0.01);
        snapshot.MaxRenderMs.ShouldBe(10, tolerance: 0.01);
    }

    [Fact]
    public void PublishFrame_SwapsTreeAndLayoutTogether()
    {
        var state = new DiagnosticsState();
        state.LatestFrame.ShouldBeNull();

        var root = VNode.CreateElement("div");
        VNodeLayoutInfo[] layouts = [new(root.ID, 0, 0, null, null, 10, 1, 0)];
        state.PublishFrame(root, layouts, null);
        var first = state.LatestFrame;

        state.PublishFrame(null, null, null);
        var second = state.LatestFrame;

        first.ShouldNotBeNull();
        first.Sequence.ShouldBe(1);
        first.Root.ShouldBeSameAs(root);
        first.Layouts.ShouldBeSameAs(layouts);
        second.ShouldNotBeNull();
        second.Sequence.ShouldBe(2);
        second.Root.ShouldBeNull();
    }

    [Fact]
    public void RecordError_KeepsTheLast50()
    {
        var state = new DiagnosticsState();

        for (var i = 0; i < DiagnosticsState.ErrorCapacity + 5; i++)
        {
            state.RecordError(new InvalidOperationException($"boom {i}"), "render");
        }

        var errors = state.Errors.Read();
        errors.Length.ShouldBe(DiagnosticsState.ErrorCapacity);
        errors[^1].Value.Source.ShouldBe("render");
        errors[^1].Value.Type.ShouldBe(typeof(InvalidOperationException).FullName);
        errors[^1].Value.Message.ShouldBe($"boom {DiagnosticsState.ErrorCapacity + 4}");
        errors[^1].Value.Details.ShouldContain("boom");
    }

    [Fact]
    public void RecordError_RecordsTheSameExceptionOnce()
    {
        var state = new DiagnosticsState();
        var exception = new InvalidOperationException("once");

        state.RecordError(exception, "render");
        state.RecordError(exception, "render");
        state.RecordError(new InvalidOperationException("once"), "render");

        state.Errors.Read().Length.ShouldBe(2);
    }

    [Fact]
    public void LoggerProvider_KeepsDebugAndAboveWithExceptions()
    {
        var state = new DiagnosticsState();
        using var provider = new DiagnosticsLoggerProvider(state);
        var logger = provider.CreateLogger("Test.Category");

        logger.IsEnabled(LogLevel.Trace).ShouldBeFalse();
        logger.IsEnabled(LogLevel.Debug).ShouldBeTrue();

        logger.LogTrace("hidden");
        logger.LogDebug("debug {Value}", 1);
        logger.LogError(new EventId(7), new InvalidOperationException("bad"), "failed");

        var logs = state.Logs.Read();
        logs.Length.ShouldBe(2);
        logs[0].Value.Level.ShouldBe(LogLevel.Debug);
        logs[0].Value.Message.ShouldBe("debug 1");
        logs[0].Value.Category.ShouldBe("Test.Category");
        logs[1].Value.EventId.ShouldBe(7);
        logs[1].Value.Exception.ShouldNotBeNull().ShouldContain("bad");
    }

    [Fact]
    public void LoggerProvider_WithoutStateReturnsANoOpLogger()
    {
        using var provider = new DiagnosticsLoggerProvider(null);

        provider.CreateLogger("x").ShouldBeSameAs(NullLogger.Instance);
    }
}
