// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Logging;
using RazorConsole.Core;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Tests.Rendering;

public sealed class LiveDisplayCanvasTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpdateTarget_WithMouseEvents_UsesAlternateScreenBuffer()
    {
        var captured = new List<IRenderable>();
        var ansiConsole = CreateConsole(captured);
        await using var canvas = new LiveDisplayCanvas(
            new ConsoleLiveDisplayOptions { EnableMouseEvents = true },
            ansiConsole);

        canvas.UpdateTarget(new TextRenderable("hello"));
        await canvas.WhenIdleAsync(Token);
        await canvas.DisposeAsync();

        captured.Count.ShouldBe(5);
        GetControlCode(captured[0]).ShouldBe(SM(DECALTSCR));
        GetControlCode(captured[1]).ShouldBe("\u001b[?1003h\u001b[?1006h");
        captured[2].ShouldBeOfType<DiffRenderable>();
        GetControlCode(captured[3]).ShouldBe("\u001b[?1003l\u001b[?1006l");
        GetControlCode(captured[4]).ShouldBe(RM(DECALTSCR));
    }

    [Fact]
    public async Task UpdateTarget_WithoutMouseEvents_DoesNotUseAlternateScreenBuffer()
    {
        var captured = new List<IRenderable>();
        var ansiConsole = CreateConsole(captured);
        await using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, ansiConsole);

        canvas.UpdateTarget(new TextRenderable("hello"));
        await canvas.WhenIdleAsync(Token);

        captured.Count.ShouldBe(1);
        captured[0].ShouldBeOfType<DiffRenderable>();
    }

    [Fact]
    public async Task SynchronizedUpdates_WrapEachUpdateAndRefresh()
    {
        var captured = new List<IRenderable>();
        var console = CreateConsole(captured);
        await using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions
        {
            UseAlternateScreenBuffer = false,
            EnableSynchronizedUpdates = true,
        }, console);

        canvas.UpdateTarget(new TextRenderable("first"));
        await canvas.WhenIdleAsync(Token);
        canvas.UpdateTarget(new TextRenderable("second"));
        await canvas.WhenIdleAsync(Token);
        canvas.Refresh();
        await canvas.WhenIdleAsync(Token);

        captured.Count.ShouldBe(9);
        for (var index = 0; index < captured.Count; index += 3)
        {
            GetControlCode(captured[index]).ShouldBe("\u001b[?2026h");
            captured[index + 1].ShouldBeOfType<DiffRenderable>();
            GetControlCode(captured[index + 2]).ShouldBe("\u001b[?2026l");
        }
    }

    [Fact]
    public async Task SynchronizedUpdates_EndFrameAndInvalidateWhenRenderingFails()
    {
        var captured = new List<IRenderable>();
        var console = CreateConsole(captured);
        var fail = true;
        console.When(static console => console.Write(Arg.Any<DiffRenderable>()))
            .Do(call =>
            {
                if (fail)
                {
                    throw new IOException("Frame write failed.");
                }
            });
        await using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions
        {
            UseAlternateScreenBuffer = false,
            EnableSynchronizedUpdates = true,
        }, console);

        Should.NotThrow(() => canvas.UpdateTarget(new TextRenderable("hello")));
        await canvas.WhenIdleAsync(Token);
        GetControlCode(captured[^1]).ShouldBe("\u001b[?2026l");

        fail = false;
        canvas.Refresh();
        await canvas.WhenIdleAsync(Token);
        GetControlCode(captured[^3]).ShouldBe("\u001b[?2026h");
        GetControlCode(captured[^1]).ShouldBe("\u001b[?2026l");
        var output = RenderText(captured[^2]);
        output.ShouldContain(ED(2));
        output.ShouldContain("hello");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task MovingWideShape_AllRowsStayInsideOneSynchronizedFrame(int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        await using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions
        {
            UseAlternateScreenBuffer = false,
            EnableSynchronizedUpdates = true,
        }, console);
        for (var step = 0; step < 3; step++)
        {
            writer.GetStringBuilder().Clear();
            var row = new string(' ', step) + new string('#', 8);
            canvas.UpdateTarget(new Rows(Enumerable.Range(0, width).Select(_ => new Text(row))));
            await canvas.WhenIdleAsync(Token);
            var output = writer.ToString();
            output.ShouldStartWith("\u001b[?2026h");
            output.ShouldEndWith("\u001b[?2026l");
            output.Split("\u001b[?2026h").Length.ShouldBe(2);
            output.Split("\u001b[?2026l").Length.ShouldBe(2);
            for (var y = 0; y < width; y++)
            {
                output.ShouldContain(CUP(y + 1, 1));
            }
        }
    }

    [Fact]
    public void SynchronizedUpdates_ClonePreservesOptInAndDefaultIsOff()
    {
        ConsoleLiveDisplayOptions.Default.EnableSynchronizedUpdates.ShouldBeFalse();
        var options = new ConsoleLiveDisplayOptions { EnableSynchronizedUpdates = true };
        options.Clone().EnableSynchronizedUpdates.ShouldBeTrue();
    }

    [Fact]
    public async Task Requests_DuringWrite_ReturnImmediatelyAndMergeIntoOneFrame()
    {
        var writes = 0;
        using var writeStarted = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim(true);
        var console = Substitute.For<IAnsiConsole>();
        console.When(static console => console.Write(Arg.Any<IRenderable>()))
            .Do(_ =>
            {
                Interlocked.Increment(ref writes);
                writeStarted.Set();
                releaseWrite.Wait(Timeout);
            });
        await using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, console);
        canvas.UpdateTarget(new TextRenderable("hello"));
        await canvas.WhenIdleAsync(Token);
        writes.ShouldBe(1);

        writeStarted.Reset();
        releaseWrite.Reset();
        canvas.Refresh();
        writeStarted.Wait(Timeout, Token).ShouldBeTrue();

        // The frame loop is blocked in a write: every request returns at once, and they all fold into one frame.
        for (var i = 0; i < 20; i++)
        {
            canvas.Refresh();
            canvas.UpdateTarget(new TextRenderable($"update {i}"));
        }

        writes.ShouldBe(2);
        releaseWrite.Set();
        await canvas.WhenIdleAsync(Token).WaitAsync(Timeout, Token);

        writes.ShouldBe(3);
    }

    [Fact]
    public async Task UpdateTarget_LatestTargetWins()
    {
        var captured = new List<IRenderable>();
        using var writeStarted = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim();
        var console = Substitute.For<IAnsiConsole>();
        console.When(static console => console.Write(Arg.Any<IRenderable>()))
            .Do(call =>
            {
                // Render now: the canvas reuses one DiffRenderable for every frame.
                lock (captured)
                {
                    captured.Add(call.Arg<IRenderable>());
                }

                writeStarted.Set();
                releaseWrite.Wait(Timeout);
            });
        await using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, console);

        canvas.UpdateTarget(new TextRenderable("first"));
        writeStarted.Wait(Timeout, Token).ShouldBeTrue();
        canvas.UpdateTarget(new TextRenderable("second"));
        canvas.UpdateTarget(new TextRenderable("third"));
        releaseWrite.Set();
        await canvas.WhenIdleAsync(Token).WaitAsync(Timeout, Token);

        captured.Count.ShouldBe(2);
        var output = RenderText(captured[^1]);
        output.ShouldContain("third");
        output.ShouldNotContain("second");
    }

    [Fact]
    public async Task Refresh_BeforeAnyTarget_WritesNothing()
    {
        var captured = new List<IRenderable>();
        await using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { EnableMouseEvents = true }, CreateConsole(captured));

        canvas.Refresh();
        canvas.UpdateTarget(null);
        await canvas.WhenIdleAsync(Token);

        captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WriteErrors_NeverReachCallers_AndAreLoggedOncePerRunOfFailures()
    {
        var fail = true;
        var console = Substitute.For<IAnsiConsole>();
        console.When(static console => console.Write(Arg.Any<IRenderable>()))
            .Do(_ =>
            {
                if (fail)
                {
                    throw new IOException("Frame write failed.");
                }
            });
        var logger = new RecordingLogger();
        await using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, console, logger);

        Should.NotThrow(() => canvas.UpdateTarget(new TextRenderable("hello")));
        await canvas.WhenIdleAsync(Token);
        for (var i = 0; i < 5; i++)
        {
            Should.NotThrow(canvas.Refresh);
            await canvas.WhenIdleAsync(Token);
        }

        logger.Entries.Count(static entry => entry.Level == LogLevel.Error).ShouldBe(1);
        logger.Entries.Single(static entry => entry.Level == LogLevel.Error).Exception.ShouldBeOfType<IOException>();

        fail = false;
        canvas.Refresh();
        await canvas.WhenIdleAsync(Token);
        logger.Entries[^1].Level.ShouldBe(LogLevel.Information);

        fail = true;
        canvas.Refresh();
        await canvas.WhenIdleAsync(Token);
        logger.Entries.Count(static entry => entry.Level == LogLevel.Error).ShouldBe(2);
    }

    [Fact]
    public async Task Refresh_AfterWriteError_RedrawsEverything()
    {
        var captured = new List<IRenderable>();
        var fail = false;
        var console = Substitute.For<IAnsiConsole>();
        console.When(static console => console.Write(Arg.Any<IRenderable>()))
            .Do(call =>
            {
                if (fail)
                {
                    throw new IOException("Frame write failed.");
                }

                captured.Add(call.Arg<IRenderable>());
            });
        await using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, console);
        canvas.UpdateTarget(new TextRenderable("hello"));
        await canvas.WhenIdleAsync(Token);

        fail = true;
        Should.NotThrow(canvas.Refresh);
        await canvas.WhenIdleAsync(Token);

        fail = false;
        canvas.Refresh();
        await canvas.WhenIdleAsync(Token);
        var output = RenderText(captured[^1]);
        output.ShouldContain(ED(2));
        output.ShouldContain("hello");
    }

    [Fact]
    public async Task Dispose_WaitsForFrameInProgress_BeforeLeavingAlternateScreen()
    {
        var captured = new List<IRenderable>();
        using var writeStarted = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim(true);
        var console = Substitute.For<IAnsiConsole>();
        console.When(static console => console.Write(Arg.Any<IRenderable>()))
            .Do(call =>
            {
                var renderable = call.Arg<IRenderable>();
                lock (captured)
                {
                    captured.Add(renderable);
                }

                if (renderable is DiffRenderable)
                {
                    writeStarted.Set();
                    releaseWrite.Wait(Timeout);
                }
            });
        var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { EnableMouseEvents = true }, console);
        canvas.UpdateTarget(new TextRenderable("first"));
        await canvas.WhenIdleAsync(Token);

        writeStarted.Reset();
        releaseWrite.Reset();
        canvas.Refresh();
        writeStarted.Wait(Timeout, Token).ShouldBeTrue();
        canvas.Refresh();

        var dispose = canvas.DisposeAsync().AsTask();
        await Task.Delay(50, Token);
        dispose.IsCompleted.ShouldBeFalse();

        releaseWrite.Set();
        await dispose.WaitAsync(Timeout, Token);

        // The frame in progress finished, the queued refresh was dropped, and the terminal was restored last.
        captured.Count(static item => item is DiffRenderable).ShouldBe(2);
        GetControlCode(captured[^2]).ShouldBe("\u001b[?1003l\u001b[?1006l");
        GetControlCode(captured[^1]).ShouldBe(RM(DECALTSCR));
    }

    [Fact]
    public async Task Requests_AfterDispose_AreIgnored()
    {
        var captured = new List<IRenderable>();
        var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, CreateConsole(captured));
        canvas.UpdateTarget(new TextRenderable("hello"));
        await canvas.WhenIdleAsync(Token);
        await canvas.DisposeAsync();
        var count = captured.Count;

        Should.NotThrow(() => canvas.UpdateTarget(new TextRenderable("late")));
        Should.NotThrow(canvas.Refresh);
        await canvas.WhenIdleAsync(Token).WaitAsync(Timeout, Token);

        captured.Count.ShouldBe(count);
    }

    private static IAnsiConsole CreateConsole(List<IRenderable> captured)
    {
        var console = Substitute.For<IAnsiConsole>();
        console.When(static console => console.Write(Arg.Any<IRenderable>()))
            .Do(call => captured.Add(call.Arg<IRenderable>()));
        return console;
    }

    private static string RenderText(IRenderable renderable)
        => string.Concat(renderable.Render(CreateRenderOptions(), 80).Select(static segment => segment.Text));

    private static string GetControlCode(IRenderable renderable)
    {
        renderable.ShouldBeOfType<ControlCode>();
        return RenderText(renderable);
    }

    private static RenderOptions CreateRenderOptions()
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        return new RenderOptions(console.Profile.Capabilities, new Size(80, 25));
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<(LogLevel Level, Exception? Exception)> _entries = [];

        public IReadOnlyList<(LogLevel Level, Exception? Exception)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, exception));
            }
        }
    }

    private sealed class TextRenderable(string text) : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth)
            => new(text.Length, text.Length);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            yield return new Segment(text);
        }
    }
}
