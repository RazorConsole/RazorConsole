// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Testing.Vt;
using Spectre.Console;

namespace RazorConsole.Tests.Rendering;

public sealed class AnsiFrameEncoderTests
{
    private static readonly AnsiOutputCapabilities TrueColorWithLinks = new(ColorSystem.TrueColor, Links: true);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryFrame_ShowsExactlyItsCells_OnAnEmulatedTerminal(int seed)
    {
        const int Width = 24;
        const int Height = 8;
        var random = new Random(seed);
        var encoder = new AnsiFrameEncoder(hideCursor: true);
        var terminal = new VtEmulator(Width, Height);
        var canvas = RandomCanvas(random, Width, Height);

        for (var step = 0; step < 40; step++)
        {
            Mutate(random, canvas);
            var frame = Frame(canvas, step);
            terminal.Feed(encoder.Encode(frame, Width, Height, TrueColorWithLinks));
            ShouldShow(terminal, frame, $"seed {seed}, step {step}");

            if (step == 20)
            {
                // A write that may not have reached the terminal: the next frame is drawn in full.
                encoder.Invalidate();
                terminal.Feed("garbage");
            }
        }
    }

    [Fact]
    public void AShorterFrame_ClearsTheRowsItNoLongerHas()
    {
        var encoder = new AnsiFrameEncoder(hideCursor: true);
        var terminal = new VtEmulator(10, 4);
        var tall = new TerminalCanvas(10, 4);
        for (var y = 0; y < 4; y++)
        {
            tall.Write(0, y, "row " + y);
        }

        terminal.Feed(encoder.Encode(Frame(tall, 1), 10, 4, TrueColorWithLinks));
        var shorter = new TerminalCanvas(10, 2);
        shorter.Write(0, 0, "row 0");
        shorter.Write(0, 1, "row 1");
        terminal.Feed(encoder.Encode(Frame(shorter, 2), 10, 4, TrueColorWithLinks));

        var screen = terminal.CreateFrame(1, DateTimeOffset.UtcNow);
        screen.ScreenText.ShouldNotContain("row 2");
        screen.ScreenText.ShouldNotContain("row 3");
        screen.ScreenText.ShouldContain("row 1");
    }

    [Fact]
    public void ATerminalResize_RedrawsInFull()
    {
        var encoder = new AnsiFrameEncoder(hideCursor: true);
        var canvas = new TerminalCanvas(10, 3);
        canvas.Write(0, 0, "hello");

        encoder.Encode(Frame(canvas, 1), 10, 3, TrueColorWithLinks).ShouldContain("\u001b[2J");
        encoder.Encode(Frame(canvas, 2), 10, 3, TrueColorWithLinks).ShouldNotContain("\u001b[2J");
        var resized = encoder.Encode(Frame(canvas, 3), 12, 3, TrueColorWithLinks);
        resized.ShouldContain("\u001b[2J");
        resized.ShouldContain("hello");
    }

    [Fact]
    public void AnUnchangedFrame_WritesNoCells()
    {
        var encoder = new AnsiFrameEncoder(hideCursor: true);
        var canvas = new TerminalCanvas(10, 3);
        canvas.Write(0, 1, "hello", new Style(Color.Red));
        encoder.Encode(Frame(canvas, 1), 10, 3, TrueColorWithLinks);

        var again = encoder.Encode(Frame(canvas, 2), 10, 3, TrueColorWithLinks);

        again.ShouldNotContain("hello");
        again.ShouldNotContain("\u001b[0;");
    }

    [Fact]
    public void OnlyTheChangedSpanOfARow_IsWritten()
    {
        var encoder = new AnsiFrameEncoder(hideCursor: true);
        var before = new TerminalCanvas(20, 1);
        before.Write(0, 0, "count: 9 items");
        encoder.Encode(Frame(before, 1), 20, 1, TrueColorWithLinks);
        var after = new TerminalCanvas(20, 1);
        after.Write(0, 0, "count: 10 items");

        var text = encoder.Encode(Frame(after, 2), 20, 1, TrueColorWithLinks);

        text.ShouldContain("\u001b[1;8H10 items");
        text.ShouldNotContain("count");
    }

    [Theory]
    [InlineData(ColorSystem.TrueColor, "\u001b[38;2;1;2;3;101m")]
    [InlineData(ColorSystem.EightBit, "\u001b[30;101m")]
    [InlineData(ColorSystem.Standard, "\u001b[30;101m")]
    [InlineData(ColorSystem.NoColors, "\u001b[1m")]
    public void Colors_FitTheTerminalsColorSystem(ColorSystem colors, string expected)
    {
        var encoder = new AnsiFrameEncoder(hideCursor: true);
        var canvas = new TerminalCanvas(4, 1);
        var style = colors == ColorSystem.NoColors
            ? new Style(new Color(1, 2, 3), Color.Red, Decoration.Bold)
            : new Style(new Color(1, 2, 3), Color.Red);
        canvas.Write(0, 0, "x", style);

        encoder.Encode(Frame(canvas, 1), 4, 1, new AnsiOutputCapabilities(colors, Links: false)).ShouldContain(expected + "x");
    }

