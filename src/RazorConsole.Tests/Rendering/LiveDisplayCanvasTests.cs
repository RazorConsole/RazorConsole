// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Tests.Rendering;

public sealed class LiveDisplayCanvasTests
{
    [Fact]
    public void UpdateTarget_WithMouseEvents_UsesAlternateScreenBuffer()
    {
        var captured = new List<IRenderable>();
        var ansiConsole = CreateConsole(captured);
        using var canvas = new LiveDisplayCanvas(
            new ConsoleLiveDisplayOptions { EnableMouseEvents = true },
            ansiConsole);

        canvas.UpdateTarget(new TextRenderable("hello"));
        canvas.Dispose();

        captured.Count.ShouldBe(5);
        GetControlCode(captured[0]).ShouldBe(SM(DECALTSCR));
        GetControlCode(captured[1]).ShouldBe("\u001b[?1003h\u001b[?1006h");
        captured[2].ShouldBeOfType<RazorConsole.Core.Renderables.DiffRenderable>();
        GetControlCode(captured[3]).ShouldBe("\u001b[?1003l\u001b[?1006l");
        GetControlCode(captured[4]).ShouldBe(RM(DECALTSCR));
    }

    [Fact]
    public void UpdateTarget_WithoutMouseEvents_DoesNotUseAlternateScreenBuffer()
    {
        var captured = new List<IRenderable>();
        var ansiConsole = CreateConsole(captured);
        using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, ansiConsole);

        canvas.UpdateTarget(new TextRenderable("hello"));

        captured.Count.ShouldBe(1);
        captured[0].ShouldBeOfType<RazorConsole.Core.Renderables.DiffRenderable>();
    }

    [Fact]
    public void SynchronizedUpdates_WrapEachUpdateAndRefresh()
    {
        var captured = new List<IRenderable>();
        var console = CreateConsole(captured);
        using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions
        {
            UseAlternateScreenBuffer = false,
            EnableSynchronizedUpdates = true,
        }, console);

        canvas.UpdateTarget(new TextRenderable("first"));
        canvas.UpdateTarget(new TextRenderable("second"));
        canvas.Refresh();

        captured.Count.ShouldBe(9);
        for (var index = 0; index < captured.Count; index += 3)
        {
            GetControlCode(captured[index]).ShouldBe("\u001b[?2026h");
            captured[index + 1].ShouldBeOfType<RazorConsole.Core.Renderables.DiffRenderable>();
            GetControlCode(captured[index + 2]).ShouldBe("\u001b[?2026l");
        }
    }

    [Fact]
    public void SynchronizedUpdates_EndFrameAndInvalidateWhenRenderingFails()
    {
        var captured = new List<IRenderable>();
        var console = CreateConsole(captured);
        var fail = true;
        console.When(static console => console.Write(Arg.Any<RazorConsole.Core.Renderables.DiffRenderable>()))
            .Do(call =>
            {
                if (fail)
                {
                    throw new IOException("Frame write failed.");
                }
            });
        using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions
        {
            UseAlternateScreenBuffer = false,
            EnableSynchronizedUpdates = true,
        }, console);

        Should.Throw<IOException>(() => canvas.UpdateTarget(new TextRenderable("hello")));
        GetControlCode(captured[^1]).ShouldBe("\u001b[?2026l");

        fail = false;
        canvas.Refresh();
        GetControlCode(captured[^3]).ShouldBe("\u001b[?2026h");
        GetControlCode(captured[^1]).ShouldBe("\u001b[?2026l");
        var output = string.Concat(captured[^2].Render(CreateRenderOptions(), 80)
            .Select(segment => segment.Text));
        output.ShouldContain(ED(2));
        output.ShouldContain("hello");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void MovingWideShape_AllRowsStayInsideOneSynchronizedFrame(int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions
        {
            UseAlternateScreenBuffer = false,
            EnableSynchronizedUpdates = true,
        }, console);
        for (var step = 0; step < 3; step++)
        {
            writer.GetStringBuilder().Clear();
            var row = new string(' ', step) + new string('#', 8);
            canvas.UpdateTarget(new Rows(Enumerable.Range(0, width).Select(_ => new Text(row))));
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
    public async Task Refresh_DuringWrite_ReturnsImmediatelyAndWritesOneMoreFrame()
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
                releaseWrite.Wait(TimeSpan.FromSeconds(10));
            });
        using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, console);
        canvas.UpdateTarget(new TextRenderable("hello"));
        writes.ShouldBe(1);

        writeStarted.Reset();
        releaseWrite.Reset();
        var blockedRefresh = Task.Run(canvas.Refresh, TestContext.Current.CancellationToken);
        writeStarted.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue();

        for (var i = 0; i < 20; i++)
        {
            canvas.Refresh();
        }

        writes.ShouldBe(2);
        releaseWrite.Set();
        await blockedRefresh.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        writes.ShouldBe(3);
    }

    [Fact]
    public async Task UpdateTarget_DropsErrorsFromRefreshesQueuedByOtherCallers()
    {
        var writes = 0;
        using var secondWriteStarted = new ManualResetEventSlim();
        using var releaseSecondWrite = new ManualResetEventSlim();
        var console = Substitute.For<IAnsiConsole>();
        console.When(static console => console.Write(Arg.Any<IRenderable>()))
            .Do(_ =>
            {
                switch (Interlocked.Increment(ref writes))
                {
                    case 2:
                        secondWriteStarted.Set();
                        releaseSecondWrite.Wait(TimeSpan.FromSeconds(10));
                        break;
                    case 3:
                        throw new IOException("Background repaint failed.");
                }
            });
        using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, console);
        canvas.UpdateTarget(new TextRenderable("first"));

        var update = Task.Run(() => canvas.UpdateTarget(new TextRenderable("second")), TestContext.Current.CancellationToken);
        secondWriteStarted.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue();
        canvas.Refresh();
        releaseSecondWrite.Set();

        await update.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        writes.ShouldBe(3);
    }

    [Fact]
    public void Refresh_ThrowsErrorFromItsOwnWrite()
    {
        var fail = false;
        var console = Substitute.For<IAnsiConsole>();
        console.When(static console => console.Write(Arg.Any<IRenderable>()))
            .Do(_ =>
            {
                if (fail)
                {
                    throw new IOException("Frame write failed.");
                }
            });
        using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, console);
        canvas.UpdateTarget(new TextRenderable("hello"));

        fail = true;

        Should.Throw<IOException>(canvas.Refresh);
    }

    private static IAnsiConsole CreateConsole(List<IRenderable> captured)
    {
        var console = Substitute.For<IAnsiConsole>();
        console.When(static console => console.Write(Arg.Any<IRenderable>()))
            .Do(call => captured.Add(call.Arg<IRenderable>()));
        return console;
    }

    private static string GetControlCode(IRenderable renderable)
    {
        renderable.ShouldBeOfType<ControlCode>();
        return string.Concat(renderable.Render(CreateRenderOptions(), 80).Select(static segment => segment.Text));
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
