// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

public sealed class WidgetLayoutTests
{
    [Fact]
    public void TextWidget_Layout_ProducesBoundsAndRenderableOutput()
    {
        var widget = new TextWidget("text-1", "hello");
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 5));

        result.Size.ShouldBe(new LayoutSize(5, 1));
        result.RootBox.Bounds.ShouldBe(new LayoutRect(0, 0, 5, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("hello");
    }

    [Fact]
    public void TextWidget_Layout_ClipsToMaxWidth()
    {
        var widget = new TextWidget("text-1", "hello");
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 3, 0, 5));

        result.Size.ShouldBe(new LayoutSize(3, 2));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("hel\nlo ");
    }

    [Fact]
    public void TextWidget_Layout_HandlesMultilineText()
    {
        var widget = new TextWidget("text-1", "hello\nworld");
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 5));

        result.Size.ShouldBe(new LayoutSize(5, 2));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("hello\nworld");
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesDataTextElementToTextWidget()
    {
        var node = VNode.CreateElement("span");
        node.SetAttribute("data-text", "true");
        node.SetAttribute("data-content", "Current count:");
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 5));

        widget.ShouldBeOfType<TextWidget>();
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("Current count:");
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesFigletElementToSpectreWidgetFallback()
    {
        var node = VNode.CreateElement("div");
        node.SetAttribute("class", "figlet");
        node.SetAttribute("data-content", "Counter Example");
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 12));
        var output = RenderToText(result.PaintToRenderable(), maxWidth: 80);

        widget.ShouldBeOfType<SpectreWidget>();
        output.ShouldContain("___");
        output.ShouldContain("|");
        output.ShouldNotBe("Counter Example");
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesHtmlInlineContainerToSpectreWidgetFallback()
    {
        var node = VNode.CreateElement("p");
        var strong = VNode.CreateElement("strong");
        strong.AddChild(VNode.CreateText("Tip:"));
        var em = VNode.CreateElement("em");
        em.AddChild(VNode.CreateText("HTML emphasis"));
        node.AddChild(strong);
        node.AddChild(VNode.CreateText(" combine "));
        node.AddChild(em);
        node.AddChild(VNode.CreateText(" inline."));
        using var serviceProvider = new ServiceCollection()
            .AddRazorConsoleServices()
            .BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<WidgetTranslationContext>();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 5));

        widget.ShouldBeOfType<SpectreWidget>();
        RenderToText(result.PaintToRenderable(), maxWidth: 80).ShouldContain("Tip: combine HTML emphasis inline.");
    }

    [Fact]
    public void SpectreWidget_Layout_PreservesFigletTextAtMeasuredWidth()
    {
        var widget = new SpectreWidget(
            "figlet-1",
            new FigletText("Login Portal") { Justification = Justify.Center, Color = Color.Default });
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 80, 0, 10));
        var output = RenderToText(result.PaintToRenderable(), maxWidth: 80);

        output.ShouldContain("____                   _");
        output.ShouldContain("_ __  | |_");
    }

    [Fact]
    public void SpectreWidget_Layout_WrapsLongSegmentsToMeasuredWidth()
    {
        var widget = new SpectreWidget("markup", new Markup("alpha beta gamma"));
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 10, 0, 5));

        result.Size.ShouldBe(new LayoutSize(10, 2));
        RenderToText(result.PaintToRenderable(), maxWidth: 10).ShouldBe("alpha beta\ngamma     ");
    }

    [Fact]
    public void SpectreWidget_Layout_PreservesInlineSegmentForegroundStyles()
    {
        var widget = new SpectreWidget("markup", new SegmentedRenderable());
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 5));
        var canvas = new TerminalCanvas(result.Size.Width, result.Size.Height);
        result.Root.Paint(new PaintContext(canvas));

        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("OK NO");
        canvas[0, 0].Style?.Foreground.ShouldBe(Color.Green);
        canvas[1, 0].Style?.Foreground.ShouldBe(Color.Green);
        canvas[2, 0].Style?.Foreground.ShouldBe(Style.Plain.Foreground);
        canvas[3, 0].Style?.Foreground.ShouldBe(Color.Red);
        canvas[4, 0].Style?.Foreground.ShouldBe(Color.Red);
    }

    [Fact]
    public void LayoutResult_Renderable_RepaintsSpectreWidgetOnEveryRender()
    {
        var widget = new SpectreWidget("animated", new CountingRenderable());
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 5));
        var renderable = result.PaintToRenderable();

        RenderToText(renderable, maxWidth: 20).ShouldBe("1");
        RenderToText(renderable, maxWidth: 20).ShouldBe("2");
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesTextInputWithInlineLabelAndContent()
    {
        var node = VNode.CreateElement("div");
        node.SetAttribute("data-text-input", "true");
        node.SetAttribute("data-expand", "true");

        var panel = VNode.CreateElement("div");
        panel.SetAttribute("class", "panel");
        panel.SetAttribute("data-border", "rounded");
        panel.SetAttribute("data-expand", "true");
        panel.AddChild(CreateTextElement("Username"));

        var padder = VNode.CreateElement("div");
        padder.SetAttribute("class", "padder");
        padder.SetAttribute("data-padding", "1,0,1,0");
        padder.AddChild(CreateTextElement("Enter your username"));
        panel.AddChild(padder);
        node.AddChild(panel);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 40, 0, 5));

        widget.ShouldBeOfType<PanelWidget>();
        RenderToText(result.PaintToRenderable(), maxWidth: 40).ShouldBe(
            "╭──────────────────────────────────────╮\n" +
            "│Username Enter your username          │\n" +
            "╰──────────────────────────────────────╯");
    }

    [Fact]
    public void WidgetTranslationContext_IgnoresWhitespaceOnlyRenderTreeTextNodes()
    {
        var node = VNode.CreateText("\n    ");
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 5));

        result.Size.ShouldBe(LayoutSize.Empty);
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe(string.Empty);
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesColumnsWithDefaultSpacing()
    {
        var node = VNode.CreateElement("div");
        node.SetAttribute("class", "columns");
        node.AddChild(VNode.CreateText("\n    "));
        node.AddChild(VNode.CreateText("A"));
        node.AddChild(VNode.CreateText("\n    "));
        node.AddChild(VNode.CreateText("B"));
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 5));

        widget.ShouldBeOfType<RowWidget>();
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("A B");
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesHtmlListsWithBulletsAndNumbers()
    {
        var unordered = VNode.CreateElement("ul");
        var first = VNode.CreateElement("li");
        first.AddChild(VNode.CreateText("First"));
        var second = VNode.CreateElement("li");
        second.AddChild(VNode.CreateText("Second"));
        unordered.AddChild(first);
        unordered.AddChild(second);
        var ordered = VNode.CreateElement("ol");
        ordered.SetAttribute("start", "50");
        var numbered = VNode.CreateElement("li");
        numbered.AddChild(VNode.CreateText("Coffee"));
        ordered.AddChild(numbered);
        var context = new WidgetTranslationContext();
        var engine = new LayoutEngine();

        var unorderedResult = engine.Layout(context.Translate(unordered), new BoxConstraints(0, 20, 0, 5));
        var orderedResult = engine.Layout(context.Translate(ordered), new BoxConstraints(0, 20, 0, 5));

        RenderToText(unorderedResult.PaintToRenderable(), maxWidth: 20).ShouldBe("• First \n• Second");
        RenderToText(orderedResult.PaintToRenderable(), maxWidth: 20).ShouldBe("50. Coffee");
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesPanelWithSpectreDefaultPaddingAndExpand()
    {
        var node = VNode.CreateElement("div");
        node.SetAttribute("class", "panel");
        node.SetAttribute("data-header", "Counter");
        node.SetAttribute("data-border", "rounded");
        node.SetAttribute("data-expand", "true");
        node.AddChild(VNode.CreateText("X"));
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 10, 0, 5));
        var boxes = result.EnumerateLayoutBoxes();

        widget.ShouldBeOfType<PanelWidget>();
        result.Size.ShouldBe(new LayoutSize(10, 3));
        boxes[1].Bounds.ShouldBe(new LayoutRect(2, 1, 1, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 10).ShouldBe("╭─Counte─╮\n│ X      │\n╰────────╯");
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesTableToTableWidget()
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        using var serviceProvider = services.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<WidgetTranslationContext>();
        var table = CreateSimpleTableNode();

        var widget = context.Translate(table);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 40, 0, 10));
        var output = RenderToText(result.PaintToRenderable(), maxWidth: 40);

        widget.ShouldBeOfType<TableWidget>();
        output.ShouldContain("Name");
        output.ShouldContain("README.md");
        output.ShouldContain("╭");
    }

    [Fact]
    public void TableWidget_Layout_DrawsRoundedBorderHeaderAndRows()
    {
        var widget = new TableWidget(
            "table-1",
            [
                new TableWidgetRow(
                [
                    new TableWidgetCell("th-1", new TextWidget("text-1", "Name"), HorizontalAlignment.Center),
                    new TableWidgetCell("th-2", new TextWidget("text-2", "Type"), HorizontalAlignment.Center),
                ]),
            ],
            [
                new TableWidgetRow(
                [
                    new TableWidgetCell("td-1", new TextWidget("text-3", "README.md")),
                    new TableWidgetCell("td-2", new TextWidget("text-4", "File")),
                ]),
            ]);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 40, 0, 10));

        result.Size.ShouldBe(new LayoutSize(20, 5));
        RenderToText(result.PaintToRenderable(), maxWidth: 40).ShouldBe(
            "╭───────────┬──────╮\n" +
            "│   Name    │ Type │\n" +
            "├───────────┼──────┤\n" +
            "│ README.md │ File │\n" +
            "╰───────────┴──────╯");
    }

    [Fact]
    public void TableWidget_Layout_ExpandsToConstraintWidth()
    {
        var widget = new TableWidget(
            "table-1",
            [],
            [new TableWidgetRow([new TableWidgetCell("td-1", new TextWidget("text-1", "A"))])],
            expand: true);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 10, 0, 5));

        result.Size.ShouldBe(new LayoutSize(10, 3));
        RenderToText(result.PaintToRenderable(), maxWidth: 10).ShouldBe("╭────────╮\n│ A      │\n╰────────╯");
    }

    [Fact]
    public void StackWidget_Layout_PreservesFixedWidthTableChild()
    {
        var widget = new StackWidget(
            "stack-1",
            [
                new TextWidget("title-1", "Title"),
                new TableWidget(
                    "table-1",
                    [],
                    [new TableWidgetRow([new TableWidgetCell("td-1", new TextWidget("text-1", "A"))])],
                    width: 10),
            ]);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 30, 0, 10));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(10, 4));
        boxes.ShouldContain(box => box.VNodeId == "table-1" && box.Bounds == new LayoutRect(0, 1, 10, 3));
        RenderToText(result.PaintToRenderable(), maxWidth: 30).ShouldBe("Title     \n╭────────╮\n│ A      │\n╰────────╯");
    }

    [Fact]
    public void TableWidget_Layout_ArrangesCellChildrenInsideCellBoxes()
    {
        var widget = new TableWidget(
            "table-1",
            [],
            [new TableWidgetRow([new TableWidgetCell("td-1", new TextWidget("text-1", "A"), width: 5)])]);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 5));
        var boxes = result.EnumerateLayoutBoxes();

        boxes[0].Bounds.ShouldBe(new LayoutRect(0, 0, 7, 3));
        boxes.ShouldContain(box => box.VNodeId == "td-1" && box.Bounds == new LayoutRect(1, 1, 5, 1));
        boxes.ShouldContain(box => box.VNodeId == "text-1" && box.Bounds == new LayoutRect(2, 1, 1, 1));
    }

    [Fact]
    public void ScrollableWidget_Layout_DrawsSideScrollbar()
    {
        var widget = new ScrollableWidget(
            "scroll-1",
            new StackWidget(
                "rows-1",
                [
                    new TextWidget("text-1", "One"),
                    new TextWidget("text-2", "Two"),
                    new TextWidget("text-3", "Three"),
                ]),
            itemsCount: 5,
            offset: 0,
            pageSize: 3,
            enableEmbedded: false);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 10));

        result.Size.ShouldBe(new LayoutSize(7, 3));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("One   █\nTwo   █\nThree │");
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesScrollableWithScrollbar()
    {
        var node = VNode.CreateElement("scrollable");
        node.SetAttribute("data-items-count", "5");
        node.SetAttribute("data-offset", "0");
        node.SetAttribute("data-page-size", "3");
        node.SetAttribute("data-enable-embedded", "false");

        var scrollbar = VNode.CreateElement("div");
        scrollbar.SetAttribute("data-scrollbar", "true");
        scrollbar.SetAttribute("data-track-char", "│");
        scrollbar.SetAttribute("data-thumb-char", "█");
        scrollbar.SetAttribute("data-min-thumb-height", "1");
        node.AddChild(scrollbar);

        var rows = VNode.CreateElement("div");
        rows.SetAttribute("class", "rows");
        rows.AddChild(VNode.CreateText("One"));
        rows.AddChild(VNode.CreateText("Two"));
        rows.AddChild(VNode.CreateText("Three"));
        node.AddChild(rows);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        widget.ShouldBeOfType<ScrollableWidget>();
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("One   █\nTwo   █\nThree │");
    }

    [Fact]
    public void StackWidget_Layout_ArrangesChildrenVerticallyWithGap()
    {
        var widget = new StackWidget(
            "stack-1",
            [
                new TextWidget("text-1", "one"),
                new TextWidget("text-2", "two"),
            ],
            gap: 1);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 10));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(3, 3));
        boxes.Count.ShouldBe(3);
        boxes[0].Bounds.ShouldBe(new LayoutRect(0, 0, 3, 3));
        boxes[1].Bounds.ShouldBe(new LayoutRect(0, 0, 3, 1));
        boxes[2].Bounds.ShouldBe(new LayoutRect(0, 2, 3, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("one\n   \ntwo");
    }

    [Fact]
    public void StackWidget_Layout_ArrangesAbsoluteChildrenWithoutFlowHeight()
    {
        var widget = new StackWidget(
            "stack-1",
            [
                new TextWidget("flow", "flowing"),
                new TextWidget(
                    "overlay",
                    "top",
                    attributes: new Dictionary<string, string?>
                    {
                        ["position"] = "absolute",
                        ["top"] = "0",
                        ["left"] = "2",
                    },
                    zIndex: 10),
            ]);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 10));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(7, 1));
        boxes[1].Bounds.ShouldBe(new LayoutRect(0, 0, 7, 1));
        boxes[2].Bounds.ShouldBe(new LayoutRect(2, 0, 3, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("fltopng");
    }

    [Fact]
    public void WidgetTranslationContext_TranslatesAbsoluteElementsAsOverlays()
    {
        var root = VNode.CreateElement("div");
        root.SetAttribute("class", "rows");
        root.AddChild(VNode.CreateText("flowing"));

        var overlay = VNode.CreateElement("div");
        overlay.SetAttribute("position", "absolute");
        overlay.SetAttribute("top", "0");
        overlay.SetAttribute("left", "2");
        overlay.SetAttribute("z-index", "10");
        overlay.AddChild(VNode.CreateText("top"));
        root.AddChild(overlay);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(root);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        result.Size.ShouldBe(new LayoutSize(7, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("fltopng");
    }

    [Fact]
    public void WidgetTranslationContext_SpectreFallbackPreservesCollectedOverlays()
    {
        var root = VNode.CreateElement("div");
        root.AddChild(VNode.CreateText("flowing"));

        var overlay = VNode.CreateElement("div");
        overlay.SetAttribute("position", "absolute");
        overlay.SetAttribute("top", "0");
        overlay.SetAttribute("left", "2");
        overlay.SetAttribute("z-index", "10");
        overlay.AddChild(VNode.CreateText("top"));
        root.AddChild(overlay);
        using var serviceProvider = new ServiceCollection()
            .AddRazorConsoleServices()
            .BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<WidgetTranslationContext>();

        var widget = context.Translate(root);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 5));

        widget.ShouldBeOfType<SpectreWidget>();
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldContain("fltopng");
    }

    [Fact]
    public void LayoutResult_EnumerateLayoutInfos_IncludesResolvedZIndex()
    {
        var widget = new StackWidget(
            "stack-1",
            [new TextWidget("text-1", "one", zIndex: 5)],
            zIndex: 2);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 10), renderVersion: 3);
        var infos = result.EnumerateLayoutInfos();

        infos.Count.ShouldBe(2);
        infos[0].VNodeId.ShouldBe("stack-1");
        infos[0].ZIndex.ShouldBe(2);
        infos[1].VNodeId.ShouldBe("text-1");
        infos[1].ZIndex.ShouldBe(5);
        infos[1].Top.ShouldBe(0);
        infos[1].Left.ShouldBe(0);
        infos[1].Width.ShouldBe(3);
        infos[1].Height.ShouldBe(1);
    }

    [Fact]
    public void RowWidget_Layout_ArrangesChildrenHorizontallyWithGap()
    {
        var widget = new RowWidget(
            "row-1",
            [
                new TextWidget("text-1", "one"),
                new TextWidget("text-2", "two"),
            ],
            gap: 2);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 5));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(8, 1));
        boxes[1].Bounds.ShouldBe(new LayoutRect(0, 0, 3, 1));
        boxes[2].Bounds.ShouldBe(new LayoutRect(5, 0, 3, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("one  two");
    }

    [Fact]
    public void PaddingWidget_Layout_OffsetsChildAndAddsBlankSpace()
    {
        var widget = new PaddingWidget("padder-1", new TextWidget("text-1", "x"), left: 2, top: 1, right: 1, bottom: 1);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 5));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(4, 3));
        boxes[0].Bounds.ShouldBe(new LayoutRect(0, 0, 4, 3));
        boxes[1].Bounds.ShouldBe(new LayoutRect(2, 1, 1, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("    \n  x \n    ");
    }

    [Fact]
    public void AlignWidget_Layout_CentersChildWithinExplicitArea()
    {
        var widget = new AlignWidget(
            "align-1",
            new TextWidget("text-1", "x"),
            horizontal: HorizontalAlignment.Center,
            vertical: VerticalAlignment.Middle,
            width: 5,
            height: 3);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 5));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(5, 3));
        boxes[1].Bounds.ShouldBe(new LayoutRect(2, 1, 1, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("     \n  x  \n     ");
    }

    [Fact]
    public void AlignWidget_Layout_CentersChildAcrossAvailableWidthByDefault()
    {
        var widget = new AlignWidget(
            "align-1",
            new TextWidget("text-1", "hi"),
            horizontal: HorizontalAlignment.Center);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 3));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(20, 1));
        boxes[1].Bounds.ShouldBe(new LayoutRect(9, 0, 2, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("         hi         ");
    }

    [Fact]
    public void AlignWidget_Layout_CentersWrappedTextLines()
    {
        var widget = new AlignWidget(
            "align-1",
            new TextWidget("text-1", "alpha beta gamma"),
            horizontal: HorizontalAlignment.Center,
            width: 10);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 10, 0, 5));

        result.Size.ShouldBe(new LayoutSize(10, 2));
        RenderToText(result.PaintToRenderable(), maxWidth: 10).ShouldBe("alpha beta\n  gamma   ");
    }

    [Fact]
    public void TextWidget_Paint_DoesNotOverflowArrangedBounds()
    {
        var widget = new RowWidget(
            "row-1",
            [
                new TextWidget("text-1", "hello"),
                new TextWidget("text-2", "z"),
            ],
            gap: 0);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 4, 0, 1));

        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("hell");
    }

    [Fact]
    public void PanelWidget_Layout_DrawsSquareBorderAndArrangesChildInside()
    {
        var widget = new PanelWidget("panel-1", new TextWidget("text-1", "x"));
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 10));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(3, 3));
        boxes[0].Bounds.ShouldBe(new LayoutRect(0, 0, 3, 3));
        boxes[1].Bounds.ShouldBe(new LayoutRect(1, 1, 1, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("┌─┐\n│x│\n└─┘");
    }

    [Fact]
    public void PanelWidget_Layout_AppliesPaddingAndTitle()
    {
        var widget = new PanelWidget(
            "panel-1",
            new TextWidget("text-1", "x"),
            title: "Hi",
            paddingLeft: 1,
            paddingTop: 1,
            paddingRight: 1,
            paddingBottom: 1);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 10));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(6, 5));
        boxes[1].Bounds.ShouldBe(new LayoutRect(2, 2, 1, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe("┌─Hi─┐\n│    │\n│ x  │\n│    │\n└────┘");
    }

    [Fact]
    public void PanelWidget_Layout_ExpandedPanelPreservesChildDesiredWidth()
    {
        var widget = new PanelWidget(
            "outer",
            new PanelWidget("inner", new TextWidget("text", "content"), title: "Inner"),
            title: "Outer",
            expand: true);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 30, 0, 10));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.Width.ShouldBe(30);
        boxes[1].Bounds.ShouldBe(new LayoutRect(1, 1, 9, 3));
        RenderToText(result.PaintToRenderable(), maxWidth: 30).ShouldStartWith("┌─Outer");
        RenderToText(result.PaintToRenderable(), maxWidth: 30).ShouldContain("│┌─Inner─┐");
    }

    [Fact]
    public void PanelWidget_Layout_WithoutBorder_UsesPaddingOnly()
    {
        var widget = new PanelWidget(
            "panel-1",
            new TextWidget("text-1", "x"),
            border: PanelBorderStyle.None,
            paddingLeft: 1,
            paddingRight: 1);
        var engine = new LayoutEngine();

        var result = engine.Layout(widget, new BoxConstraints(0, 20, 0, 10));
        var boxes = result.EnumerateLayoutBoxes();

        result.Size.ShouldBe(new LayoutSize(3, 1));
        boxes[1].Bounds.ShouldBe(new LayoutRect(1, 0, 1, 1));
        RenderToText(result.PaintToRenderable(), maxWidth: 20).ShouldBe(" x ");
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

    private sealed class SegmentedRenderable : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth)
            => new(5, 5);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            yield return new Segment("OK", new Style(Color.Green));
            yield return new Segment(" ");
            yield return new Segment("NO", new Style(Color.Red));
        }
    }

    private sealed class CountingRenderable : IRenderable
    {
        private int _renderCount;

        public Measurement Measure(RenderOptions options, int maxWidth)
            => new(1, 1);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            yield return new Segment((_renderCount++).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static VNode CreateSimpleTableNode()
    {
        var table = VNode.CreateElement("table");
        table.SetAttribute("data-border", "rounded");
        table.SetAttribute("data-expand", "true");

        var head = VNode.CreateElement("thead");
        var headerRow = VNode.CreateElement("tr");
        var headerCell = VNode.CreateElement("th");
        headerCell.SetAttribute("data-align", "left");
        headerCell.AddChild(CreateTextElement("Name"));
        headerRow.AddChild(headerCell);
        head.AddChild(headerRow);

        var body = VNode.CreateElement("tbody");
        var bodyRow = VNode.CreateElement("tr");
        var bodyCell = VNode.CreateElement("td");
        bodyCell.AddChild(CreateTextElement("README.md"));
        bodyRow.AddChild(bodyCell);
        body.AddChild(bodyRow);

        table.AddChild(head);
        table.AddChild(body);
        return table;
    }

    private static VNode CreateTextElement(string content)
    {
        var node = VNode.CreateElement("span");
        node.SetAttribute("data-text", "true");
        node.SetAttribute("data-content", content);
        return node;
    }
}
