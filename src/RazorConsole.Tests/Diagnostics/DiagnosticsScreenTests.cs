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
/// The renderer screen (proposal §3.7) and the painted cell of <c>inspectAt</c>, both read from the latest frame's
/// lazily painted canvas.
/// </summary>
public sealed class DiagnosticsScreenTests
{
    [Fact]
    public async Task Screen_AndInspectAt_ReadTheCanvasOfTheLatestDeliveredFrame()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        using var renderer = DiagnosticsHooksTests.CreateObservedRenderer(state);
        await DiagnosticsHooksTests.MountAsync<LabelComponent>(renderer, state, ct);
        var frame = state.LatestFrame.ShouldNotBeNull();
        var queries = new DiagnosticsAppQueries(state, new FocusManager(), new ConsoleNavigationManager(), new ConsoleAppOptions());

        var result = queries.Screen(new ScreenParams());

        var canvas = frame.PaintCanvas().ShouldNotBeNull();
        result.Source.ShouldBe("renderer");
        result.Frame.ShouldBe(frame.Sequence);
        result.Width.ShouldBe(canvas.Width);
        result.Lines.Count.ShouldBe(canvas.Height);
        var row = result.Lines.ToList().FindIndex(line => line.Contains(LabelComponent.Text, StringComparison.Ordinal));
        row.ShouldBeGreaterThanOrEqualTo(0);
        var column = result.Lines[row].IndexOf(LabelComponent.Text, StringComparison.Ordinal);

        // A frame built only for a hit test is never delivered, so the screen keeps showing the delivered one.
        renderer.RefreshSnapshot();
        state.LatestFrame.ShouldBeSameAs(frame);

        var at = queries.InspectAt(new InspectAtParams(column, row));
        at.Frame.ShouldBe(frame.Sequence);
        at.Cell.ShouldNotBeNull().Text.ShouldBe("h");
        at.Stack.ShouldNotBeEmpty();
        at.Stack[0].Tag.ShouldBe("div");
    }

    [Fact]
    public void Screen_ReturnsLinesCellsAndAClippedRegion()
    {
        var state = new DiagnosticsState();
        var frame = new DrawWidget(6, 2, canvas =>
        {
            canvas.Write(0, 0, "ab", new Style(new Color(255, 0, 0), new Color(0, 0, 255), Decoration.Bold | Decoration.Underline, "https://example.test"));
            canvas.Write(0, 1, "cd");
        }).Publish(state);
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
    public void Screen_WithoutACanvas_IsUnavailableWithAReason(string pipeline, string reason)
    {
        var state = new DiagnosticsState();
        var queries = new DiagnosticsAppQueries(state, new FocusManager(), new ConsoleNavigationManager(), new ConsoleAppOptions { RenderingPipeline = Enum.Parse<RazorConsoleRenderingPipeline>(pipeline) });

        // Before the first frame, and for a frame whose renderable is not a widget canvas (legacy pipeline, nothing mounted).
        foreach (var publish in new Action[] { () => { }, () => state.PublishFrame(null, null, null), () => state.PublishFrame(null, null, null, new Text("legacy")) })
        {
            publish();

            var error = Should.Throw<DiagnosticsProtocolException>(() => queries.Screen(new ScreenParams())).ToError();

            error.Code.ShouldBe(DiagnosticsErrorCodes.ScreenUnavailable);
            error.Data.ShouldNotBeNull().Deserialize(DiagnosticsJsonContext.Default.DiagnosticsUnavailableData)!.Reason.ShouldBe(reason);
            queries.InspectAt(new InspectAtParams(0, 0)).Cell.ShouldBeNull();
        }
    }

    [Fact]
    public void InspectAt_ReturnsNoCellOutsideTheCanvas()
    {
        var state = new DiagnosticsState();
        new DrawWidget(2, 2).Publish(state);
        var queries = new DiagnosticsAppQueries(state, new FocusManager(), new ConsoleNavigationManager(), new ConsoleAppOptions());

        queries.InspectAt(new InspectAtParams(1, 1)).Cell.ShouldNotBeNull().Text.ShouldBe(" ");
        queries.InspectAt(new InspectAtParams(2, 0)).Cell.ShouldBeNull();
        queries.InspectAt(new InspectAtParams(-1, 0)).Cell.ShouldBeNull();
    }

    [Fact]
    public void Frame_PaintsOnlyWhenACanvasIsRequested_AndOnlyOnce()
    {
        var state = new DiagnosticsState();
        var widget = new DrawWidget(4, 1);
        var frame = widget.Publish(state);

        widget.Paints.ShouldBe(0);

        var first = frame.PaintCanvas().ShouldNotBeNull();
        var second = frame.PaintCanvas();

        second.ShouldBeSameAs(first);
        widget.Paints.ShouldBe(1);
        new DiagnosticsAppQueries(state, new FocusManager(), new ConsoleNavigationManager(), new ConsoleAppOptions()).Screen(new ScreenParams());
        widget.Paints.ShouldBe(1);
    }

    [Fact]
    public void Frame_PaintsOnceWhenRequestsAskForItConcurrently()
    {
        const int Requests = 16;
        var state = new DiagnosticsState();
        var widget = new DrawWidget(4, 1, _ => Thread.Sleep(20));
        var frame = widget.Publish(state);
        var canvases = new TerminalCanvas?[Requests];

        // Dedicated threads: blocking this many thread-pool threads on a barrier starves every other test in the
        // process for seconds (the pool adds about two threads a second beyond its minimum).
        using var start = new Barrier(Requests);
        var threads = Enumerable.Range(0, Requests).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            canvases[i] = frame.PaintCanvas();
        })
        {
            IsBackground = true,
        }).ToList();
        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        widget.Paints.ShouldBe(1);
        canvases[0].ShouldNotBeNull();
        canvases.ShouldAllBe(canvas => ReferenceEquals(canvas, canvases[0]));
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

/// <summary>
/// A widget of a fixed size that counts its paints and draws what the test says, to build frames with a known canvas.
/// </summary>
internal sealed class DrawWidget(int width, int height, Action<TerminalCanvas>? draw = null) : Widget("draw")
{
    private int _paints;

    public int Paints => Volatile.Read(ref _paints);

    public IRenderable ToRenderable()
        => new LayoutEngine().Layout(this, new BoxConstraints(0, width, 0, height)).PaintToRenderable();

    public DiagnosticsFrame Publish(DiagnosticsState state) => state.PublishFrame(null, null, null, ToRenderable());

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints) => new(width, height);

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
    }

    protected override void PaintCore(PaintContext context)
    {
        Interlocked.Increment(ref _paints);
        draw?.Invoke(context.Canvas);
    }
}
