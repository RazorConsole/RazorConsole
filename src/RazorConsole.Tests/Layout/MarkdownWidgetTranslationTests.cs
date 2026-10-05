// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

public sealed class MarkdownWidgetTranslationTests
{
    [Fact]
    public void Heading_TranslatesToStyledTextWidgetWithMarkdownPrefix()
    {
        var node = VNode.CreateElement("h1");
        node.AddChild(VNode.CreateText("Hello World"));
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 5));
        var canvas = new TerminalCanvas(result.Size.Width, result.Size.Height);
        result.Root.Paint(new PaintContext(canvas));

        widget.ShouldBeOfType<TextWidget>();
        RenderToText(result.PaintToRenderable(), 80).ShouldBe("# Hello World");
        canvas[0, 0].Style?.Foreground.ShouldBe(Color.Yellow);
        canvas[0, 0].Style?.Decoration.ShouldBe(Decoration.Bold);
    }

    [Fact]
    public void Heading_Level3_UsesLevel3PrefixAndStyle()
    {
        var node = VNode.CreateElement("h3");
        node.AddChild(VNode.CreateText("Section"));
        var context = new WidgetTranslationContext();

        var widget = context.Translate(node);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 5));
        var canvas = new TerminalCanvas(result.Size.Width, result.Size.Height);
        result.Root.Paint(new PaintContext(canvas));

        RenderToText(result.PaintToRenderable(), 80).ShouldBe("### Section");
        canvas[0, 0].Style?.Foreground.ShouldBe(Color.Green);
    }

    [Fact]
    public void CodeBlock_TranslatesToSyntaxHighlightedSpectreWidget()
    {
        var pre = VNode.CreateElement("pre");
        var code = VNode.CreateElement("code");
        code.SetAttribute("class", "language-csharp");
        code.AddChild(VNode.CreateText("Console.WriteLine(\"hi\");"));
        pre.AddChild(code);

        using var serviceProvider = new ServiceCollection().AddRazorConsoleServices().BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<WidgetTranslationContext>();

        var widget = context.Translate(pre);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 5));

        widget.ShouldBeOfType<SpectreWidget>();
        RenderToText(result.PaintToRenderable(), 80).ShouldContain("Console.WriteLine");
    }

    [Fact]
    public void CodeBlock_WithoutSyntaxService_FallsBackToPlainText()
    {
        var pre = VNode.CreateElement("pre");
        var code = VNode.CreateElement("code");
        code.SetAttribute("class", "language-csharp");
        code.AddChild(VNode.CreateText("plain"));
        pre.AddChild(code);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(pre);

        widget.ShouldBeOfType<TextWidget>();
        ((TextWidget)widget).Text.ShouldBe("plain");
    }

    [Fact]
    public void Blockquote_IndentsSingleParagraphWithoutBorder()
    {
        var blockquote = VNode.CreateElement("blockquote");
        var paragraph = VNode.CreateElement("p");
        paragraph.AddChild(VNode.CreateText("quoted text"));
        blockquote.AddChild(paragraph);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(blockquote);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 5));

        widget.ShouldBeOfType<BoxWidget>();
        ((BoxWidget)widget).PaddingLeft.ShouldBe(2);
        ((BoxWidget)widget).Border.Left.ShouldBe(BoxBorderStyle.None);
        RenderToText(result.PaintToRenderable(), 80).TrimEnd().ShouldBe("  quoted text");
    }

    [Fact]
    public void Blockquote_StacksMultipleParagraphsVertically()
    {
        var blockquote = VNode.CreateElement("blockquote");
        var first = VNode.CreateElement("p");
        first.AddChild(VNode.CreateText("first"));
        var second = VNode.CreateElement("p");
        second.AddChild(VNode.CreateText("second"));
        blockquote.AddChild(first);
        blockquote.AddChild(second);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(blockquote);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 5));
        var lines = RenderToText(result.PaintToRenderable(), 80).Split('\n').Select(l => l.TrimEnd()).ToArray();

        lines.ShouldContain("  first");
        lines.ShouldContain("  second");
    }

    [Fact]
    public void Blockquote_NestedQuote_DoublesIndent()
    {
        var outer = VNode.CreateElement("blockquote");
        var inner = VNode.CreateElement("blockquote");
        var paragraph = VNode.CreateElement("p");
        paragraph.AddChild(VNode.CreateText("nested"));
        inner.AddChild(paragraph);
        outer.AddChild(inner);
        var context = new WidgetTranslationContext();

        var widget = context.Translate(outer);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 5));

        RenderToText(result.PaintToRenderable(), 80).TrimEnd().ShouldBe("    nested");
    }

    [Fact]
    public void HorizontalRule_FillsWidthWithRuleCharacter()
    {
        var hr = VNode.CreateElement("hr");
        var context = new WidgetTranslationContext();

        var widget = context.Translate(hr);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 10, 0, 5));
        var canvas = new TerminalCanvas(result.Size.Width, result.Size.Height);
        result.Root.Paint(new PaintContext(canvas));

        widget.ShouldBeOfType<RuleWidget>();
        RenderToText(result.PaintToRenderable(), 10).ShouldBe(new string('─', 10));
        canvas[0, 0].Style?.Foreground.ShouldBe(Color.Grey);
    }

    [Fact]
    public void NestedList_UnorderedInUnordered_IndentsAndKeepsBullets()
    {
        var ul = VNode.CreateElement("ul");
        var liA = CreateListItem("a");
        var nested = VNode.CreateElement("ul");
        nested.AddChild(CreateListItem("a1"));
        nested.AddChild(CreateListItem("a2"));
        liA.AddChild(nested);
        ul.AddChild(liA);
        ul.AddChild(CreateListItem("b"));

        var context = new WidgetTranslationContext();
        var widget = context.Translate(ul);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 10));
        var lines = RenderToText(result.PaintToRenderable(), 80).Split('\n').Select(l => l.TrimEnd()).ToArray();

        lines.ShouldBe(new[] { "• a", "  • a1", "  • a2", "• b" });
    }

    [Fact]
    public void NestedList_OrderedInUnordered_UsesOrderedPrefixForNestedList()
    {
        var ul = VNode.CreateElement("ul");
        var liA = CreateListItem("a");
        var nestedOl = VNode.CreateElement("ol");
        nestedOl.AddChild(CreateListItem("one"));
        nestedOl.AddChild(CreateListItem("two"));
        liA.AddChild(nestedOl);
        ul.AddChild(liA);

        var context = new WidgetTranslationContext();
        var widget = context.Translate(ul);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 10));
        var lines = RenderToText(result.PaintToRenderable(), 80).Split('\n').Select(l => l.TrimEnd()).ToArray();

        lines.ShouldBe(new[] { "• a", "  1. one", "  2. two" });
    }

    [Fact]
    public void NestedList_UnorderedInOrdered_UsesUnorderedPrefixForNestedList()
    {
        var ol = VNode.CreateElement("ol");
        var liA = CreateListItem("a");
        var nestedUl = VNode.CreateElement("ul");
        nestedUl.AddChild(CreateListItem("x"));
        liA.AddChild(nestedUl);
        ol.AddChild(liA);

        var context = new WidgetTranslationContext();
        var widget = context.Translate(ol);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 10));
        var lines = RenderToText(result.PaintToRenderable(), 80).Split('\n').Select(l => l.TrimEnd()).ToArray();

        lines.ShouldBe(new[] { "1. a", "   • x" });
    }

    [Fact]
    public void NestedList_ThreeLevelsDeep_IndentsEachLevel()
    {
        var level3 = VNode.CreateElement("ul");
        level3.AddChild(CreateListItem("leaf"));

        var level2Item = CreateListItem("mid");
        level2Item.AddChild(level3);
        var level2 = VNode.CreateElement("ul");
        level2.AddChild(level2Item);

        var level1Item = CreateListItem("top");
        level1Item.AddChild(level2);
        var level1 = VNode.CreateElement("ul");
        level1.AddChild(level1Item);

        var context = new WidgetTranslationContext();
        var widget = context.Translate(level1);
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 80, 0, 10));
        var lines = RenderToText(result.PaintToRenderable(), 80).Split('\n').Select(l => l.TrimEnd()).ToArray();

        lines.ShouldBe(new[] { "• top", "  • mid", "    • leaf" });
    }

    private static VNode CreateListItem(string text)
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
