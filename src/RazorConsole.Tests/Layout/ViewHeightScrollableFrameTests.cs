// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

/// <summary>
/// Repro + regression coverage for the reported bug: a <c>ViewHeightScrollable</c> wrapping a
/// bordered <c>Panel</c> (the real Gallery usage - see
/// gallery/RazorConsole.Gallery/Pages/ScrollableGallery.razor) scrolled its entire frame
/// (border/title) along with its content instead of keeping the frame fixed and only moving the
/// inner content.
/// </summary>
public sealed class ViewHeightScrollableFrameTests
{
    [Fact]
    public void ViewHeightScrollable_WithBorderedPanel_KeepsFrameFixedWhileContentScrolls()
    {
        // 6 lines of content, 3-line viewport => offsets 0, 1, 2, 3 are all valid (max offset = 3).
        string[] lines = ["Line1", "Line2", "Line3", "Line4", "Line5", "Line6"];

        var atOffset0 = Render(lines, offset: 0, linesToRender: 3, width: 20, title: "Frame");
        var atOffset1 = Render(lines, offset: 1, linesToRender: 3, width: 20, title: "Frame");
        var atOffset3 = Render(lines, offset: 3, linesToRender: 3, width: 20, title: "Frame");

        // The frame is 5 rows tall: top border, 3 content rows, bottom border.
        var rows0 = atOffset0.Split('\n');
        var rows1 = atOffset1.Split('\n');
        var rows3 = atOffset3.Split('\n');
        rows0.Length.ShouldBe(5);
        rows1.Length.ShouldBe(5);
        rows3.Length.ShouldBe(5);

        // The top border (with title) and bottom border must be byte-for-byte identical and stay
        // at the same physical row regardless of scroll offset - this is the "frame stays fixed"
        // requirement.
        rows0[0].ShouldBe(rows1[0]);
        rows0[0].ShouldBe(rows3[0]);
        rows0[0].ShouldContain("Frame");
        rows0[^1].ShouldBe(rows1[^1]);
        rows0[^1].ShouldBe(rows3[^1]);

        // The inner content must change with the offset, proving scrolling still works.
        rows0[1].ShouldContain("Line1");
        rows1[1].ShouldContain("Line2");
        rows3[1].ShouldContain("Line4");
        rows3[3].ShouldContain("Line6");
    }

    [Fact]
    public void ViewHeightScrollable_ShortContent_RendersNormallyWithoutScrolling()
    {
        string[] lines = ["Only one line"];

        var output = Render(lines, offset: 0, linesToRender: 3, width: 20, title: "Short");

        var rows = output.Split('\n');
        rows[0].ShouldContain("Short");
        rows.ShouldContain(row => row.Contains("Only one line"));

        // Border rows must still be present top and bottom with no corruption.
        rows[0].ShouldStartWith("╭");
        rows[^1].ShouldStartWith("╰");
    }

    [Fact]
    public void ViewHeightScrollable_WithPadding_KeepsPaddingAndFrameFixed()
    {
        string[] lines = ["A", "B", "C", "D"];

        var atOffset0 = Render(lines, offset: 0, linesToRender: 2, width: 20, title: "Pad", paddingLeftRight: 2);
        var atOffset2 = Render(lines, offset: 2, linesToRender: 2, width: 20, title: "Pad", paddingLeftRight: 2);

        var rows0 = atOffset0.Split('\n');
        var rows2 = atOffset2.Split('\n');

        rows0[0].ShouldBe(rows2[0]);
        rows0[^1].ShouldBe(rows2[^1]);

        // Padding means the content column should be indented by 2 spaces from the left border.
        rows0[1].ShouldStartWith("│  A");
        rows2[1].ShouldStartWith("│  C");
    }

    [Fact]
    public void ViewHeightScrollable_ExpandTrueOrFalse_KeepsFrameFixed()
    {
        string[] lines = ["X1", "X2", "X3", "X4"];

        foreach (var expand in new[] { true, false })
        {
            var atOffset0 = Render(lines, offset: 0, linesToRender: 2, width: 20, title: "Exp", expand: expand);
            var atOffset1 = Render(lines, offset: 1, linesToRender: 2, width: 20, title: "Exp", expand: expand);

            var rows0 = atOffset0.Split('\n');
            var rows1 = atOffset1.Split('\n');

            rows0[0].ShouldBe(rows1[0]);
            rows0[^1].ShouldBe(rows1[^1]);
        }
    }

