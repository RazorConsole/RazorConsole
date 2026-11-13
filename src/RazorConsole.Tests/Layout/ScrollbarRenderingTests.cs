// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

public sealed class ScrollbarRenderingTests
{
    private static readonly ScrollbarOptions Chars = new(TrackChar: '░', ThumbChar: '█');

    [Fact]
    public void Border_PaintsTheBarOverTheRightBorder()
    {
        var box = CreateBox(rows: 6, Chars);

        RenderLines(box).ShouldBe(
        [
            "┌──┐",
            "│r0█",
            "│r1█",
            "│r2░",
            "└──┘",
        ]);
    }

    [Fact]
    public void Border_ThumbFollowsTheOffset()
    {
        var box = CreateBox(rows: 6, Chars, top: 3);

        RenderLines(box).ShouldBe(
        [
            "┌──┐",
            "│r3░",
            "│r4█",
            "│r5█",
            "└──┘",
        ]);
    }

    [Fact]
    public void Border_ContentFits_ShowsNoBarWhenAuto()
    {
        var box = CreateBox(rows: 2, Chars);

        RenderLines(box).ShouldBe(
        [
            "┌──┐",
            "│r0│",
            "│r1│",
            "│  │",
            "└──┘",
        ]);
    }

    [Fact]
    public void Border_ContentFits_ShowsFullThumbWhenAlways()
    {
        var box = CreateBox(rows: 2, Chars with { Visibility = ScrollbarVisibility.Always });

        RenderLines(box).ShouldBe(
        [
            "┌──┐",
            "│r0█",
            "│r1█",
            "│  │",
            "└──┘",
        ]);
    }

    [Fact]
    public void Visibility_Never_HidesTheBar()
    {
        var box = CreateBox(rows: 6, Chars with { Visibility = ScrollbarVisibility.Never });

        RenderLines(box).ShouldBe(
        [
            "┌──┐",
            "│r0│",
            "│r1│",
            "│r2│",
            "└──┘",
        ]);
    }

    [Fact]
    public void Inset_ReservesAColumnInsideTheBoxOnlyWhileOverflowing()
    {
        var overflowing = CreateBox(rows: 6, Chars with { Placement = ScrollbarPlacement.Inset });
        var fitting = CreateBox(rows: 2, Chars with { Placement = ScrollbarPlacement.Inset });

        RenderLines(overflowing).ShouldBe(
        [
            "┌───┐",
            "│r0█│",
            "│r1█│",
            "│r2░│",
            "└───┘",
        ]);
        RenderLines(fitting).ShouldBe(
        [
            "┌──┐",
            "│r0│",
            "│r1│",
            "│  │",
            "└──┘",
        ]);
    }

    [Fact]
    public void Outside_AddsAColumnRightOfTheBorder()
    {
        var box = CreateBox(rows: 6, Chars with { Placement = ScrollbarPlacement.Outside });

        RenderLines(box).ShouldBe(
        [
            "┌──┐█",
            "│r0│█",
            "│r1│█",
            "│r2│░",
            "└──┘░",
        ]);
    }

    [Fact]
    public void Border_WithoutARightBorder_FallsBackToInset()
    {
        var box = CreateBox(rows: 6, Chars, borderRight: BoxBorderStyle.None);

        RenderLines(box).ShouldBe(
        [
            "┌───",
            "│r0█",
            "│r1█",
            "│r2░",
            "└───",
        ]);
    }

    [Fact]
    public void PublishesTrackAndThumbForHitTesting()
    {
        var registry = new ScrollStateRegistry();
        var box = CreateBox(rows: 6, Chars, registry: registry, scrollId: "s");

        var result = new LayoutEngine().Layout(box, new BoxConstraints(0, 20, 0, 10));
        var scroll = result.EnumerateLayoutBoxes().Single(b => b.VNodeId == "box").Scroll;

        scroll.ShouldNotBeNull();
        scroll.ScrollId.ShouldBe("s");
        scroll.Viewport.ShouldBe(new LayoutRect(1, 1, 2, 3));
        scroll.Track.ShouldBe(new LayoutRect(3, 1, 1, 3));
        scroll.Thumb.ShouldBe(new LayoutRect(3, 1, 1, 2));
        scroll.ContentHeight.ShouldBe(6);
        scroll.ViewportHeight.ShouldBe(3);
        result.EnumerateLayoutInfos().Single(i => i.VNodeId == "box").Scroll.ShouldBe(scroll);
    }

    [Fact]
    public void NoBarShowing_PublishesAnEmptyTrack()
    {
        var box = CreateBox(rows: 2, Chars, registry: new ScrollStateRegistry(), scrollId: "s");

        var result = new LayoutEngine().Layout(box, new BoxConstraints(0, 20, 0, 10));

        var scroll = result.EnumerateLayoutBoxes().Single(b => b.VNodeId == "box").Scroll;
        scroll.ShouldNotBeNull();
        scroll.ScrollId.ShouldBe("s");
        scroll.Track.IsEmpty.ShouldBeTrue();
        scroll.Thumb.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void ThumbStaysInsideTheTrackWhenTheOffsetIsTooLarge()
    {
        var box = CreateBox(rows: 6, Chars, top: 500);

        RenderLines(box).ShouldBe(
        [
            "┌──┐",
            "│r3░",
            "│r4█",
            "│r5█",
            "└──┘",
        ]);
    }

    private static BoxWidget CreateBox(
        int rows,
        ScrollbarOptions bar,
        int top = 0,
        BoxBorderStyle? borderRight = null,
        ScrollStateRegistry? registry = null,
        string? scrollId = null)
    {
        var content = new StackWidget(
            "rows",
            Enumerable.Range(0, rows).Select(i => (Widget)new TextWidget($"row{i}", $"r{i}")).ToArray());
        var viewport = registry is null
            ? new ScrollViewportWidget("viewport", content, top)
            : new ScrollViewportWidget("viewport", content, top, registry, scrollId);
        if (registry is not null)
        {
            registry.SetTop(scrollId!, top, ScrollChangeSource.Parameter);
        }

        return new BoxWidget("box", viewport, height: 5, border: BoxBorderStyle.Square, borderRight: borderRight)
        {
            Scrollbar = bar,
        };
    }

    private static string[] RenderLines(Widget widget)
    {
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));
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
}
