// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

public sealed class ScrollStateRegistryTests
{
    [Fact]
    public void ReportLayout_ClampsOffsetToNewLimitsInTheSamePass()
    {
        var registry = new ScrollStateRegistry();
        registry.SetTop("a", 50, ScrollChangeSource.Engine);

        var top = registry.ReportLayout("a", contentHeight: 10, viewportHeight: 4);

        top.ShouldBe(6);
        registry.Get("a").Top.ShouldBe(6);
        registry.Get("a").Metrics.MaxTop.ShouldBe(6);
    }

    [Fact]
    public void ReportLayout_ContentShrinksToZero_ResetsOffset()
    {
        var registry = new ScrollStateRegistry();
        registry.ReportLayout("a", 10, 4);
        registry.SetTop("a", 6, ScrollChangeSource.Engine);

        registry.ReportLayout("a", 0, 4).ShouldBe(0);
    }

    [Fact]
    public void ReportLayout_Sticky_FollowsTheEnd()
    {
        var registry = new ScrollStateRegistry();
        registry.SetSticky("a", true);

        registry.ReportLayout("a", 10, 4).ShouldBe(6);
        registry.ReportLayout("a", 15, 4).ShouldBe(11);
    }

    [Fact]
    public void SetTop_IsClampedOnceMetricsAreKnown()
    {
        var registry = new ScrollStateRegistry();
        registry.ReportLayout("a", 10, 4);

        registry.SetTop("a", 99, ScrollChangeSource.Engine);
        registry.Get("a").Top.ShouldBe(6);

        registry.SetTop("a", -3, ScrollChangeSource.Engine);
        registry.Get("a").Top.ShouldBe(0);
    }

    [Fact]
    public void TopChanged_RaisedForEngineChangesAndClamping_NotForParameterChanges()
    {
        var registry = new ScrollStateRegistry();
        var changes = new List<(string Id, int Top)>();
        var repaints = 0;
        registry.TopChanged += (id, top) => changes.Add((id, top));
        registry.RepaintRequested += () => repaints++;
        registry.ReportLayout("a", 10, 4);

        registry.SetTop("a", 2, ScrollChangeSource.Parameter);
        registry.SetTop("a", 3, ScrollChangeSource.Engine);
        registry.SetTop("a", 3, ScrollChangeSource.Engine);
        registry.ReportLayout("a", 5, 4);

        changes.ShouldBe([("a", 3), ("a", 1)]);
        repaints.ShouldBe(1);
    }

    [Fact]
    public void Remove_ForgetsState()
    {
        var registry = new ScrollStateRegistry();
        registry.ReportLayout("a", 10, 4);
        registry.SetTop("a", 2, ScrollChangeSource.Engine);

        registry.Remove("a");

        registry.Get("a").ShouldBe(ScrollState.Default);
    }

    [Fact]
    public void TwoViewportsWithTheSameId_ShareState_AndBothRender()
    {
        var registry = new ScrollStateRegistry();
        registry.SetTop("dup", 2, ScrollChangeSource.Engine);
        var first = new ScrollViewportWidget("vp1", Rows(10), 0, registry, "dup");
        var second = new ScrollViewportWidget("vp2", Rows(6), 0, registry, "dup");
        var root = new StackWidget("root", [first, second]);

        registry.BeginLayoutPass();
        new LayoutEngine().Layout(root, new BoxConstraints(0, 10, 0, 7));

        // The second viewport reports last, so its limits win.
        registry.Get("dup").Metrics.ContentHeight.ShouldBe(6);
        first.Top.ShouldBe(2);
        second.Top.ShouldBeInRange(0, second.MaxTop);
    }

