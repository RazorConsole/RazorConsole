// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

public sealed class TerminalCanvasTests
{
    [Fact]
    public void Write_WritesTextAtCoordinates()
    {
        var canvas = new TerminalCanvas(8, 2);

        canvas.Write(2, 1, "abc");

        canvas[2, 1].Text.ShouldBe("a");
        canvas[3, 1].Text.ShouldBe("b");
        canvas[4, 1].Text.ShouldBe("c");
    }

    [Fact]
    public void Write_ClipsTextOutsideCanvas()
    {
        var canvas = new TerminalCanvas(3, 1);

        canvas.Write(-1, 0, "abcd");

        canvas[0, 0].Text.ShouldBe("b");
        canvas[1, 0].Text.ShouldBe("c");
        canvas[2, 0].Text.ShouldBe("d");
    }

    [Fact]
    public void Fill_ClipsRectAndAppliesStyle()
    {
        var canvas = new TerminalCanvas(4, 2);
        var style = new Style(foreground: Color.Red);

        canvas.Fill(new LayoutRect(2, 0, 10, 2), '#', style);

        canvas[1, 0].Text.ShouldBe(" ");
        canvas[2, 0].Text.ShouldBe("#");
        canvas[2, 0].Style.ShouldBe(style);
        canvas[3, 1].Text.ShouldBe("#");
    }

    [Fact]
    public void ToRenderable_RendersPrecomputedCanvasRows()
    {
        var canvas = new TerminalCanvas(5, 2);
        canvas.Write(0, 0, "hello");
        canvas.Write(1, 1, "x");

        var text = RenderToText(canvas.ToRenderable(), maxWidth: 5);

        text.ShouldBe("hello\n x   ");
    }

    [Fact]
    public void ToRenderable_ClipsToMaxWidth()
    {
        var canvas = new TerminalCanvas(5, 1);
        canvas.Write(0, 0, "hello");

        var text = RenderToText(canvas.ToRenderable(), maxWidth: 3);

        text.ShouldBe("hel");
    }

    private static string RenderToText(IRenderable renderable, int maxWidth)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        var options = new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(maxWidth, 25));
        var segments = renderable.Render(options, maxWidth);
        return string.Concat(segments.Select(segment => segment.IsLineBreak ? "\n" : segment.Text));
    }
}
