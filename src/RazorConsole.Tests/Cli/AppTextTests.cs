// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Operations;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The text form of <c>tree</c> and <c>find</c>: one line per node, whatever the text holds.
/// </summary>
public sealed class AppTextTests
{
    [Theory]
    [InlineData("RazorConsole", "\"RazorConsole\"")]
    [InlineData("a\r\nb\t\"c\"", "\"a\\r\\nb\\t\\\"c\\\"\"")]
    [InlineData("back\\slash", "\"back\\\\slash\"")]
    [InlineData("bell\u0007", "\"bell\\u0007\"")]
    [InlineData("Привіт ✓", "\"Привіт ✓\"")]
    public void Quote_EscapesControlCharacters_OnOneLine(string text, string expected)
        => AppText.Quote(text).ShouldBe(expected);

    [Fact]
    public void Tree_HidesWhitespaceOnlyTextNodes_AndKeepsRealText()
    {
        var tree = new TreeResult(
            Node(
                "div",
                Node("text", "\r\n    "),
                Node("span", Node("text", "RazorConsole")),
                Node("text", "\r\n  ")),
            Frame: 1);

        var text = AppText.Tree(tree);

        // AppendLine ends lines with the platform's newline; the text nodes' own CR/LF must not appear.
        text.Split(Environment.NewLine).ShouldBe(["div", "  span", "    text \"RazorConsole\""]);
    }

    [Fact]
    public void Tree_PrintsTheTextOfAMarkupElementQuotedAndEscaped()
    {
        var tree = new TreeResult(
            Node("div", Markup("Razor\"Console\"\r\n"), Markup("│")),
            Frame: 1);

        AppText.Tree(tree).Split(Environment.NewLine)
            .ShouldBe(["div", "  span \"Razor\\\"Console\\\"\\r\\n\"", "  span \"│\""]);
    }

    [Fact]
    public void Find_WithoutMatches_IsAnEmptyList()
        => AppText.Find(new FindResult([], Frame: 1)).ShouldBe("no matches");

    private static DiagnosticsNode Node(string tag, params DiagnosticsNode[] children)
        => new() { Path = "/", Kind = "element", Tag = tag, Children = children };

    private static DiagnosticsNode Markup(string text)
        => new() { Path = "/", Kind = "element", Tag = "span", Text = text };

    private static DiagnosticsNode Node(string kind, string text)
        => new() { Path = "/", Kind = kind, Text = text };
}
