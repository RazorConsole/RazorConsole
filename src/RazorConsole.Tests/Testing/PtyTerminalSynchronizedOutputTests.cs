// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Concurrent;
using System.Text;
using RazorConsole.Testing;
using RazorConsole.Testing.Pty;

namespace RazorConsole.Tests.Testing;

/// <summary>
/// Frame capture around synchronized updates (<c>CSI ? 2026 h</c> … <c>l</c>): an update left open is captured when
/// the output ends, and ended after <see cref="PtyTerminal.SynchronizedUpdateTimeout"/>. The terminal reads from a
/// fake connection and measures time with a manual clock, so no real terminal can swallow or reorder the sequences.
/// </summary>
public sealed class PtyTerminalSynchronizedOutputTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public void SynchronizedUpdateTimeout_IsOneSecond()
    {
        PtyTerminal.SynchronizedUpdateTimeout.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ClosedSynchronizedUpdate_IsCapturedWhenItEnds()
    {
        var clock = new ManualTimeProvider();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, clock);

        await OutputAsync(terminal, connection, "\u001b[?2026hab");
        terminal.CurrentFrame.ContainsText("ab").ShouldBeFalse(terminal.DumpDiagnostics());

        await OutputAsync(terminal, connection, "c\u001b[?2026l");
        terminal.CurrentFrame.ContainsText("abc").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenSynchronizedUpdate_IsCapturedWhenTheOutputEnds(bool processExits)
    {
        var clock = new ManualTimeProvider();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, clock);
        await OutputAsync(terminal, connection, "before");
        terminal.CurrentFrame.ContainsText("before").ShouldBeTrue(terminal.DumpDiagnostics());

        await OutputAsync(terminal, connection, "\u001b[?2026h\u001b[2J\u001b[Hafter");
        terminal.CurrentFrame.ContainsText("before").ShouldBeTrue(terminal.DumpDiagnostics());
        if (processExits)
        {
            connection.Exit(3);
        }
        else
        {
            connection.CloseOutput();
        }

        var frame = await terminal.WaitUntilAsync(f => f.ContainsText("after"), WaitLimit, "final frame", TestContext.Current.CancellationToken);
        frame.ContainsText("before").ShouldBeFalse(terminal.DumpDiagnostics());
        frame.GetLine(0).TrimEnd().ShouldBe("after");
    }

    [Fact]
    public async Task OpenSynchronizedUpdate_WithNoMoreOutput_IsEndedAndCapturedAfterOneSecond()
    {
        var clock = new ManualTimeProvider();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, clock);

        await OutputAsync(terminal, connection, "\u001b[?2026hpartial");
        clock.Advance(TimeSpan.FromMilliseconds(999));
        terminal.CurrentFrame.ContainsText("partial").ShouldBeFalse(terminal.DumpDiagnostics());

        clock.Advance(TimeSpan.FromMilliseconds(1));
        terminal.CurrentFrame.GetLine(0).TrimEnd().ShouldBe("partial");

        // The update is over: later output is captured at once.
        await OutputAsync(terminal, connection, "+more");
        terminal.CurrentFrame.GetLine(0).TrimEnd().ShouldBe("partial+more");
    }

    [Fact]
    public async Task OpenSynchronizedUpdate_StillWritingAfterOneSecond_IsCapturedWithTheNextOutput()
    {
        // The clock jumps without firing the timer, as if its callback were late.
        var clock = new ManualTimeProvider();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, clock);

        await OutputAsync(terminal, connection, "\u001b[?2026ha");
        clock.Skip(TimeSpan.FromMilliseconds(999));
        await OutputAsync(terminal, connection, "b");
        terminal.CurrentFrame.ContainsText("ab").ShouldBeFalse(terminal.DumpDiagnostics());

        clock.Skip(TimeSpan.FromMilliseconds(1));
        await OutputAsync(terminal, connection, "c");
        terminal.CurrentFrame.GetLine(0).TrimEnd().ShouldBe("abc");
    }

    [Fact]
    public async Task SynchronizedUpdateTimeout_CountsFromTheStartOfEachUpdate()
    {
        var clock = new ManualTimeProvider();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, clock);

        await OutputAsync(terminal, connection, "\u001b[?2026ha\u001b[?2026l");
        terminal.CurrentFrame.GetLine(0).TrimEnd().ShouldBe("a");
        clock.Advance(TimeSpan.FromMilliseconds(900));

        await OutputAsync(terminal, connection, "\u001b[?2026hb");
        clock.Advance(TimeSpan.FromMilliseconds(200));
        terminal.CurrentFrame.GetLine(0).TrimEnd().ShouldBe("a");

        clock.Advance(TimeSpan.FromMilliseconds(800));
        terminal.CurrentFrame.GetLine(0).TrimEnd().ShouldBe("ab");
    }

    [Fact]
    public async Task SynchronizedUpdatesInOneChunk_ArentTakenForOneLongUpdate()
    {
        var clock = new ManualTimeProvider();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, clock);

        await OutputAsync(terminal, connection, "\u001b[?2026ha");
        clock.Skip(TimeSpan.FromMilliseconds(900));

        // The first update ends and a second starts within one read: the second gets its own full second.
        await OutputAsync(terminal, connection, "\u001b[?2026l\u001b[?2026hb");
        clock.Skip(TimeSpan.FromMilliseconds(200));
        await OutputAsync(terminal, connection, "c");
        terminal.CurrentFrame.ContainsText("a").ShouldBeFalse(terminal.DumpDiagnostics());

        clock.Skip(TimeSpan.FromMilliseconds(800));
        await OutputAsync(terminal, connection, "d");
        terminal.CurrentFrame.GetLine(0).TrimEnd().ShouldBe("abcd");
    }

    [Fact]
    public async Task OutputEndedBeforeTheExitIsKnown_ReportsTheExitStatus()
    {
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, new ManualTimeProvider());
        await OutputAsync(terminal, connection, "done");
        connection.CloseOutput();
        _ = Task.Delay(300, TestContext.Current.CancellationToken).ContinueWith(_ => connection.Exit(0), TaskScheduler.Default);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            terminal.WaitUntilAsync(f => f.ContainsText("never shown"), WaitLimit, "never shown", TestContext.Current.CancellationToken));
        exception.Message.ShouldContain("exited with code 0");
    }

    [Fact]
    public async Task OutputEndedWhileTheProcessRuns_FailsWithinTheExitStatusWait()
    {
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, new ManualTimeProvider());
        connection.CloseOutput();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            terminal.WaitUntilAsync(f => f.ContainsText("never shown"), WaitLimit, "never shown", TestContext.Current.CancellationToken));
        exception.Message.ShouldContain("Process 1: running");
        stopwatch.Elapsed.ShouldBeLessThan(WaitLimit);
    }

    private static PtyTerminal Start(FakePtyConnection connection, TimeProvider clock)
        => PtyTerminal.Start(connection, new PtyTerminalOptions { FileName = "fake", Width = 20, Height = 3 }, clock);

    // Waits until the reader has handled the output; the frame decision is made under the same lock as RawOutput.
    private static async Task OutputAsync(PtyTerminal terminal, FakePtyConnection connection, string text)
    {
        var expected = terminal.RawOutput + text;
        connection.Output(text);
        var limit = DateTime.UtcNow + WaitLimit;
        while (terminal.RawOutput != expected)
        {
            DateTime.UtcNow.ShouldBeLessThan(limit, terminal.DumpDiagnostics());
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private sealed class FakePtyConnection : PtyConnection, IAsyncDisposable
    {
        private readonly BlockingCollection<byte[]> _output = [];

        public override int ProcessId => 1;

        public void Output(string text) => _output.Add(Encoding.UTF8.GetBytes(text));

        public void CloseOutput() => _output.CompleteAdding();

        public void Exit(int exitCode) => SetExited(exitCode);

        public override int Read(Span<byte> buffer)
        {
            if (!_output.TryTake(out var data, Timeout.Infinite))
            {
                return 0;
            }

            data.CopyTo(buffer);
            return data.Length;
        }

        public override void Write(ReadOnlySpan<byte> data)
        {
        }

        public override void Resize(int width, int height)
        {
        }

        public override void Kill() => SetExited(137);

        public override void StopReading() => _output.CompleteAdding();

        public override void Dispose(bool readerStopped)
        {
        }

        // The terminal is disposed first and stops the reader; the collection can then be released.
        public ValueTask DisposeAsync()
        {
            _output.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _now);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_sync)
            {
                _timers.Add(timer);
            }

            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>
        /// Moves the clock forward and runs the timers that are due.
        /// </summary>
        public void Advance(TimeSpan time)
        {
            Skip(time);
            ManualTimer[] due;
            lock (_sync)
            {
                due = [.. _timers.Where(timer => timer.DueAt is { } at && at <= _now)];
                foreach (var timer in due)
                {
                    timer.DueAt = null;
                }
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        /// <summary>
        /// Moves the clock forward without running timers.
        /// </summary>
        public void Skip(TimeSpan time) => Interlocked.Add(ref _now, time.Ticks);

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public long? DueAt { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                // Periodic timers are not needed here.
                lock (owner._sync)
                {
                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetTimestamp() + dueTime.Ticks;
                }

                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (owner._sync)
                {
                    DueAt = null;
                    owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
