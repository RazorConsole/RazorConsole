// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

/// <summary>
/// Covers translation and layout of HTML <c>&lt;ul&gt;</c>/<c>&lt;ol&gt;</c>/<c>&lt;li&gt;</c>
/// elements, including the regression where styled or nested <c>&lt;li&gt;</c> content was
/// silently dropped (or lost its styling) because it was flattened to plain text instead of
/// being translated through the normal widget pipeline.
/// </summary>
public sealed class HtmlListWidgetTests
{
    [Fact]
    public void UnorderedList_WithPlainTextItems_RendersBullets()
    {
        var node = CreateList("ul", CreateTextItem("Alpha"), CreateTextItem("Beta"));
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 5));

        RenderToText(result.PaintToRenderable(), 20).ShouldBe("• Alpha\n• Beta ");
    }

    [Fact]
    public void OrderedList_NumbersItemsAndHonorsStartAttribute()
    {
        var node = CreateList("ol", CreateTextItem("Alpha"), CreateTextItem("Beta"), CreateTextItem("Gamma"));
        node.SetAttribute("start", "5");
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 5));

        RenderToText(result.PaintToRenderable(), 20).ShouldBe("5. Alpha\n6. Beta \n7. Gamma");
    }

    [Fact]
    public void EmptyList_ProducesNoRowsAndZeroHeight()
    {
        var node = CreateList("ul");
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 5));

        widget.ShouldBeOfType<StackWidget>();
        widget.Children.ShouldBeEmpty();
        result.Size.Height.ShouldBe(0);
    }

    [Fact]
    public void ListItem_WithStyledMarkupSpan_PreservesTextAndStyle()
    {
        // This mirrors what the <Markup Foreground="..."> component compiles to:
        // a <span data-text="true" data-style="..." data-content="..." /> element.
        var li = VNode.CreateElement("li");
        var span = VNode.CreateElement("span");
        span.SetAttribute("data-text", "true");
        span.SetAttribute("data-style", "red");
        span.SetAttribute("data-content", "Styled item");
        li.AddChild(span);
        var node = CreateList("ul", li);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);

        var listItem = widget.Children.ShouldHaveSingleItem().ShouldBeOfType<HtmlListItemWidget>();
        var content = listItem.Content.ShouldBeOfType<TextWidget>();
        content.Text.ShouldBe("Styled item");
        content.Style.ShouldNotBeNull();
        content.Style!.Foreground.ShouldBe(Color.Red);

        // Text must not have been silently dropped when rendered end-to-end either.
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 40, 0, 5));
        RenderToText(result.PaintToRenderable(), 40).ShouldBe("• Styled item");
    }

    [Fact]
    public void NestedList_IndentsUnderParentItem()
    {
        var childLi = CreateTextItem("Child");
        var nestedList = CreateList("ul", childLi);
        var parentLi = VNode.CreateElement("li");
        parentLi.AddChild(VNode.CreateText("Parent"));
        parentLi.AddChild(nestedList);
        var node = CreateList("ul", parentLi);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 5));

        var lines = RenderToText(result.PaintToRenderable(), 20).Split('\n').Select(l => l.TrimEnd()).ToArray();
        lines.ShouldBe(["• Parent", "  • Child"]);
    }

    [Fact]
    public void ListItem_WithMultilineText_MeasuresAllLines()
    {
        var li = VNode.CreateElement("li");
        li.AddChild(VNode.CreateText("Line one\nLine two\nLine three"));
        var node = CreateList("ul", li);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 20, 0, 10));

        result.Size.Height.ShouldBe(3);
        var lines = RenderToText(result.PaintToRenderable(), 20).Split('\n').Select(l => l.TrimEnd()).ToArray();
        lines.ShouldBe(["• Line one", "  Line two", "  Line three"]);
    }

    [Fact]
    public void Scrollable_WithStyledOrderedListItems_RendersAllItemsAcrossOffsets()
    {
        var items = Enumerable.Range(1, 6)
            .Select(i =>
            {
                var li = VNode.CreateElement("li");
                var span = VNode.CreateElement("span");
                span.SetAttribute("data-text", "true");
                span.SetAttribute("data-style", "green");
                span.SetAttribute("data-content", $"Item {i}");
                li.AddChild(span);
                return li;
            })
            .ToArray();
        var node = CreateList("ol", items);
        var context = new WidgetTranslationContext();
        var listWidget = context.Translate(node);

        for (var offset = 0; offset <= 3; offset++)
        {
            var scrollable = new ScrollableWidget(
                "scroll-1",
                listWidget,
                itemsCount: 6,
                offset: offset,
                pageSize: 3,
                enableEmbedded: false,
                showScrollbar: false,
                cropLines: true,
                autoPageSize: false);
            var engine = new LayoutEngine();

            var result = engine.Layout(scrollable, new BoxConstraints(0, 20, 0, 3));
            var text = RenderToText(result.PaintToRenderable(), 20);
            var visibleLines = text.Split('\n');

            visibleLines.Length.ShouldBe(3);
            for (var row = 0; row < 3; row++)
            {
                var expectedItem = offset + row + 1;
                visibleLines[row].TrimEnd().ShouldBe($"{expectedItem}. Item {expectedItem}");
            }
        }
    }

    private static VNode CreateList(string tagName, params VNode[] items)
    {
        var list = VNode.CreateElement(tagName);
        foreach (var item in items)
        {
            list.AddChild(item);
        }

        return list;
    }

    private static VNode CreateTextItem(string text)
    {
        var li = VNode.CreateElement("li");
        li.AddChild(VNode.CreateText(text));
        return li;
    }

    private static string RenderToText(IRenderable renderable, int maxWidth)
    {
        var options = CreateRenderOptions(maxWidth, 25);
        var segments = renderable.Render(options, maxWidth);
        return string.Concat(segments.Select(segment => segment.IsLineBreak ? "\n" : segment.Text));
    }

    private static RenderOptions CreateRenderOptions(int maxWidth, int height)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        return new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(maxWidth, height));
    }
}
