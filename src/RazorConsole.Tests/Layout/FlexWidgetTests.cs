// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Renderables;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

/// <summary>
/// Justify, align and wrap behavior of <see cref="FlexWidget"/>, ported from the legacy FlexBoxRenderable tests.
/// </summary>
public sealed class FlexWidgetTests
{
    [Theory]
    [InlineData(FlexJustify.Start, "abcd")]
    [InlineData(FlexJustify.End, "      abcd")]
    [InlineData(FlexJustify.Center, "   abcd")]
    [InlineData(FlexJustify.SpaceBetween, "ab      cd")]
    public void Row_Justify_PositionsItemsAlongTheMainAxis(FlexJustify justify, string expected)
    {
        var flex = new FlexWidget("flex", [Text("a", "ab"), Text("b", "cd")], justify: justify, fillWidth: true);

        Render(flex, 10).ShouldBe(expected);
    }

    [Fact]
    public void Row_JustifySpaceBetween_WithOneItem_KeepsItAtTheStart()
    {
        var flex = new FlexWidget("flex", [Text("a", "ab")], justify: FlexJustify.SpaceBetween, fillWidth: true);

        Render(flex, 10).ShouldBe("ab");
    }

    [Theory]
    [InlineData(FlexAlign.Start, "ax\nb\nc")]
    [InlineData(FlexAlign.Center, "a\nbx\nc")]
    [InlineData(FlexAlign.End, "a\nb\ncx")]
    public void Row_Align_PositionsShortItemsOnTheCrossAxis(FlexAlign align, string expected)
    {
        var flex = new FlexWidget("flex", [Text("tall", "a\nb\nc"), Text("short", "x")], align: align);

        Render(flex, 10).ShouldBe(expected);
    }

    [Theory]
    [InlineData(FlexAlign.Start, "ab\nc")]
    [InlineData(FlexAlign.Center, "  ab\n  c")]
    [InlineData(FlexAlign.End, "    ab\n     c")]
    public void Column_Align_PositionsItemsHorizontally(FlexAlign align, string expected)
    {
        var flex = new FlexWidget("flex", [Text("a", "ab"), Text("b", "c")], FlexDirection.Column, align: align, width: 6);

        Render(flex, 10).ShouldBe(expected);
    }

    [Fact]
    public void Column_Gap_InsertsBlankLines()
    {
        var flex = new FlexWidget("flex", [Text("a", "a"), Text("b", "b")], FlexDirection.Column, gap: 1);

        Render(flex, 10).ShouldBe("a\n\nb");
    }

    [Fact]
    public void Row_NoWrap_KeepsItemsOnOneLineAndClips()
    {
        var flex = new FlexWidget("flex", [Text("a", "aaaa"), Text("b", "bbbb"), Text("c", "cccc")], gap: 1);

        Render(flex, 10).ShouldBe("aaaa bbbb");
    }

    [Fact]
    public void Row_Wrap_JustifiesEachLineSeparately()
    {
        var flex = new FlexWidget(
            "flex",
            [Text("a", "aaaa"), Text("b", "bbbb"), Text("c", "cc")],
            justify: FlexJustify.End,
            wrap: FlexWrap.Wrap,
            gap: 1,
            fillWidth: true);

        Render(flex, 10).ShouldBe(" aaaa bbbb\n        cc");
    }

    [Fact]
    public void Row_Wrap_StacksLinesByTheirTallestItem()
    {
        var flex = new FlexWidget(
            "flex",
            [Text("a", "a1\na2"), Text("b", "b1"), Text("c", "c1")],
            wrap: FlexWrap.Wrap);

        Render(flex, 4).ShouldBe("a1b1\na2\nc1");
    }

    [Fact]
    public void FixedWidth_MeasuresChildrenAtThatWidth_SoWrappedLinesKeepTheirRows()
    {
        var column = new FlexWidget("column", [Text("wraps", "aaaa bbbb")], FlexDirection.Column, width: 6);
        var row = new FlexWidget("row", [column, Text("side", "x")], gap: 1);
        var page = new FlexWidget("page", [row, Text("below", "next")], FlexDirection.Column);

        Render(page, 20).ShouldBe("aaaa   x\nbbbb\nnext");
    }

    [Fact]
    public void FixedHeight_MeasuresChildrenAtThatHeight()
    {
        var child = Text("tall", "a\nb\nc\nd");
        var column = new FlexWidget("column", [child], FlexDirection.Column, height: 2);

        new LayoutEngine().Layout(column, new BoxConstraints(0, 10, 0, 20));

        child.DesiredSize.Height.ShouldBe(2);
    }

    [Fact]
    public void FixedWidth_LargerThanTheConstraint_MeasuresChildrenAtTheConstraint()
    {
        var child = Text("wraps", "aaaa bbbb");
        var column = new FlexWidget("column", [child], FlexDirection.Column, width: 30);

        new LayoutEngine().Layout(column, new BoxConstraints(0, 6, 0, 20));

        child.DesiredSize.Height.ShouldBe(2);
    }

    private static TextWidget Text(string id, string text) => new(id, text);

    private static string Render(Widget widget, int width)
    {
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, width, 0, 20));
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
