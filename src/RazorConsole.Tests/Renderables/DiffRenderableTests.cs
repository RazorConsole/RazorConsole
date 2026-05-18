// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Renderables;
using Spectre.Console;
using Spectre.Console.Rendering;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Tests.Renderables;

public sealed class DiffRenderableTests
{
    [Fact]
    public void RenderLineDiff_SkipsUnchangedSegmentsAndWritesDiff()
    {
        var previousLine = new SegmentLine
        {
            new("prefix "),
            new("value"),
        };

        var nextLine = new SegmentLine
        {
            new("prefix "),
            new("changed"),
        };

        var result = DiffRenderable.RenderLineDiff(nextLine, previousLine).ToList();

        var controlSegments = result
            .Select(segment => segment.Text)
            .Where(text => text.Contains(ESC, StringComparison.Ordinal))
            .ToList();

        var prefixWidth = Segment.CellCount(new List<Segment>
        {
            new("prefix "),
        });

        controlSegments.ShouldContain(text => text.Contains(CUF(prefixWidth), StringComparison.Ordinal));
        controlSegments.ShouldNotContain(text => text.Contains(EL(2), StringComparison.Ordinal));
        result.ShouldContain(segment => segment.Text == "changed");
    }

    [Fact]
    public void RenderLineDiff_ClearsTailWhenLineShrinks()
    {
        var previousLine = new SegmentLine
        {
            new("prefix "),
            new("value"),
        };

        var nextLine = new SegmentLine
        {
            new("prefix "),
        };

        var result = DiffRenderable.RenderLineDiff(nextLine, previousLine).ToList();

        var controlSegments = result
            .Select(segment => segment.Text)
            .Where(text => text.Contains(ESC, StringComparison.Ordinal))
            .ToList();

        controlSegments.ShouldContain(text => text.Contains(EL(0), StringComparison.Ordinal));
    }

    [Fact]
    public void Render_DoesNotEmitNextLineAfterFinalLine()
    {
        var renderable = new MultilineRenderable("aaaaa", "bbbbb", "ccccc");
        var diff = new DiffRenderable(renderable, hideCursor: true);
        var nextLine = NEL();

        var result = ((IRenderable)diff).Render(CreateRenderOptions(width: 5, height: 3), 5)
            .Select(segment => segment.Text)
            .ToList();

        result.Count(text => string.Equals(text, nextLine, StringComparison.Ordinal)).ShouldBe(2);
        result[^2].ShouldBe("\r");
    }

    [Fact]
    public void Render_DisablesAndRestoresAutoWrap()
    {
        var renderable = new MultilineRenderable("aaaaa", "bbbbb", "ccccc");
        var diff = new DiffRenderable(renderable, hideCursor: true);

        var result = ((IRenderable)diff).Render(CreateRenderOptions(width: 5, height: 3), 5)
            .Select(segment => segment.Text)
            .ToList();

        result.First().ShouldContain(RM(DECAWM));
        result.Last().ShouldContain(SM(DECAWM));
    }

    private static RenderOptions CreateRenderOptions(int width, int height)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        return new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(width, height));
    }

    private sealed class MultilineRenderable(params string[] lines) : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth)
            => new(lines.Max(line => line.Length), lines.Max(line => line.Length));

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                yield return new Segment(lines[i]);
                if (i < lines.Length - 1)
                {
                    yield return Segment.LineBreak;
                }
            }
        }
    }
}

