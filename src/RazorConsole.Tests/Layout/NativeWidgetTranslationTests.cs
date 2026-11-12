// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Layout.Leaves;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

/// <summary>
/// Covers nodes that WidgetLayout used to hand to the legacy Spectre translators.
/// </summary>
public sealed class NativeWidgetTranslationTests
{
    [Fact]
    public void FlexBox_TranslatesToFlexWidget()
    {
        var node = Element("div", ("class", "flexbox"), ("data-layout", "flex"), ("data-gap", "1"));
        node.AddChild(Text("a"));
        node.AddChild(Text("b"));

        var widget = new WidgetTranslationContext().Translate(node);

        widget.ShouldBeOfType<FlexWidget>();
        Render(widget, 10).ShouldBe("a b");
    }

    [Fact]
    public void Grid_FillsRowsLeftToRight_WithTwoSpaceColumnGap()
    {
        var node = Element("div", ("class", "grid"), ("data-columns", "2"), ("data-expand", "false"));
        foreach (var text in new[] { "Time", "Topic", "09:00", "Standup", "10:30" })
        {
            node.AddChild(Element("span", ("data-text", "true"), ("data-content", text)));
        }

        var widget = new WidgetTranslationContext().Translate(node);

        widget.ShouldBeOfType<TableWidget>();
        Render(widget, 30).ShouldBe("Time   Topic\n09:00  Standup\n10:30");
    }

    [Fact]
    public void Paragraph_FlowsInlineFormattingAsOneRun_CollapsingWhitespace()
    {
        var paragraph = Element("p");
        paragraph.AddChild(Text("\n    Hello "));
        var strong = Element("strong");
        strong.AddChild(Text("bold"));
        paragraph.AddChild(strong);
        paragraph.AddChild(Text(" and   "));
        var code = Element("code");
        code.AddChild(Text("[x]"));
        paragraph.AddChild(code);
        paragraph.AddChild(Text(" text.\n"));

        var widget = new WidgetTranslationContext().Translate(paragraph);

        widget.ShouldBeOfType<SpectreWidget>();
        Render(widget, 40).ShouldBe("Hello bold and [x] text.");
    }

