// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using RazorConsole.Core;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Diagnostics;

/// <summary>
/// The renderer screen (proposal §3.7) and the painted cell of <c>inspectAt</c>.
/// </summary>
public sealed class DiagnosticsScreenTests
{
    [Fact]
    public async Task Renderer_PublishesEachPaintedCanvasWithTheFrameItShows()
    {
        var state = new DiagnosticsState();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state);
        await renderer.MountComponentAsync<LabelComponent>(ParameterView.Empty, TestContext.Current.CancellationToken);
        var snapshot = renderer.RefreshSnapshot();
        var painted = state.LatestFrame.ShouldNotBeNull();
        state.LatestScreen.ShouldBeNull();

        Paint(snapshot.Renderable!);

        var screen = state.LatestScreen.ShouldNotBeNull();
        screen.Frame.ShouldBeSameAs(painted);

        // A frame built only for a hit test is never painted, so the screen keeps showing the painted one.
        renderer.RefreshSnapshot();
        state.LatestFrame!.Sequence.ShouldBeGreaterThan(painted.Sequence);
        state.LatestScreen.ShouldBeSameAs(screen);

        var queries = new DiagnosticsAppQueries(state, new FocusManager(), new ConsoleNavigationManager(), new ConsoleAppOptions());
        var result = queries.Screen(new ScreenParams());
        result.Source.ShouldBe("renderer");
        result.Frame.ShouldBe(painted.Sequence);
        result.Width.ShouldBe(screen.Canvas.Width);
        result.Lines.Count.ShouldBe(screen.Canvas.Height);
        var row = result.Lines.ToList().FindIndex(line => line.Contains(LabelComponent.Text, StringComparison.Ordinal));
        row.ShouldBeGreaterThanOrEqualTo(0);
        var column = result.Lines[row].IndexOf(LabelComponent.Text, StringComparison.Ordinal);

        var at = queries.InspectAt(new InspectAtParams(column, row));
        at.Frame.ShouldBe(painted.Sequence);
        at.Cell.ShouldNotBeNull().Text.ShouldBe("h");
        at.Stack.ShouldNotBeEmpty();
        at.Stack[0].Tag.ShouldBe("div");
    }

    [Fact]
    public void Screen_ReturnsLinesCellsAndAClippedRegion()
    {
        var state = new DiagnosticsState();
        var canvas = new TerminalCanvas(6, 2);
        canvas.Write(0, 0, "ab", new Style(new Color(255, 0, 0), new Color(0, 0, 255), Decoration.Bold | Decoration.Underline, "https://example.test"));
        canvas.Write(0, 1, "cd");
        var frame = state.PublishFrame(null, null, null);
        state.PublishScreen(frame, canvas);
        var queries = new DiagnosticsAppQueries(state, new FocusManager(), new ConsoleNavigationManager(), new ConsoleAppOptions());

        var full = queries.Screen(new ScreenParams());
        full.Lines.ShouldBe(["ab    ", "cd    "]);
        full.Width.ShouldBe(6);
        full.Height.ShouldBe(2);
        full.Frame.ShouldBe(frame.Sequence);
        full.Cells.ShouldBeNull();
        full.Region.ShouldBeNull();

        var part = queries.Screen(new ScreenParams(IncludeCells: true, Region: new DiagnosticsRegion(1, 0, 10, 5)));
        part.Region.ShouldBe(new DiagnosticsRegion(1, 0, 5, 2));
        part.Lines.ShouldBe(["b    ", "d    "]);
        var styled = part.Cells.ShouldNotBeNull()[0][0];
        styled.Text.ShouldBe("b");
        styled.Fg.ShouldBe("#ff0000");
        styled.Bg.ShouldBe("#0000ff");
        styled.Decoration.ShouldBe(["bold", "underline"]);
        styled.Link.ShouldBe("https://example.test");
        var plain = part.Cells[1][0];
        plain.Text.ShouldBe("d");
        plain.Fg.ShouldBeNull();
        plain.Bg.ShouldBeNull();
        plain.Decoration.ShouldBeNull();

        Should.Throw<DiagnosticsProtocolException>(() => queries.Screen(new ScreenParams(Region: new DiagnosticsRegion(-1, 0, 1, 1))))
            .Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
    }

    [Theory]
    [InlineData("WidgetLayout", DiagnosticsUnavailableReasons.NotPainted)]
    [InlineData("LegacySpectre", DiagnosticsUnavailableReasons.LegacyPipeline)]
    public void Screen_BeforeAnyPaint_IsUnavailableWithAReason(string pipeline, string reason)
    {
        var state = new DiagnosticsState();
        state.PublishFrame(null, null, null);
        var queries = new DiagnosticsAppQueries(state, new FocusManager(), new ConsoleNavigationManager(), new ConsoleAppOptions { RenderingPipeline = Enum.Parse<RazorConsoleRenderingPipeline>(pipeline) });

        var error = Should.Throw<DiagnosticsProtocolException>(() => queries.Screen(new ScreenParams())).ToError();

        error.Code.ShouldBe(DiagnosticsErrorCodes.ScreenUnavailable);
        error.Data.ShouldNotBeNull().Deserialize(DiagnosticsJsonContext.Default.DiagnosticsUnavailableData)!.Reason.ShouldBe(reason);
        queries.InspectAt(new InspectAtParams(0, 0)).Cell.ShouldBeNull();
    }

    [Fact]
    public void InspectAt_ReturnsNoCellOutsideTheCanvas()
    {
        var state = new DiagnosticsState();
        state.PublishScreen(state.PublishFrame(null, null, null), new TerminalCanvas(2, 2));
        var queries = new DiagnosticsAppQueries(state, new FocusManager(), new ConsoleNavigationManager(), new ConsoleAppOptions());

        queries.InspectAt(new InspectAtParams(1, 1)).Cell.ShouldNotBeNull().Text.ShouldBe(" ");
        queries.InspectAt(new InspectAtParams(2, 0)).Cell.ShouldBeNull();
        queries.InspectAt(new InspectAtParams(-1, 0)).Cell.ShouldBeNull();
    }

    internal static void Paint(IRenderable renderable, int width = 40, int height = 10)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        _ = renderable.Render(new RenderOptions(console.Profile.Capabilities, new Size(width, height)), width).ToList();
    }

    private sealed class LabelComponent : ComponentBase
    {
        public const string Text = "hello";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddContent(1, Text);
            builder.CloseElement();
        }
    }
}
