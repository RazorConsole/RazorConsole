// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering.Markdown;
using Spectre.Console;

namespace RazorConsole.Tests.Input;

public sealed class StyledTextTests
{
    [Fact]
    public void Lists_KeepEveryItemAtTheSameIndent_AndPreserveOrderedStart()
    {
        var result = MarkdownText.Parse("Intro\n\n- one\n- two\n  - nested\n- three\n\n7. seven\n8. eight", "  ");
        result.Text.ShouldContain("\n  • one\n  • two\n    • nested\n  • three");
        result.Text.ShouldContain("\n  7. seven\n  8. eight");
        var heading = MarkdownText.Parse("### Heading");
        heading.Text.ShouldBe("### Heading");
        heading.Styles[0].Style.Decoration.ShouldBe(Decoration.Bold | Decoration.Italic);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(40)]
    public void PipeTables_AlignAndWrapUnicodeCells_WithoutRawMarkdown(int width)
    {
        const string source = "| 工具 | Purpose |\n|:---|---:|\n| `read` | 读取工作区中的 UTF-8 文本文件，支持分页读取。 |\n| `write` | Create files |";
        var result = MarkdownText.Parse(source, "  ", width);
        result.Text.ShouldNotContain("|");
        result.Text.ShouldContain("━");
        result.Text.ShouldContain("─");
        result.Text.ShouldContain("read");
        result.Text.ShouldContain("write");
        result.Text.Split('\n').ShouldAllBe(line => TextSelectionState.CellWidth(line) <= width);
        result.Styles.ShouldContain(span => result.Text.Substring(span.Start, span.Length) == "read" && span.Style.Foreground == new Color(53, 132, 29));
        result.Styles.ShouldContain(span => result.Text.Substring(span.Start, span.Length) == "工具" && span.Style.Foreground == new Color(166, 105, 21));
    }

    [Fact]
    public void WordWrap_PreservesSourceOffsetsAndHangingIndentHitTests()
    {
        var text = new TextSelectionState();
        text.SetText("• hello world again");
        var rows = text.Wrap(12, 2, true);
        string.Concat(rows.Select(row => text.Text[row.Start..row.End])).ShouldBe(text.Text);
        text.Text[rows[0].Start..rows[0].End].ShouldBe("• hello ");
        rows[1].Indent.ShouldBe(2);
        text.PositionAt(rows[1], 2).ShouldBe(rows[1].Start);
        text.PositionAt(rows[1], 3).ShouldBe(rows[1].Start + 1);
        rows.ShouldAllBe(row => TextSelectionState.CellWidth(text.Text.Substring(row.Start, row.End - row.Start)) + row.Indent <= 12);
    }

    [Fact]
    public void Markdown_ProducesSelectableTextWithStyleOffsets()
    {
        var result = MarkdownText.Parse("**bold** and `code`\n\n[docs](https://example.com)");
        result.Text.ShouldBe("bold and code\n\ndocs (https://example.com)");
        result.Styles.ShouldContain(span => result.Text.Substring(span.Start, span.Length) == "bold" && span.Style.Decoration == Decoration.Bold);
        result.Styles.ShouldContain(span => result.Text.Substring(span.Start, span.Length) == "code" && span.Style.Foreground == new Color(53, 132, 29));
        result.Styles.ShouldContain(span => result.Text.Substring(span.Start, span.Length) == "docs" && span.Style.Decoration == Decoration.Underline);
        MarkdownText.Parse("<div>literal HTML</div>").Text.ShouldContain("literal HTML");
    }
}