    [Fact]
    public void Anchor_WithHref_RendersAsLink()
    {
        var anchor = Element("a", ("href", "https://example.com"));
        anchor.AddChild(Text("Click here"));

        InlineMarkupBuilder.TryBuildMarkup(anchor, out var markup).ShouldBeTrue();

        markup.ShouldBe("[link=https://example.com]Click here[/]");
        Render(new WidgetTranslationContext().Translate(anchor), 20).ShouldBe("Click here");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Anchor_WithoutHref_RendersPlainText(string? href)
    {
        var anchor = href is null ? Element("a") : Element("a", ("href", href));
        anchor.AddChild(Text("Not a link"));

        InlineMarkupBuilder.TryBuildMarkup(anchor, out var markup).ShouldBeTrue();

        markup.ShouldBe("Not a link");
        Render(new WidgetTranslationContext().Translate(anchor), 20).ShouldBe("Not a link");
    }

    [Fact]
    public void Anchor_EscapesMarkupCharactersInHref()
    {
        var anchor = Element("a", ("href", "https://example.com/path?q=[test]"));
        anchor.AddChild(Text("Link"));

        InlineMarkupBuilder.TryBuildMarkup(anchor, out var markup).ShouldBeTrue();

        markup.ShouldBe("[link=https://example.com/path?q=[[test]]]Link[/]");
        Render(new WidgetTranslationContext().Translate(anchor), 20).ShouldBe("Link");
    }

    [Fact]
    public void Anchor_ComposesNestedFormatting()
    {
        var anchor = Element("a", ("href", "https://example.com"));
        anchor.AddChild(Text("Click "));
        var strong = Element("strong");
        strong.AddChild(Text("here"));
        anchor.AddChild(strong);

        InlineMarkupBuilder.TryBuildMarkup(anchor, out var markup).ShouldBeTrue();

        markup.ShouldBe("[link=https://example.com]Click [bold]here[/][/]");
    }

    [Fact]
    public void Code_StylesTextAndEscapesMarkup()
    {
        var code = Element("code");
        code.AddChild(Text("var value = [42];"));

        InlineMarkupBuilder.TryBuildMarkup(code, out var markup).ShouldBeTrue();

        markup.ShouldBe("[indianred1 on #1f1f1f]var value = [[42]];[/]");
        Render(new WidgetTranslationContext().Translate(code), 30).ShouldBe("var value = [42];");
    }

    [Fact]
    public void Code_DisablesNestedFormatting()
    {
        var code = Element("code");
        code.AddChild(Text("var "));
        var strong = Element("strong");
        strong.AddChild(Text("value"));
        code.AddChild(strong);

        InlineMarkupBuilder.TryBuildMarkup(code, out _).ShouldBeFalse();
        InlineMarkupBuilder.TryBuildFlowMarkup([code], out _).ShouldBeFalse();
    }

    [Fact]
    public void Paragraph_WrapsLongTextAtWordBoundaries()
    {
        var paragraph = Element("p");
        paragraph.AddChild(Text("one two three four"));

        var widget = new WidgetTranslationContext().Translate(paragraph);

        Render(widget, 9).ShouldBe("one two\nthree\nfour");
    }

    [Fact]
    public void PlainDivWithInlineElements_FlowsOnOneLine()
    {
        var div = Element("div");
        div.AddChild(Text("Quote with "));
        var quote = Element("q");
        quote.AddChild(Text("citation"));
        div.AddChild(quote);
        div.AddChild(Text("."));

        var widget = new WidgetTranslationContext().Translate(div);

        Render(widget, 40).ShouldBe("Quote with “citation”.");
    }

    [Fact]
    public void ParagraphWithComponentChild_KeepsChildrenSideBySide()
    {
        var paragraph = Element("p");
        paragraph.AddChild(Element("span", ("data-text", "true"), ("data-content", "left")));
        paragraph.AddChild(Element("span", ("data-text", "true"), ("data-content", "right")));

        var widget = new WidgetTranslationContext().Translate(paragraph);

        widget.ShouldBeOfType<FlexWidget>();
        Render(widget, 20).ShouldBe("leftright");
    }

    [Fact]
    public void Spinner_IsALeafAndRegistersItsAnimation()
    {
        var node = Element("div", ("class", "spinner"), ("data-spinner", "true"), ("data-message", "Loading"));
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);

        widget.ShouldBeOfType<SpectreWidget>();
        context.AnimatedRenderables.Count.ShouldBe(1);
        Render(widget, 20).ShouldContain("Loading");
    }

    [Fact]
    public void BarChart_IsALeaf()
    {
        var node = Element(
            "barchart",
            ("data-items", "[{\"Label\":\"Apples\",\"Value\":5}]"),
            ("data-width", "20"),
            ("data-culture", "en-US"));

        var widget = new WidgetTranslationContext().Translate(node);

        widget.ShouldBeOfType<SpectreWidget>();
        Render(widget, 20).ShouldContain("Apples");
    }

    [Theory]
    [InlineData("data-color", "notacolor")]
    [InlineData("data-axes-color", "notacolor")]
    [InlineData("data-width", "2")]
    public void StepChart_WithBadAttribute_FallsBackInsteadOfThrowing(string name, string value)
    {
        var node = Element("stepchart", ("data-show-axes", "true"), ("data-width", "20"), ("data-height", "6"));
        var series = Element("chartseries", ("data-points", "0,1;1,2;2,3"));
        node.AddChild(series);
        (name == "data-color" ? series : node).SetAttribute(name, value);

        var widget = Should.NotThrow(() => new WidgetTranslationContext().Translate(node));

        widget.ShouldNotBeOfType<SpectreWidget>();
        Should.NotThrow(() => Render(widget, 20));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0")]
    public void Canvas_WithMissingOrInvalidSize_FallsBackInsteadOfThrowing(string? width)
    {
        var node = Element("div", ("data-canvas", "true"), ("data-height", "4"), ("data-canvas-data-id", Guid.NewGuid().ToString()));
        if (width is not null)
        {
            node.SetAttribute("data-width", width);
        }

        var widget = Should.NotThrow(() => new WidgetTranslationContext().Translate(node));

        widget.ShouldNotBeOfType<SpectreWidget>();
    }

    [Fact]
    public void Paragraph_FlattensNestedInlineElements_DroppingTheirClassesAndHandlers()
    {
        var paragraph = Element("p");
        paragraph.AddChild(Text("Press "));
        var link = Element("a", ("href", "https://example.com"), ("data-focus-key", "link"), (IVNodeIdAccessor.HookAttributeName, "1"));
        link.AddChild(Text("here"));
        paragraph.AddChild(link);
        paragraph.AddChild(Text(" or "));
        var strong = Element("strong", ("class", "x"));
        strong.AddChild(Text("there"));
        paragraph.AddChild(strong);

        var widget = new WidgetTranslationContext().Translate(paragraph);

        // One leaf for the whole run: the nested elements get no widgets, bounds or focus of their own.
        widget.ShouldBeOfType<SpectreWidget>();
        widget.Children.ShouldBeEmpty();
        Render(widget, 40).ShouldBe("Press here or there");
    }

    [Fact]
    public void FlexWidget_Wrap_FixedWidthWiderThanConstraint_MeasuresTheArrangedLines()
    {
        var widget = new FlexWidget(
            "flex",
            [new TextWidget("a", "aaaa"), new TextWidget("b", "bbbb"), new TextWidget("c", "cccc")],
            wrap: FlexWrap.Wrap,
            width: 12);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 8, 0, 10));

