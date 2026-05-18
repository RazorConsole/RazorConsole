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

        captured.Count.ShouldBe(3);
        GetControlCode(captured[0]).ShouldBe(SM(DECALTSCR));
        captured[1].ShouldBeOfType<RazorConsole.Core.Renderables.DiffRenderable>();
        GetControlCode(captured[2]).ShouldBe(RM(DECALTSCR));
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
