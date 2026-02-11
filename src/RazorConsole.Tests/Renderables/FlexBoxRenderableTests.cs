// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Renderables;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Renderables;

public class FlexBoxRenderableTests
{
    private static RenderOptions CreateTestRenderOptions(int width = 40, int height = 25)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        return new RenderOptions(console.Profile.Capabilities, new Size(width, height));
    }

    private static string RenderToText(IRenderable renderable, int maxWidth = 40)
    {
        var options = CreateTestRenderOptions(maxWidth);
        var segments = renderable.Render(options, maxWidth).ToList();
        return string.Concat(segments.Select(s => s.IsLineBreak ? "\n" : s.Text));
    }

    private static List<string> RenderToLines(IRenderable renderable, int maxWidth = 40)
    {
        return RenderToText(renderable, maxWidth).Split('\n').ToList();
    }

    #region Constructor

    [Fact]
    public void Constructor_WithNullItems_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new FlexBoxRenderable(null!));
    }

    [Fact]
    public void Constructor_WithEmptyList_DoesNotThrow()
    {
        var renderable = new FlexBoxRenderable(new List<IRenderable>());
        renderable.ShouldNotBeNull();
    }

    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var renderable = new FlexBoxRenderable(new List<IRenderable>());
        renderable.Direction.ShouldBe(FlexDirection.Row);
        renderable.Justify.ShouldBe(FlexJustify.Start);
        renderable.Align.ShouldBe(FlexAlign.Start);
        renderable.Wrap.ShouldBe(FlexWrap.NoWrap);
        renderable.Gap.ShouldBe(0);
        renderable.Width.ShouldBeNull();
        renderable.Height.ShouldBeNull();
    }

    #endregion

    #region Measure

    [Fact]
    public void Measure_EmptyItems_ReturnsZero()
    {
        var renderable = new FlexBoxRenderable(new List<IRenderable>());
        var options = CreateTestRenderOptions();

        var measurement = renderable.Measure(options, 40);

        measurement.Min.ShouldBe(0);
        measurement.Max.ShouldBe(0);
    }

    [Fact]
    public void Measure_Row_SumsWidthsOfItems()
    {
        var items = new List<IRenderable>
        {
            new Markup("AAAA"),  // 4 chars
            new Markup("BB"),    // 2 chars
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row);
        var options = CreateTestRenderOptions();

        var measurement = renderable.Measure(options, 40);

        // Max should be sum of item widths (4 + 2 = 6)
        measurement.Max.ShouldBe(6);
    }

    [Fact]
    public void Measure_Row_WithGap_IncludesGaps()
    {
        var items = new List<IRenderable>
        {
            new Markup("AAAA"),
            new Markup("BB"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, gap: 3);
        var options = CreateTestRenderOptions();

        var measurement = renderable.Measure(options, 40);

        // Max should be 4 + 3 (gap) + 2 = 9
        measurement.Max.ShouldBe(9);
    }

    [Fact]
    public void Measure_Column_TakesMaxWidth()
    {
        var items = new List<IRenderable>
        {
            new Markup("AAAA"),  // 4 chars
            new Markup("BBBBBB"), // 6 chars
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Column);
        var options = CreateTestRenderOptions();

        var measurement = renderable.Measure(options, 40);

        // Max should be the widest item (6)
        measurement.Max.ShouldBe(6);
    }

    #endregion

    #region Row rendering

    [Fact]
    public void Render_EmptyItems_YieldsNoSegments()
    {
        var renderable = new FlexBoxRenderable(new List<IRenderable>());
        var options = CreateTestRenderOptions();

        var segments = renderable.Render(options, 40).ToList();

        segments.ShouldBeEmpty();
    }

    [Fact]
    public void Render_SingleItem_Row_RendersItem()
    {
        var items = new List<IRenderable> { new Markup("Hello") };
        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row);

        var text = RenderToText(renderable);

        text.ShouldContain("Hello");
    }

    [Fact]
    public void Render_TwoItems_Row_RendersOnSameLine()
    {
        var items = new List<IRenderable>
        {
            new Markup("AAA"),
            new Markup("BBB"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row);
        var lines = RenderToLines(renderable);

        // Should render on one line (no wrapping)
        lines.Count.ShouldBe(1);
        lines[0].ShouldContain("AAA");
        lines[0].ShouldContain("BBB");
    }

    [Fact]
    public void Render_Row_WithGap_InsertsPadding()
    {
        var items = new List<IRenderable>
        {
            new Markup("A"),
            new Markup("B"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, gap: 3);
        var text = RenderToText(renderable);

        // Should have A, then 3 spaces, then B (plus trailing space from justify)
        text.ShouldContain("A   B");
    }

    [Fact]
    public void Render_Row_JustifyEnd_PutsContentAtEnd()
    {
        var items = new List<IRenderable>
        {
            new Markup("AB"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, justify: FlexJustify.End);
        var lines = RenderToLines(renderable, maxWidth: 10);

        // Content should be at the right; leading padding of 8 chars
        lines[0].Length.ShouldBeGreaterThanOrEqualTo(10);
        lines[0].TrimEnd().ShouldEndWith("AB");
        lines[0].ShouldStartWith("        "); // 8 spaces before AB
    }

    [Fact]
    public void Render_Row_JustifyCenter_CentersContent()
    {
        var items = new List<IRenderable>
        {
            new Markup("AB"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, justify: FlexJustify.Center);
        var lines = RenderToLines(renderable, maxWidth: 10);

        // 10 - 2 = 8 free space, half = 4 on each side
        lines[0].ShouldStartWith("    AB");
    }

    [Fact]
    public void Render_Row_JustifySpaceBetween_DistributesSpace()
    {
        var items = new List<IRenderable>
        {
            new Markup("A"),
            new Markup("B"),
            new Markup("C"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, justify: FlexJustify.SpaceBetween);
        var lines = RenderToLines(renderable, maxWidth: 21);

        // Free space = 21 - 3 = 18, distributed between 2 gaps → 9 each
        // So: A + 9 spaces + B + 9 spaces + C
        lines[0].ShouldStartWith("A");
        lines[0].ShouldContain("B");
        lines[0].ShouldContain("C");
        // First char is A, no leading space
        lines[0][0].ShouldBe('A');
    }

    #endregion

    #region Column rendering

    [Fact]
    public void Render_TwoItems_Column_RendersOnSeparateLines()
    {
        var items = new List<IRenderable>
        {
            new Markup("AAA"),
            new Markup("BBB"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Column);
        var lines = RenderToLines(renderable);

        lines.Count.ShouldBeGreaterThanOrEqualTo(2);
        lines[0].ShouldContain("AAA");
        lines[1].ShouldContain("BBB");
    }

    [Fact]
    public void Render_Column_WithGap_InsertsBlankLines()
    {
        var items = new List<IRenderable>
        {
            new Markup("AAA"),
            new Markup("BBB"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Column, gap: 2);
        var lines = RenderToLines(renderable);

        // Should be: AAA, blank, blank, BBB = 4 lines
        lines.Count.ShouldBe(4);
        lines[0].ShouldContain("AAA");
        lines[3].ShouldContain("BBB");
        lines[1].Trim().ShouldBeEmpty();
        lines[2].Trim().ShouldBeEmpty();
    }

    [Fact]
    public void Render_Column_AlignEnd_RightAligns()
    {
        var items = new List<IRenderable>
        {
            new Markup("AB"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Column, align: FlexAlign.End);
        var lines = RenderToLines(renderable, maxWidth: 10);

        // Item should be right-aligned: 8 spaces + AB
        lines[0].ShouldStartWith("        AB");
    }

    [Fact]
    public void Render_Column_AlignCenter_CentersHorizontally()
    {
        var items = new List<IRenderable>
        {
            new Markup("AB"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Column, align: FlexAlign.Center);
        var lines = RenderToLines(renderable, maxWidth: 10);

        // 10 - 2 = 8, half = 4 leading
        lines[0].ShouldStartWith("    AB");
    }

    #endregion

    #region Wrapping

    [Fact]
    public void Render_Row_NoWrap_AllItemsOnOneLine()
    {
        var items = new List<IRenderable>
        {
            new Markup("AAAA"),
            new Markup("BBBB"),
            new Markup("CCCC"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, wrap: FlexWrap.NoWrap);
        var lines = RenderToLines(renderable, maxWidth: 10);

        // All items on one line even though they exceed maxWidth
        lines.Count.ShouldBe(1);
    }

    [Fact]
    public void Render_Row_Wrap_ItemsExceedingWidthWrap()
    {
        var items = new List<IRenderable>
        {
            new Markup("AAAA"),
            new Markup("BBBB"),
            new Markup("CCCC"),
        };

        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, wrap: FlexWrap.Wrap);
        var lines = RenderToLines(renderable, maxWidth: 9);

        // 9 chars wide, each item is 4 chars → AAAA+BBBB = 8 fits, CCCC wraps
        lines.Count.ShouldBeGreaterThanOrEqualTo(2);
        lines[0].ShouldContain("AAAA");
        lines[0].ShouldContain("BBBB");
    }

    [Fact]
    public void Render_Row_Wrap_WithGap_AccountsForGaps()
    {
        var items = new List<IRenderable>
        {
            new Markup("AAA"),
            new Markup("BBB"),
            new Markup("CCC"),
        };

        // Width=10, gap=2: AAA + 2 + BBB = 8, then CCC would need 2 + 3 = 5 more → 13 > 10, wraps
        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, wrap: FlexWrap.Wrap, gap: 2);
        var lines = RenderToLines(renderable, maxWidth: 10);

        lines.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Cross-axis alignment (Row)

    [Fact]
    public void Render_Row_AlignStart_ShortItemPaddedBelow()
    {
        // Create items of different heights
        var tallItem = new Rows(new List<IRenderable>
        {
            new Markup("T1"),
            new Markup("T2"),
            new Markup("T3"),
        });
        var shortItem = new Markup("S");

        var items = new List<IRenderable> { tallItem, shortItem };
        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, align: FlexAlign.Start);
        var lines = RenderToLines(renderable);

        // Should have 3 lines (height of tall item)
        lines.Count.ShouldBe(3);
        // Short item on the first line
        lines[0].ShouldContain("S");
    }

    [Fact]
    public void Render_Row_AlignEnd_ShortItemPaddedAbove()
    {
        var tallItem = new Rows(new List<IRenderable>
        {
            new Markup("T1"),
            new Markup("T2"),
            new Markup("T3"),
        });
        var shortItem = new Markup("S");

        var items = new List<IRenderable> { tallItem, shortItem };
        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, align: FlexAlign.End);
        var lines = RenderToLines(renderable);

        // Short item should be on the last line
        lines.Count.ShouldBe(3);
        lines[2].ShouldContain("S");
    }

    [Fact]
    public void Render_Row_AlignCenter_ShortItemCentered()
    {
        var tallItem = new Rows(new List<IRenderable>
        {
            new Markup("T1"),
            new Markup("T2"),
            new Markup("T3"),
        });
        var shortItem = new Markup("S");

        var items = new List<IRenderable> { tallItem, shortItem };
        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, align: FlexAlign.Center);
        var lines = RenderToLines(renderable);

        // Short item should be on the middle line
        lines.Count.ShouldBe(3);
        lines[1].ShouldContain("S");
    }

    #endregion

    #region Width constraint

    [Fact]
    public void Render_WithExplicitWidth_UsesConstrainedWidth()
    {
        var items = new List<IRenderable> { new Markup("AB") };
        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, justify: FlexJustify.End, width: 6);

        var lines = RenderToLines(renderable, maxWidth: 40);

        // With width=6 and justify=End, should have 4 spaces + AB
        lines[0].ShouldStartWith("    AB");
    }

    [Fact]
    public void Measure_WithExplicitWidth_RespectsConstraint()
    {
        var items = new List<IRenderable> { new Markup("AB") };
        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, width: 10);
        var options = CreateTestRenderOptions(40);

        var measurement = renderable.Measure(options, 40);

        // Max should not exceed explicit width
        measurement.Max.ShouldBeLessThanOrEqualTo(10);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void Render_NegativeGap_ClampedToZero()
    {
        var items = new List<IRenderable> { new Markup("A"), new Markup("B") };
        var renderable = new FlexBoxRenderable(items, gap: -5);

        renderable.Gap.ShouldBe(0);
    }

    [Fact]
    public void Render_SingleItem_Row_JustifySpaceBetween_CentersItem()
    {
        var items = new List<IRenderable> { new Markup("AB") };
        var renderable = new FlexBoxRenderable(items, direction: FlexDirection.Row, justify: FlexJustify.SpaceBetween);
        var lines = RenderToLines(renderable, maxWidth: 10);

        // Single item with SpaceBetween should center
        lines[0].ShouldStartWith("    AB");
    }

    #endregion
}