        result.Size.ShouldBe(new LayoutSize(8, 2));
        RenderText(result, 8).ShouldBe("aaaabbbb\ncccc");
    }

    [Fact]
    public void FlexWidget_Wrap_FillWidth_MeasuresTheArrangedLines()
    {
        var widget = new FlexWidget(
            "flex",
            [new TextWidget("a", "aaaa"), new TextWidget("b", "bbbb"), new TextWidget("c", "cccc")],
            wrap: FlexWrap.Wrap,
            gap: 1,
            fillWidth: true);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 10, 0, 10));

        result.Size.ShouldBe(new LayoutSize(10, 2));
        RenderText(result, 10).ShouldBe("aaaa bbbb\ncccc");
    }

    [Fact]
    public void FlexWidget_WrapsRowItemsOntoNewLines()
    {
        var widget = new FlexWidget(
            "flex",
            [new TextWidget("a", "aaaa"), new TextWidget("b", "bbbb"), new TextWidget("c", "cccc")],
            wrap: FlexWrap.Wrap,
            gap: 1);

        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 10, 0, 10));

        result.Size.ShouldBe(new LayoutSize(9, 2));
        RenderText(result, 10).ShouldBe("aaaa bbbb\ncccc");
    }

    [Fact]
    public void TableWidget_RewrapsCellContentNarrowerThanItsPreferredWidth()
    {
        var cell = new TableWidgetCell("cell", new SpectreWidget("text", new Markup("alpha beta gamma")), paddingLeft: 0, paddingRight: 0);
        var table = new TableWidget("table", [], [new TableWidgetRow([cell])], TableWidgetBorderStyle.None, showHeaders: false, width: 10);

        var result = new LayoutEngine().Layout(table, new BoxConstraints(0, 20, 0, 10));

        result.Size.Height.ShouldBe(2);
        RenderText(result, 20).ShouldBe("alpha beta\ngamma");
    }

    [Fact]
    public void BoxWidget_WithTitleAndNoBorder_KeepsATitleRow()
    {
        var box = new BoxWidget("box", new TextWidget("body", "content"), title: "Title");

        var result = new LayoutEngine().Layout(box, new BoxConstraints(0, 20, 0, 10));

        RenderText(result, 20).ShouldBe("  Title\ncontent");
    }

    private static VNode Element(string tag, params (string Name, string Value)[] attributes)
    {
        var node = VNode.CreateElement(tag);
        foreach (var (name, value) in attributes)
        {
            node.SetAttribute(name, value);
        }

        return node;
    }

    private static VNode Text(string value) => VNode.CreateText(value);

    private static string Render(Widget widget, int width)
        => RenderText(new LayoutEngine().Layout(widget, new BoxConstraints(0, width, 0, 20)), width);

    private static string RenderText(LayoutResult result, int width)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });
        var options = new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(width, 25));
        var text = string.Concat(result.PaintToRenderable().Render(options, width).Select(segment => segment.IsLineBreak ? "\n" : segment.Text));
        return string.Join("\n", text.Split('\n').Select(line => line.TrimEnd())).TrimEnd('\n');
    }
}