    [Fact]
    public void Links_AreWrittenOnlyWhenTheTerminalShowsThem()
    {
        var canvas = new TerminalCanvas(10, 1);
        canvas.Write(0, 0, "docs", link: "https://razorconsole.com");

        var withLinks = new AnsiFrameEncoder(hideCursor: true).Encode(Frame(canvas, 1), 10, 1, TrueColorWithLinks);
        var withoutLinks = new AnsiFrameEncoder(hideCursor: true).Encode(Frame(canvas, 1), 10, 1, new AnsiOutputCapabilities(ColorSystem.TrueColor, Links: false));

        withLinks.ShouldContain("\u001b]8;id=1;https://razorconsole.com\u001b\\docs\u001b]8;;\u001b\\");
        withoutLinks.ShouldNotContain("\u001b]8;");
        withoutLinks.ShouldContain("docs");
    }

    [Fact]
    public void TheCursor_IsShownAfterEachFrame_UnlessItIsHidden()
    {
        var canvas = new TerminalCanvas(4, 1);

        new AnsiFrameEncoder(hideCursor: true).Encode(Frame(canvas, 1), 4, 1, TrueColorWithLinks).ShouldNotEndWith("\u001b[?25h");
        new AnsiFrameEncoder(hideCursor: false).Encode(Frame(canvas, 1), 4, 1, TrueColorWithLinks).ShouldEndWith("\u001b[?25h");
    }

    private static TerminalFrame Frame(TerminalCanvas canvas, long sequence)
    {
        // Frames never change after they are painted: present a copy, as the presenter paints a new canvas each time.
        var copy = new TerminalCanvas(canvas.Width, canvas.Height);
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var cell = canvas[x, y];
                if (!cell.IsContinuation)
                {
                    copy.Write(x, y, cell.Text, SpectreCellStyle.ToSpectreStyle(cell.CellStyle), cell.Link);
                }
            }
        }

        return new TerminalFrame(sequence, TestFrames.Of(new Text(string.Empty)), copy);
    }

    private static void ShouldShow(VtEmulator terminal, TerminalFrame frame, string because)
    {
        var screen = terminal.CreateFrame(1, DateTimeOffset.UtcNow);
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var expected = frame[x, y];
                var actual = screen[x, y];
                actual.Text.ShouldBe(expected.Text, $"{because}: text at ({x}, {y})");
                actual.CellStyle.ShouldBe(expected.CellStyle, $"{because}: style at ({x}, {y})");
                actual.Link.ShouldBe(expected.Link, $"{because}: link at ({x}, {y})");
            }
        }
    }

    // Two cells each, written at even columns, so a write never covers half of a wide grapheme.
    private static readonly string[] Texts = ["ab", "zz", "# ", "  ", "工", "字", "éa", "xy"];
    private static readonly string?[] Links = [null, null, null, "https://a.example", "https://b.example"];

    private static TerminalCanvas RandomCanvas(Random random, int width, int height)
    {
        var canvas = new TerminalCanvas(width, height);
        for (var i = 0; i < width * height / 2; i++)
        {
            WriteRandom(random, canvas);
        }

        return canvas;
    }

    private static void Mutate(Random random, TerminalCanvas canvas)
    {
        var changes = random.Next(0, 8);
        for (var i = 0; i < changes; i++)
        {
            WriteRandom(random, canvas);
        }
    }

    private static void WriteRandom(Random random, TerminalCanvas canvas)
    {
        var text = Texts[random.Next(Texts.Length)];
        var x = 2 * random.Next(canvas.Width / 2);
        var y = random.Next(canvas.Height);
        canvas.Write(x, y, text, RandomStyle(random), Links[random.Next(Links.Length)]);
    }

    private static Style? RandomStyle(Random random)
        => random.Next(5) switch
        {
            0 => (Style?)null,
            1 => new Style(Color.FromInt32(random.Next(256))),
            2 => new Style(new Color((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)), Color.FromInt32(random.Next(16))),
            3 => new Style(Color.Default, Color.Default, (Decoration)(1 << random.Next(9))),
            _ => new Style(Color.FromInt32(random.Next(16)), new Color(10, 20, 30), Decoration.Bold | Decoration.Underline),
        };
}
