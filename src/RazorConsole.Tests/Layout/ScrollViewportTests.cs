// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

public sealed class ScrollViewportTests
{
    [Fact]
    public void ScrollbarGeometry_ThumbIsClampedWhenOffsetExceedsMax()
    {
        // 10 rows of content, 3 visible, track of 3: max offset is 7.
        var atEnd = ScrollbarGeometry.CalculateThumb(trackLength: 3, viewportLength: 3, contentLength: 10, offset: 7);
        var past = ScrollbarGeometry.CalculateThumb(trackLength: 3, viewportLength: 3, contentLength: 10, offset: 500);

        past.ShouldBe(atEnd);
        (past.Top + past.Length).ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public void ScrollbarGeometry_NegativeOffsetStartsAtTop()
        => ScrollbarGeometry.CalculateThumb(10, 5, 20, -4).Top.ShouldBe(0);

    [Theory]
    [InlineData(0, 3, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 3, 0)]
    public void ScrollbarGeometry_NothingToDraw_ReturnsEmptyThumb(int track, int viewport, int content)
        => ScrollbarGeometry.CalculateThumb(track, viewport, content, 0).ShouldBe((0, 0));

    [Fact]
    public void ScrollbarGeometry_ContentFitsViewport_ThumbFillsTrack()
        => ScrollbarGeometry.CalculateThumb(5, 10, 4, 0).ShouldBe((0, 5));

    [Fact]
    public void ScrollbarGeometry_OffsetForThumbTop_RoundTripsEnds()
    {
        var thumb = ScrollbarGeometry.CalculateThumb(10, 5, 50, 45);
        ScrollbarGeometry.OffsetForThumbTop(thumb.Top, 10, thumb.Length, 5, 50).ShouldBe(45);
        ScrollbarGeometry.OffsetForThumbTop(0, 10, thumb.Length, 5, 50).ShouldBe(0);
    }

    [Fact]
    public void BoxConstraints_Deflate_KeepsUnboundedHeight()
    {
        var constraints = new BoxConstraints(0, 10, 0, 5).WithUnboundedHeight().Deflate(1, 2, 1, 2);

        constraints.HasBoundedHeight.ShouldBeFalse();
        constraints.MaxHeight.ShouldBe(BoxConstraints.UnboundedHeight);
    }

    [Fact]
    public void Viewport_ShowsWindowOfContentAtTop()
    {
        var viewport = new ScrollViewportWidget("vp", Rows(10), top: 2);

        var result = new LayoutEngine().Layout(viewport, new BoxConstraints(0, 10, 0, 3));

        result.Size.ShouldBe(new LayoutSize(2, 3));
        RenderLines(result).ShouldBe(["r2", "r3", "r4"]);
        viewport.ContentHeight.ShouldBe(10);
        viewport.MaxTop.ShouldBe(7);
    }

    [Fact]
    public void Viewport_ClampsTopAfterContentShrinks()
    {
        var viewport = new ScrollViewportWidget("vp", Rows(5), top: 100);

        var result = new LayoutEngine().Layout(viewport, new BoxConstraints(0, 10, 0, 3));

        viewport.Top.ShouldBe(2);
        RenderLines(result).ShouldBe(["r2", "r3", "r4"]);
    }

    [Fact]
    public void Viewport_ContentShrinksToZero_ResetsTop()
    {
        var viewport = new ScrollViewportWidget("vp", new StackWidget("rows", []), top: 5);

        new LayoutEngine().Layout(viewport, new BoxConstraints(0, 10, 0, 3));

        viewport.ContentHeight.ShouldBe(0);
        viewport.MaxTop.ShouldBe(0);
        viewport.Top.ShouldBe(0);
    }

    [Fact]
    public void Viewport_GrowsWithContentUpToParentLimit()
    {
        var small = new LayoutEngine().Layout(new ScrollViewportWidget("vp", Rows(2)), new BoxConstraints(0, 10, 0, 5));
        var large = new LayoutEngine().Layout(new ScrollViewportWidget("vp", Rows(20)), new BoxConstraints(0, 10, 0, 5));

        small.Size.Height.ShouldBe(2);
        large.Size.Height.ShouldBe(5);
    }

    [Fact]
    public void Viewport_FillHeightChild_ResolvesToContentHeight_NotUnboundedHeight()
    {
        var box = new BoxWidget("box", new TextWidget("t", "x"), fillHeight: true, border: BoxBorderStyle.Square);
        var viewport = new ScrollViewportWidget("vp", box);

        var result = new LayoutEngine().Layout(viewport, new BoxConstraints(0, 10, 0, 10));

        result.Size.Height.ShouldBe(3);
        box.Bounds.Height.ShouldBe(3);
    }

    [Fact]
    public void Viewport_FillHeightChild_FillsViewportWhenParentForcesHeight()
    {
        var box = new BoxWidget("box", new TextWidget("t", "x"), fillHeight: true, border: BoxBorderStyle.Square);
        var viewport = new ScrollViewportWidget("vp", box);

        new LayoutEngine().Layout(viewport, new BoxConstraints(0, 10, 8, 8));

        box.Bounds.Height.ShouldBe(8);
    }

    [Fact]
    public void Viewport_PaintSkipsRowsOutsideClip()
    {
        var counter = new PaintCounter();
        var rows = Enumerable.Range(0, 200).Select(i => (Widget)new CountingWidget($"row{i}", counter)).ToArray();
        var viewport = new ScrollViewportWidget("vp", new StackWidget("rows", rows), top: 100);

        var result = new LayoutEngine().Layout(viewport, new BoxConstraints(0, 10, 0, 4));
        RenderLines(result);

        counter.Painted.ShouldBe(4);
    }

    [Fact]
    public void LayoutBox_ScrolledOutRows_HaveEmptyVisibleBounds()
    {
        var viewport = new ScrollViewportWidget("vp", Rows(10), top: 2);

        var result = new LayoutEngine().Layout(viewport, new BoxConstraints(0, 10, 0, 3));
        var infos = result.EnumerateLayoutInfos().ToDictionary(info => info.VNodeId);

        infos["row0"].VisibleBounds!.Value.IsEmpty.ShouldBeTrue();
        infos["row2"].VisibleBounds.ShouldBe(new LayoutRect(0, 0, 2, 1));
        infos["row4"].VisibleBounds.ShouldBe(new LayoutRect(0, 2, 2, 1));
        infos["row5"].VisibleBounds!.Value.IsEmpty.ShouldBeTrue();
        infos["row0"].Top.ShouldBe(-2);
    }

    [Fact]
    public void LayoutBox_ClipIsInheritedByDescendantsOnly()
    {
        var viewport = new ScrollViewportWidget("vp", Rows(3), top: 0);
        var root = new StackWidget("root", [new TextWidget("above", "a"), viewport]);

        var result = new LayoutEngine().Layout(root, new BoxConstraints(0, 10, 0, 2));
        var boxes = result.EnumerateLayoutBoxes().ToDictionary(box => box.VNodeId);

        boxes["above"].Clip.ShouldBeNull();
        boxes["vp"].Clip.ShouldBeNull();
        boxes["row0"].Clip.ShouldBe(viewport.Bounds);
    }

    [Fact]
    public void LayoutBox_AbsoluteDescendantsOfViewport_AreNotClipped()
    {
        var absolute = new TextWidget(
            "abs",
            "X",
            attributes: new Dictionary<string, string?> { ["position"] = "absolute", ["top"] = "0", ["left"] = "8" });
        var viewport = new ScrollViewportWidget("vp", new StackWidget("content", [new TextWidget("line", "line"), absolute]));

        var result = new LayoutEngine().Layout(viewport, new BoxConstraints(0, 20, 0, 5));
        var boxes = result.EnumerateLayoutBoxes().ToDictionary(box => box.VNodeId);

        boxes["line"].Clip.ShouldNotBeNull();
        boxes["abs"].Clip.ShouldBeNull();
        boxes["abs"].VisibleBounds.ShouldBe(boxes["abs"].Bounds);
    }

    [Fact]
    public void Viewport_InsideAbsoluteContainer_ClipsItsContent()
    {
        var viewport = new ScrollViewportWidget("vp", Rows(10), top: 3);
        var container = new StackWidget(
            "abs",
            [viewport],
            attributes: new Dictionary<string, string?> { ["position"] = "absolute", ["top"] = "1", ["left"] = "2" });
        var root = new StackWidget("root", [new TextWidget("base", "0123456789"), container]);

        var result = new LayoutEngine().Layout(root, new BoxConstraints(0, 20, 0, 3));
        var boxes = result.EnumerateLayoutBoxes().ToDictionary(box => box.VNodeId);

        viewport.Bounds.Y.ShouldBe(1);
        boxes["abs"].Clip.ShouldBeNull();
        boxes["row3"].Clip.ShouldBe(viewport.Bounds);
        boxes["row3"].VisibleBounds.ShouldBe(boxes["row3"].Bounds);
        boxes["row0"].VisibleBounds.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Viewport_TableContent_IsCroppedAndScrolls()
    {
        var body = Enumerable.Range(0, 6)
            .Select(i => new TableWidgetRow([new TableWidgetCell($"td{i}", new TextWidget($"tx{i}", $"row{i}"))]))
            .ToArray();
        var table = new TableWidget("table", [], body);
        var viewport = new ScrollViewportWidget("vp", table, top: 3);

        var result = new LayoutEngine().Layout(viewport, new BoxConstraints(0, 20, 0, 4));

        result.Size.Height.ShouldBe(4);
        viewport.ContentHeight.ShouldBe(8);
        viewport.Top.ShouldBe(3);
        RenderLines(result).ShouldContain(line => line.Contains("row3"));
    }

    [Fact]
    public void SpectreWidget_UnderUnboundedHeight_UsesNaturalHeight()
    {
        var widget = new SpectreWidget("spectre", new Panel("hello"));
        var constraints = new BoxConstraints(0, 20, 0, 5).WithUnboundedHeight();

        var size = widget.Measure(new LayoutContext(), constraints);

        size.Height.ShouldBe(3);
    }

    [Fact]
    public void AlignAndRule_UnderUnboundedHeight_UseContentHeight()
    {
        var constraints = new BoxConstraints(0, 20, 0, 5).WithUnboundedHeight();
        var align = new AlignWidget("align", new TextWidget("t", "hi"), HorizontalAlignment.Center);
        var rule = new RuleWidget("rule");
        var context = new LayoutContext();

        align.Measure(context, constraints).Height.ShouldBe(1);
        rule.Measure(context, constraints).Height.ShouldBe(1);
    }

    [Fact]
    public void Layout_RootWithUnboundedHeight_SizesToContent()
    {
        var result = new LayoutEngine().Layout(Rows(7), new BoxConstraints(0, 10, 0, 5).WithUnboundedHeight());

        result.Size.Height.ShouldBe(7);
    }

    [Fact]
    public void ScrollableWidget_ThumbStaysInsideTrackWhenOffsetExceedsMax()
    {
        var widget = new ScrollableWidget(
            "scroll",
            Rows(10),
            itemsCount: 0,
            offset: 500,
            pageSize: 3,
            enableEmbedded: false,
            showScrollbar: true,
            cropLines: true,
            autoPageSize: true);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 3));

        RenderLines(result).ShouldBe(["r7 │", "r8 │", "r9 █"]);
    }

    private static StackWidget Rows(int count)
        => new("rows", Enumerable.Range(0, count).Select(i => (Widget)new TextWidget($"row{i}", $"r{i}")).ToArray());

    private static string[] RenderLines(LayoutResult result)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });
        var options = new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(40, 25));
        var text = string.Concat(result.PaintToRenderable().Render(options, 40).Select(s => s.IsLineBreak ? "\n" : s.Text));
        return text.Split('\n').Select(line => line.TrimEnd()).ToArray();
    }

    private sealed class PaintCounter
    {
        public int Painted { get; set; }
    }

    private sealed class CountingWidget(string id, PaintCounter counter) : Widget(id)
    {
        protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
            => constraints.Constrain(new LayoutSize(1, 1));

        protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
        {
        }

        protected override void PaintCore(PaintContext context)
            => counter.Painted++;
    }
}
