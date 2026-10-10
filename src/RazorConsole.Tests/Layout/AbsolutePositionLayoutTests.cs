// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

public sealed class AbsolutePositionLayoutTests
{
    [Fact]
    public void Absolute_InsideNestedContainer_IsPlacedRelativeToDocumentRoot()
    {
        var widget = new StackWidget(
            "root",
            [
                new TextWidget("row0", "0123456789"),
                new BoxWidget(
                    "box",
                    new StackWidget(
                        "inner",
                        [
                            new TextWidget("flow", "flow"),
                            Absolute("abs", "X", top: 0, left: 0),
                        ]),
                    paddingLeft: 4,
                    paddingTop: 1),
            ]);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        Bounds(result, "abs").ShouldBe(new LayoutRect(0, 0, 1, 1));
        RenderToText(result, 20).Split('\n')[0].ShouldBe("X123456789");
    }

    [Fact]
    public void Absolute_InsideFlexColumn_IsPlacedRelativeToDocumentRoot()
    {
        var widget = new StackWidget(
            "root",
            [
                new TextWidget("row0", "0123456789"),
                new TextWidget("row1", "abcdefghij"),
                new FlexWidget(
                    "flex",
                    [
                        new TextWidget("flow", "flow"),
                        Absolute("abs", "X", top: 1, left: 2),
                    ],
                    direction: FlexDirection.Column),
            ]);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        Bounds(result, "abs").ShouldBe(new LayoutRect(2, 1, 1, 1));
        RenderToText(result, 20).Split('\n')[1].ShouldBe("abXdefghij");
    }

    [Fact]
    public void Absolute_InsideAbsolute_UsesCumulativeOffsets()
    {
        var widget = new StackWidget(
            "root",
            [
                new TextWidget("row0", "0123456789"),
                new BoxWidget(
                    "box",
                    new StackWidget(
                        "parent",
                        [
                            new TextWidget("parent-text", "P"),
                            Absolute("child", "C", top: 1, left: 2),
                        ],
                        attributes: AbsoluteAttributes(top: 2, left: 3)),
                    paddingLeft: 6,
                    paddingTop: 1),
            ]);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        Bounds(result, "parent").ShouldBe(new LayoutRect(3, 2, 1, 1));
        Bounds(result, "child").ShouldBe(new LayoutRect(5, 3, 1, 1));
        RenderToText(result, 20).Split('\n')[3].ShouldBe("     C    ");
    }

    [Fact]
    public void Absolute_BottomRight_AreRelativeToWholeDocument()
    {
        var widget = new StackWidget(
            "root",
            [
                new TextWidget("row0", "0123456789"),
                new TextWidget("row1", "abcdefghij"),
                new BoxWidget(
                    "box",
                    new StackWidget(
                        "inner",
                        [
                            new TextWidget("flow", "flow"),
                            Absolute("abs", "Z", right: 1, bottom: 0),
                        ]),
                    paddingLeft: 3),
            ]);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        // The document is 20 wide (viewport) and 3 tall (flow content); inner bounds must not matter.
        Bounds(result, "abs").ShouldBe(new LayoutRect(18, 2, 1, 1));
    }

    [Fact]
    public void Absolute_ZIndex_OrdersOverlaysAcrossContainers()
    {
        var widget = new StackWidget(
            "root",
            [
                new StackWidget(
                    "c1",
                    [
                        new TextWidget("flow1", "flow"),
                        Absolute("high", "HIGH", top: 1, left: 0, zIndex: 5),
                    ]),
                new StackWidget(
                    "c2",
                    [
                        new TextWidget("flow2", "xxxxxxxx"),
                        Absolute("low", "LOW", top: 1, left: 0, zIndex: 1),
                    ]),
            ]);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        // The later sibling container must not paint over a higher z-index overlay from an earlier one.
        RenderToText(result, 20).ShouldBe("flow    \nHIGHxxxx");
    }

    [Fact]
    public void Absolute_OutsideBackground_ExpandsCanvas()
    {
        var widget = new StackWidget(
            "root",
            [
                new TextWidget("row0", "ab"),
                Absolute("abs", "Z", top: 3, left: 5),
            ]);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        result.Size.ShouldBe(new LayoutSize(6, 4));
        RenderToText(result, 20).ShouldBe("ab    \n      \n      \n     Z");
    }

