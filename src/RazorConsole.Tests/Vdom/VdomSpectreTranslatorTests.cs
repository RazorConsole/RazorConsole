// Copyright (c) RazorConsole. All rights reserved.

using System.Reflection;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Rendering.Syntax;
using RazorConsole.Core.Rendering.Translation;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Rendering.Translation.Translators;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Vdom;

public class VdomSpectreTranslatorTests
{
    private static TranslationContext CreateContext() => RazorConsole.Tests.TestHelpers.CreateTestTranslationContext();

    [Fact]
    public void Translate_TextNode_ReturnsTextRenderable()
    {
        var context = CreateContext();
        var node = VNode.CreateText("Hello");

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<Text>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_TextSpan_UsesVdomTranslator()
    {
        var context = CreateContext();
        var node = Element("span", span =>
        {
            span.SetAttribute("data-text", "true");
            span.SetAttribute("data-style", "red");
            span.AddChild(Text("Hello"));
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<Markup>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_SyntaxHighlighterNode_ReturnsSyntaxRenderable()
    {
        var context = CreateContext();
        var model = new SyntaxHighlightRenderModel(new[] { "[blue]Console.WriteLine()[/]" }, false, string.Empty, SyntaxOptions.Default.PlaceholderMarkup);
        var payload = SyntaxHighlightingService.EncodePayload(model);

        var node = Element("div", div =>
        {
            div.SetAttribute("class", "syntax-highlighter");
            div.SetAttribute("data-payload", payload);
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<SyntaxRenderable>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_ParagraphNode_ReturnsRowsRenderable()
    {
        var context = CreateContext();
        var node = Element("p", paragraph =>
        {
            paragraph.AddChild(Text("Paragraph content"));
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<Markup>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_ParagraphWithInlineElements_ReturnsComposedRenderable()
    {
        var context = CreateContext();
        var node = Element("p", p =>
        {
            p.AddChild(Text("Visit "));
            p.AddChild(Element("a", a =>
            {
                a.SetAttribute("href", "https://example.com");
                a.AddChild(Text("example.com"));
            }));
            p.AddChild(Text(" for more info."));
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldNotBeNull();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_Spacer_DoesNotUseHtmlFallback()
    {
        var context = CreateContext();
        var node = Element("div", spacer =>
        {
            spacer.SetAttribute("data-spacer", "true");
            spacer.SetAttribute("data-lines", "2");
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<Markup>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_PanelNode_ReturnsPanelRenderable()
    {
        var context = CreateContext();
        var child = Element("span", span =>
        {
            span.SetAttribute("data-text", "true");
            span.SetAttribute("data-style", "green");
            span.AddChild(Text("Panel body"));
        });

        var node = Element("div", panel =>
        {
            panel.SetAttribute("class", "panel");
            panel.SetAttribute("data-panel-border", "rounded");
            panel.SetAttribute("data-panel-padding", "1 1 1 1");
            panel.SetAttribute("data-header", "Title");
            panel.AddChild(child);
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        var panel = renderable.ShouldBeOfType<Panel>();
        panel.Header.ShouldNotBeNull();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_RowsNode_ReturnsRowsRenderable()
    {
        var context = CreateContext();
        var child = Element("span", span =>
        {
            span.SetAttribute("class", "row");
            span.AddChild(Text("Row content"));
        });

        var node = Element("div", rows =>
        {
            rows.SetAttribute("class", "rows");
            rows.SetAttribute("data-expand", "true");
            rows.AddChild(child);
            rows.AddChild(child);
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<Rows>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_HtmlButtonNode_ReturnsPanelRenderable()
    {
        var context = CreateContext();
        var node = Element("button", button =>
        {
            button.AddChild(Text("Click me"));
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<Panel>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_GridNode_ReturnsGridRenderable()
    {
        var context = CreateContext();
        var nodes = new List<VNode>
        {
            Element("span", span =>
            {
                span.SetAttribute("data-text", "true");
                span.AddChild(Text("Cell1"));
            }),
            Element("span", span =>
            {
                span.SetAttribute("data-text", "true");
                span.AddChild(Text("Cell2"));
            }),
        };

        var node = Element("div", grid =>
        {
            grid.SetAttribute("class", "grid");
            grid.SetAttribute("data-columns", "2");
            foreach (var child in nodes)
            {
                grid.AddChild(child);
            }
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<Grid>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_GenericDivNode_ReturnsBlockInlineRenderable()
    {
        var context = CreateContext();
        var node = Element("div", div =>
        {
            div.AddChild(Text("Line 1"));
            div.AddChild(Text("Line 2"));
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<BlockInlineRenderable>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_PadderNode_ReturnsPadderRenderable()
    {
        var context = CreateContext();
        var child = Element("span", span =>
        {
            span.SetAttribute("data-text", "true");
            span.AddChild(Text("Padded"));
        });

        var node = Element("div", padder =>
        {
            padder.SetAttribute("class", "padder");
            padder.SetAttribute("data-padding", "1");
            padder.AddChild(child);
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<Padder>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_NewlineNode_ReturnsMarkup()
    {
        var context = CreateContext();
        var node = Element("div", newline =>
        {
            newline.SetAttribute("class", "newline");
            newline.SetAttribute("data-count", "2");
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        renderable.ShouldBeOfType<Markup>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_SpinnerNode_ReturnsAnimatedRenderable()
    {
        var context = CreateContext();
        var node = Element("div", spinner =>
        {
            spinner.SetAttribute("class", "spinner");
            spinner.SetAttribute("data-spinner", "true");
            spinner.SetAttribute("data-message", "Loading");
            spinner.SetAttribute("data-style", "yellow");
            spinner.SetAttribute("data-spinner-type", "Dots");
        });

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;

        var animated = renderable.ShouldBeAssignableTo<IAnimatedConsoleRenderable>();
        animations.ShouldHaveSingleItem();
        var registered = animations.Single();
        registered.ShouldBeSameAs(animated);
    }

    [Fact]
    public void Translate_AlignNode_ReturnsAlignRenderable()
    {
        var child = Element("span", span =>
        {
            span.SetAttribute("class", "text");
            span.AddChild(Text("Aligned"));
        });

        var node = Element("div", align =>
        {
            align.SetAttribute("class", "align");
            align.SetAttribute("data-horizontal", "center");
            align.SetAttribute("data-vertical", "middle");
            align.SetAttribute("data-width", "40");
            align.SetAttribute("data-height", "5");
            align.AddChild(child);
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        renderable.ShouldBeOfType<MeasuredAlign>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_TableNode_ReturnsTableRenderable()
    {
        var headerRow = Element("tr", tr =>
        {
            tr.AddChild(HeaderCell("Stage", "left"));
            tr.AddChild(HeaderCell("Duration", "center"));
            tr.AddChild(HeaderCell("Result", "right"));
        });

        var thead = Element("thead", head =>
        {
            head.AddChild(headerRow);
        });

        var bodyRow = Element("tr", tr =>
        {
            tr.SetAttribute("data-style", "fg=grey");
            tr.AddChild(DataCell("Compile"));
            tr.AddChild(DataCell("00:03:12"));
            tr.AddChild(DataCell("✔"));
        });

        var tbody = Element("tbody", body =>
        {
            body.AddChild(bodyRow);
        });

        var tableNode = Element("table", table =>
        {
            table.SetAttribute("class", "table");
            table.SetAttribute("data-expand", "true");
            table.SetAttribute("data-title", "Build status");
            table.AddChild(thead);
            table.AddChild(tbody);
        });

        var context = CreateContext();

        var renderable = context.Translate(tableNode);
        var animations = context.AnimatedRenderables;
        var table = renderable.ShouldBeOfType<Table>();
        table.Expand.ShouldBeTrue();
        table.ShowHeaders.ShouldBeTrue();
        table.Columns.Count.ShouldBe(3);
        table.Columns[2].Alignment.ShouldBe(Justify.Right);
        animations.ShouldBeEmpty();

        static VNode HeaderCell(string content, string align) => Element("th", th =>
        {
            th.SetAttribute("data-align", align);
            th.AddChild(Text(content));
        });

        static VNode DataCell(string content) => Element("td", td =>
        {
            td.AddChild(Text(content));
        });
    }

    [Fact]
    public void Translate_StrongElement_ReturnsBoldMarkup()
    {
        var node = Element("strong", strong =>
        {
            strong.AddChild(Text("Important"));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        var markup = renderable.ShouldBeOfType<Markup>();
        BuildInlineMarkupLiteral(node).ShouldBe("[bold]Important[/]");
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_NestedInlineElements_ComposesMarkup()
    {
        var node = Element("strong", strong =>
        {
            strong.AddChild(Text("Hello "));
            strong.AddChild(Element("em", em =>
            {
                em.AddChild(Text("world"));
            }));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        var markup = renderable.ShouldBeOfType<Markup>();
        BuildInlineMarkupLiteral(node).ShouldBe("[bold]Hello [italic]world[/][/]");
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_CodeElement_DisablesNestedFormatting()
    {
        var node = Element("code", code =>
        {
            code.AddChild(Text("var value = 42;"));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        var markup = renderable.ShouldBeOfType<Markup>();
        BuildInlineMarkupLiteral(node).ShouldBe("[indianred1 on #1f1f1f]var value = 42;[/]");
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_AnchorElement_WithHref_ReturnsLinkMarkup()
    {
        var node = Element("a", a =>
        {
            a.SetAttribute("href", "https://example.com");
            a.AddChild(Text("Click here"));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        var markup = renderable.ShouldBeOfType<Markup>();
        BuildInlineMarkupLiteral(node).ShouldBe("[link=https://example.com]Click here[/]");
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_AnchorElement_WithoutHref_ReturnsPlainText()
    {
        var node = Element("a", a =>
        {
            a.AddChild(Text("Not a link"));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        var markup = renderable.ShouldBeOfType<Markup>();
        BuildInlineMarkupLiteral(node).ShouldBe("Not a link");
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_AnchorElement_WithSpecialCharactersInHref_EscapesUrl()
    {
        var node = Element("a", a =>
        {
            a.SetAttribute("href", "https://example.com/path?q=[test]");
            a.AddChild(Text("Link"));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        var markup = renderable.ShouldBeOfType<Markup>();
        // Markup.Escape should escape the square brackets
        var expectedMarkup = BuildInlineMarkupLiteral(node);
        expectedMarkup.ShouldContain("link=");
        expectedMarkup.ShouldContain("Link");
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_NestedAnchorWithFormatting_ComposesMarkup()
    {
        var node = Element("a", a =>
        {
            a.SetAttribute("href", "https://example.com");
            a.AddChild(Text("Click "));
            a.AddChild(Element("strong", strong =>
            {
                strong.AddChild(Text("here"));
            }));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        var markup = renderable.ShouldBeOfType<Markup>();
        BuildInlineMarkupLiteral(node).ShouldBe("[link=https://example.com]Click [bold]here[/][/]");
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void ConvertChildren_TextNodes_NormalizeWhitespace()
    {
        var context = CreateContext();
        var children = new List<VNode>
        {
            VNode.CreateText("  Hello   world"),
            VNode.CreateText("\n\tand\t friends  "),
        };

        var renderables = TranslationHelpers.TryConvertChildrenToRenderables(children, context, out var result) ? result : [];
        renderables.Count.ShouldBe(2);
    }

    [Fact]
    public void ConvertChildren_WhitespaceBetweenTextNodes_ProducesSingleSpacer()
    {
        var context = CreateContext();
        var children = new List<VNode>
        {
            VNode.CreateText("Hello"),
            VNode.CreateText("   \r\n\t "),
            VNode.CreateText("world"),
        };

        var renderables = TranslationHelpers.TryConvertChildrenToRenderables(children, context, out var result) ? result : [];
        renderables.Count.ShouldBe(2);
    }

    [Fact]
    public void ConvertChildren_TrailingWhitespace_IsDiscarded()
    {
        var context = CreateContext();
        var children = new List<VNode>
        {
            VNode.CreateText("Hello "),
        };

        var success = TranslationHelpers.TryConvertChildrenToRenderables(children, context, out var renderables);

        success.ShouldBeTrue();
        renderables.ShouldHaveSingleItem();
        var markup = renderables.Single();
        AssertMarkupText(markup);
    }

    [Fact]
    public void Translate_UnorderedList_ReturnsRowsWithBullets()
    {
        var node = Element("ul", ul =>
        {
            ul.AddChild(Element("li", li =>
            {
                li.AddChild(Text("First item"));
            }));
            ul.AddChild(Element("li", li =>
            {
                li.AddChild(Text("Second item"));
            }));
            ul.AddChild(Element("li", li =>
            {
                li.AddChild(Text("Third item"));
            }));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        renderable.ShouldBeOfType<Rows>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_OrderedList_ReturnsRowsWithNumbers()
    {
        var node = Element("ol", ol =>
        {
            ol.AddChild(Element("li", li =>
            {
                li.AddChild(Text("First step"));
            }));
            ol.AddChild(Element("li", li =>
            {
                li.AddChild(Text("Second step"));
            }));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        renderable.ShouldBeOfType<Rows>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_OrderedListWithStartAttribute_ReturnsRowsWithCustomStartNumber()
    {
        var node = Element("ol", ol =>
        {
            ol.SetAttribute("start", "50");
            ol.AddChild(Element("li", li =>
            {
                li.AddChild(Text("Coffee"));
            }));
            ol.AddChild(Element("li", li =>
            {
                li.AddChild(Text("Tea"));
            }));
            ol.AddChild(Element("li", li =>
            {
                li.AddChild(Text("Milk"));
            }));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        renderable.ShouldBeOfType<Rows>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_OrderedListWithInvalidStartAttribute_UsesDefaultStartNumber()
    {
        var node = Element("ol", ol =>
        {
            ol.SetAttribute("start", "invalid");
            ol.AddChild(Element("li", li =>
            {
                li.AddChild(Text("First"));
            }));
            ol.AddChild(Element("li", li =>
            {
                li.AddChild(Text("Second"));
            }));
        });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        renderable.ShouldBeOfType<Rows>();
        animations.ShouldBeEmpty();
    }

    [Fact]
    public void Translate_EmptyList_ReturnsEmptyMarkup()
    {
        var node = Element("ul", ul => { });

        var context = CreateContext();

        var renderable = context.Translate(node);
        var animations = context.AnimatedRenderables;
        renderable.ShouldBeOfType<Markup>();
        animations.ShouldBeEmpty();
    }

    private static VNode Element(string tagName, Action<VNode>? configure = null)
    {
        var node = VNode.CreateElement(tagName);
        configure?.Invoke(node);
        return node;
    }

    private static VNode Text(string? value) => VNode.CreateText(value);

    private static string BuildInlineMarkupLiteral(VNode node)
    {
        var translatorType = typeof(HtmlInlineTextElementTranslator);
        translatorType.ShouldNotBeNull();

        var method = translatorType!.GetMethod("TryBuildMarkup", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        method.ShouldNotBeNull();

        var arguments = new object?[] { node, null };
        var success = (bool)method!.Invoke(null, arguments)!;
        success.ShouldBeTrue();

        return arguments[1].ShouldBeOfType<string>();
    }


    private static void AssertMarkupText(IRenderable renderable)
    {
        // TODO think about better way to extract text from markup
        var markup = renderable.ShouldBeOfType<Markup>();
    }
}