    [Fact]
    public void ViewHeightScrollable_WithScrollbar_RendersScrollbarNextToBorderedPanel()
    {
        string[] lines = ["L1", "L2", "L3", "L4", "L5"];

        var output = Render(lines, offset: 0, linesToRender: 2, width: 20, title: "Bar", withScrollbar: true);
        var rows = output.Split('\n');

        // Frame (border) rows are still produced and the scrollbar track/thumb characters appear
        // somewhere to the right of the panel content, never corrupting the border glyphs.
        rows[0].ShouldStartWith("╭");
        rows[^1].ShouldStartWith("╰");
        output.ShouldContain('█');
    }

    [Fact]
    public void ViewHeightScrollable_BorderlessChild_IsUnaffectedByFramePeeling()
    {
        var node = VNode.CreateElement("view-height-scrollable");
        node.SetAttribute("data-offset", "1");
        node.SetAttribute("data-lines-to-render", "2");
        node.SetAttribute("data-enable-embedded", "false");

        var rows = VNode.CreateElement("div");
        rows.SetAttribute("class", "rows");
        rows.AddChild(VNode.CreateText("One"));
        rows.AddChild(VNode.CreateText("Two"));
        rows.AddChild(VNode.CreateText("Three"));
        node.AddChild(rows);

        var context = new WidgetTranslationContext();
        var widget = context.Translate(node);

        // No bordered box to peel off: the translator must return the plain ScrollableWidget it
        // always returned for borderless children, completely unchanged.
        widget.ShouldBeOfType<ScrollableWidget>();
    }

    [Fact]
    public void ViewHeightScrollable_BorderedPanelChild_PeelsBoxOutAsOuterFixedFrame()
    {
        var node = BuildViewHeightScrollableNode(["A", "B"], offset: 0, linesToRender: 2, title: "T");

        var context = new WidgetTranslationContext();
        var widget = context.Translate(node);

        // The structure should now be Box(frame) -> Scrollable(content), i.e. the box is outermost
        // and owns the fixed border/title, while the scrollable - nested inside - only clips and
        // offsets the inner content.
        var box = widget.ShouldBeOfType<BoxWidget>();
        box.Child.ShouldBeOfType<ScrollableWidget>();
    }

    private static string Render(
        string[] lines,
        int offset,
        int linesToRender,
        int width,
        string title,
        int paddingLeftRight = 0,
        bool expand = false,
        bool withScrollbar = false)
    {
        var node = BuildViewHeightScrollableNode(lines, offset, linesToRender, title, paddingLeftRight, expand, withScrollbar);
        var context = new WidgetTranslationContext();
        var widget = context.Translate(node);
        var engine = new LayoutEngine();
        var result = engine.Layout(widget, new BoxConstraints(0, width, 0, 20));
        return RenderToText(result.PaintToRenderable(), width);
    }

    private static VNode BuildViewHeightScrollableNode(
        string[] lines,
        int offset,
        int linesToRender,
        string title,
        int paddingLeftRight = 0,
        bool expand = false,
        bool withScrollbar = false)
    {
        var node = VNode.CreateElement("view-height-scrollable");
        node.SetAttribute("data-offset", offset.ToString());
        node.SetAttribute("data-lines-to-render", linesToRender.ToString());
        node.SetAttribute("data-enable-embedded", "false");

        if (withScrollbar)
        {
            var scrollbar = VNode.CreateElement("div");
            scrollbar.SetAttribute("data-scrollbar", "true");
            scrollbar.SetAttribute("data-track-char", "│");
            scrollbar.SetAttribute("data-thumb-char", "█");
            scrollbar.SetAttribute("data-min-thumb-height", "1");
            node.AddChild(scrollbar);
        }

        var panel = VNode.CreateElement("div");
        panel.SetAttribute("class", "panel");
        panel.SetAttribute("data-header", title);
        panel.SetAttribute("data-border", "rounded");
        if (paddingLeftRight > 0)
        {
            panel.SetAttribute("data-padding", $"{paddingLeftRight},0,{paddingLeftRight},0");
        }

        if (expand)
        {
            panel.SetAttribute("data-expand", "true");
        }

        var content = VNode.CreateElement("div");
        content.SetAttribute("class", "rows");
        foreach (var line in lines)
        {
            content.AddChild(CreateTextElement(line));
        }

        panel.AddChild(content);
        node.AddChild(panel);
        return node;
    }

    private static VNode CreateTextElement(string content)
    {
        var node = VNode.CreateElement("span");
        node.SetAttribute("data-text", "true");
        node.SetAttribute("data-content", content);
        return node;
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