    [Fact]
    public async Task ConcurrentLayoutInputAndReads_KeepOffsetWithinLimitsAndMetricsConsistent()
    {
        var registry = new ScrollStateRegistry();
        using var stop = new CancellationTokenSource();
        var violations = 0;

        var layout = Task.Run(() =>
        {
            var content = 20;
            while (!stop.IsCancellationRequested)
            {
                content = content == 20 ? 8 : 20;
                registry.ReportLayout("a", content, 5);
            }
        }, TestContext.Current.CancellationToken);
        var input = Task.Run(() =>
        {
            var random = new Random(1);
            while (!stop.IsCancellationRequested)
            {
                registry.SetTop("a", random.Next(-5, 40), ScrollChangeSource.Engine);
            }
        }, TestContext.Current.CancellationToken);
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var state = registry.Get("a");
                var metrics = state.Metrics;
                var consistent = metrics.ViewportHeight is 0 or 5
                    && metrics.ContentHeight is 0 or 8 or 20;
                if (!consistent || state.Top < 0)
                {
                    Interlocked.Increment(ref violations);
                }
            }
        }, TestContext.Current.CancellationToken);

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await Task.WhenAll(layout, input, reader);

        violations.ShouldBe(0);

        // Once layout has the last word, the offset is within limits.
        var final = registry.ReportLayout("a", 20, 5);
        final.ShouldBeInRange(0, 15);
    }

    [Fact]
    public void Translator_WrapsOnlyTheContentOfAnOverflowContainer()
    {
        var registry = new ScrollStateRegistry();
        var context = new WidgetTranslationContext(null, null, null, registry);
        var panel = VNode.CreateElement("div");
        panel.SetAttribute("class", "panel");
        panel.SetAttribute("data-header", "Logs");
        panel.SetAttribute("data-border", "square");
        panel.SetAttribute("data-height", "5");
        panel.SetAttribute("data-overflow", "scroll");
        panel.SetAttribute("data-scroll-id", "logs");
        for (var i = 0; i < 10; i++)
        {
            var row = VNode.CreateElement("div");
            row.AddChild(VNode.CreateText($"row {i}"));
            panel.AddChild(row);
        }

        var widget = context.Translate(panel);

        var box = widget.ShouldBeOfType<BoxWidget>();
        box.VNodeId.ShouldBe(panel.ID);
        var viewport = box.Children[0].ShouldBeOfType<ScrollViewportWidget>();
        viewport.ScrollId.ShouldBe("logs");

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));
        registry.Get("logs").Metrics.ContentHeight.ShouldBe(10);
        registry.Get("logs").Metrics.ViewportHeight.ShouldBe(3);
        result.Size.Height.ShouldBe(5);
    }

    [Fact]
    public void Translator_PlainDivWithOverflowScroll_ScrollsItsChildren()
    {
        var context = new WidgetTranslationContext(null, null, null, new ScrollStateRegistry());
        var div = VNode.CreateElement("div");
        div.SetAttribute("data-overflow", "scroll");
        for (var i = 0; i < 6; i++)
        {
            var row = VNode.CreateElement("div");
            row.AddChild(VNode.CreateText($"r{i}"));
            div.AddChild(row);
        }

        var widget = context.Translate(div);

        widget.Children.Single().ShouldBeOfType<ScrollViewportWidget>();
        _ = RenderLines(new LayoutEngine().Layout(widget, new BoxConstraints(0, 10, 0, 3)));
    }

    [Fact]
    public void Translator_OverflowHidden_ClipsWithoutScrollState()
    {
        var registry = new ScrollStateRegistry();
        var context = new WidgetTranslationContext(null, null, null, registry);
        var box = VNode.CreateElement("div");
        box.SetAttribute("data-layout", "box");
        box.SetAttribute("data-overflow", "hidden");
        for (var i = 0; i < 6; i++)
        {
            var row = VNode.CreateElement("div");
            row.AddChild(VNode.CreateText($"r{i}"));
            box.AddChild(row);
        }

        var widget = context.Translate(box);

        var viewport = widget.Children.Single().ShouldBeOfType<ScrollViewportWidget>();
        viewport.ScrollId.ShouldBeNull();
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 10, 0, 3));
        RenderLines(result).ShouldBe(["r0", "r1", "r2"]);
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
}
