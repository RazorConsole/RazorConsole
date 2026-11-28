// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Tests.Rendering;

public sealed class AnsiTextTerminalTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Frames_AreTheTextAnAnsiTerminalWritesToAnEquivalentConsole()
    {
        var options = new ConsoleLiveDisplayOptions { EnableMouseEvents = true };
        var texts = new List<string>();
        await using var terminal = new AnsiTextTerminal(options, 20, 4, texts.Add);

        // What the browser host did before: an AnsiTerminal writing to a console over a string buffer.
        var expected = new StringBuilder();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(new StringWriter(expected)),
        });
        console.Profile.Capabilities.Unicode = true;
        console.Profile.Width = 20;
        console.Profile.Height = 4;
        var reference = new AnsiTerminal(options, console);

        foreach (var text in new[] { "hello", "hello", "world" })
        {
            var frame = Frame(text);
            await terminal.PresentAsync(frame, Token);
            await reference.PresentAsync(frame, Token);
        }

        texts.Count.ShouldBe(3);
        texts[0].ShouldStartWith(SM(DECALTSCR));
        texts[0].ShouldContain("hello");
        texts[2].ShouldContain("world");
        string.Concat(texts).ShouldBe(expected.ToString());
    }

    [Fact]
    public async Task TextTheCallbackFailedToTake_GoesOutBeforeTheNextFrame()
    {
        var texts = new List<string>();
        var fail = true;
        await using var terminal = new AnsiTextTerminal(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, 20, 4, text =>
        {
            if (fail)
            {
                throw new IOException("The page is busy.");
            }

            texts.Add(text);
        });

        await Should.ThrowAsync<IOException>(terminal.PresentAsync(Frame("first"), Token).AsTask());
        fail = false;
        await terminal.PresentAsync(Frame("second"), Token);

        texts.Count.ShouldBe(1);
        texts[0].IndexOf("first", StringComparison.Ordinal).ShouldBeLessThan(texts[0].IndexOf("second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resize_RedrawsTheNextFrameInFull()
    {
        var texts = new List<string>();
        await using var terminal = new AnsiTextTerminal(new ConsoleLiveDisplayOptions { UseAlternateScreenBuffer = false }, 20, 4, texts.Add);

        await terminal.PresentAsync(Frame("hello"), Token);
        await terminal.PresentAsync(Frame("hello"), Token);
        terminal.Resize(30, 6);
        await terminal.PresentAsync(Frame("hello"), Token);

        texts[0].ShouldContain(ED(2));
        texts[1].ShouldNotContain(ED(2));
        texts[2].ShouldContain(ED(2));
    }

    [Fact]
    public async Task Dispose_DoesNotSendTheTextThatLeavesTheAlternateScreen()
    {
        var texts = new List<string>();
        var terminal = new AnsiTextTerminal(new ConsoleLiveDisplayOptions { EnableMouseEvents = true }, 20, 4, texts.Add);

        await terminal.PresentAsync(Frame("hello"), Token);
        await terminal.DisposeAsync();

        texts.Count.ShouldBe(1);
        texts[0].ShouldNotContain(RM(DECALTSCR));
    }

    private static TerminalFrame Frame(string text)
    {
        var source = TestFrames.Of(new Text(text));
        return new TerminalFrame(1, source, source.Paint());
    }
}