    [Fact]
    public void Absolute_InsideScrollableClip_IsNotClipped()
    {
        var widget = new StackWidget(
            "root",
            [
                new TextWidget("row0", "0123456789"),
                new ScrollableWidget(
                    "scroll",
                    new StackWidget(
                        "content",
                        [
                            new TextWidget("line", "line"),
                            Absolute("abs", "X", top: 0, left: 8),
                        ]),
                    itemsCount: 1,
                    offset: 0,
                    pageSize: 1,
                    enableEmbedded: false,
                    showScrollbar: false,
                    cropLines: true),
            ]);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        RenderToText(result, 20).Split('\n')[0].ShouldBe("01234567X9");
    }

    [Fact]
    public void WidgetTranslationContext_NestedAbsolute_IsPlacedRelativeToDocumentRoot()
    {
        var root = VNode.CreateElement("div");
        root.SetAttribute("class", "rows");
        root.AddChild(VNode.CreateText("0123456789"));

        var nested = VNode.CreateElement("div");
        nested.SetAttribute("class", "rows");
        nested.AddChild(VNode.CreateText("flow"));

        var overlay = VNode.CreateElement("div");
        overlay.SetAttribute("position", "absolute");
        overlay.SetAttribute("top", "0");
        overlay.SetAttribute("left", "3");
        overlay.AddChild(VNode.CreateText("X"));
        nested.AddChild(overlay);
        root.AddChild(nested);

        var widget = new WidgetTranslationContext().Translate(root);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        RenderToText(result, 20).Split('\n')[0].ShouldBe("012X456789");
    }

    [Fact]
    public void WidgetTranslationContext_Modal_IsCenteredInDocumentAndAboveOverlays()
    {
        var root = VNode.CreateElement("div");
        root.SetAttribute("class", "rows");
        root.AddChild(VNode.CreateText("0123456789"));
        root.AddChild(VNode.CreateText("abcdefghij"));
        root.AddChild(VNode.CreateText("klmnopqrst"));

        var nested = VNode.CreateElement("div");
        nested.SetAttribute("class", "rows");
        nested.AddChild(VNode.CreateText("flow"));
        var modal = VNode.CreateElement("modal");
        modal.AddChild(VNode.CreateText("MM"));
        nested.AddChild(modal);
        root.AddChild(nested);

        var widget = new WidgetTranslationContext().Translate(root);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 10, 0, 10));

        // Document is 10x4: a 2x1 modal centers at x=4, y=1 no matter which container holds it.
        RenderToText(result, 10).Split('\n')[1].ShouldBe("abcdMMghij");
    }

    [Fact]
    public void WidgetTranslationContext_Modal_HidesContentBeneathIt()
    {
        var root = VNode.CreateElement("div");
        root.SetAttribute("class", "rows");
        root.AddChild(VNode.CreateText("0123456789"));
        root.AddChild(VNode.CreateText("abcdefghij"));
        root.AddChild(VNode.CreateText("klmnopqrst"));
        root.AddChild(VNode.CreateText("uvwxyz"));
        var modal = VNode.CreateElement("modal");
        modal.AddChild(VNode.CreateText("MMMM"));
        modal.AddChild(VNode.CreateText("M"));
        root.AddChild(modal);

        var widget = new WidgetTranslationContext().Translate(root);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 10, 0, 10));

        // A 4x2 modal centered in a 10x4 document covers x=3..6 on rows 1 and 2.
        var lines = RenderToText(result, 10).Split('\n');
        lines[1].ShouldBe("abcMMMMhij");
        lines[2].ShouldBe("klmM   rst");
    }

    private static TextWidget Absolute(string id, string text, int? top = null, int? left = null, int? right = null, int? bottom = null, int zIndex = 0)
        => new(id, text, attributes: AbsoluteAttributes(top, left, right, bottom), zIndex: zIndex);

    private static Dictionary<string, string?> AbsoluteAttributes(int? top = null, int? left = null, int? right = null, int? bottom = null)
    {
        var attributes = new Dictionary<string, string?> { ["position"] = "absolute" };
        if (top.HasValue)
        {
            attributes["top"] = top.Value.ToString();
        }

        if (left.HasValue)
        {
            attributes["left"] = left.Value.ToString();
        }

        if (right.HasValue)
        {
            attributes["right"] = right.Value.ToString();
        }

        if (bottom.HasValue)
        {
            attributes["bottom"] = bottom.Value.ToString();
        }

        return attributes;
    }

    private static LayoutRect Bounds(LayoutResult result, string id)
        => result.EnumerateLayoutBoxes().Single(box => box.VNodeId == id).Bounds;

    private static string RenderToText(LayoutResult result, int maxWidth)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });
        var options = new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(maxWidth, 25));
        var segments = result.PaintToRenderable().Render(options, maxWidth);
        return string.Concat(segments.Select(segment => segment.IsLineBreak ? "\n" : segment.Text));
    }
}
