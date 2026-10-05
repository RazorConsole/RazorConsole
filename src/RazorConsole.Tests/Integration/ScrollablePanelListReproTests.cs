// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Tests.Integration.Fixtures;
using RazorConsole.Tests.Integration.Infrastructure;

namespace RazorConsole.Tests.Integration;

/// <summary>
/// Reproduces the reported Gallery bug: inside a Rows layout with MORE THAN ONE top-level
/// Expand="true" Panel, a nested Scrollable/Panel/&lt;ol&gt;/&lt;li&gt; list renders a completely
/// empty Panel body (only the border is visible).
/// </summary>
/// <remarks>
/// DIAGNOSIS ONLY: this test is intentionally left failing to pin down the regression end-to-end
/// through the real Razor + VDom + WidgetLayout pipeline. Do not "fix" it by changing assertions
/// to match broken output.
///
/// Root cause (confirmed by bisection - see the task notes/PR description for the full writeup):
/// this is a layout-engine MEASURE vs. ARRANGE mismatch for "Expand", NOT a list/&lt;ol&gt;/&lt;li&gt;
/// translation bug:
/// - <see cref="RazorConsole.Core.Layout.FlexWidget"/> (used by &lt;Rows&gt;, a Column-direction
///   flex) measures each child against the FULL available height independently, but at ARRANGE
///   time splits the available height EVENLY across every child flagged "expanding"
///   (<c>data-expand="true"</c>, set whenever Panel's <c>Expand</c> parameter is true - see
///   Box.razor / Panel.razor). With two sibling Expand Panels in the same Rows, each gets only
///   half the height at Arrange that it was given at Measure.
/// - That deficit then flows into <see cref="RazorConsole.Core.Layout.BoxWidget"/>
///   (Panel) and <see cref="RazorConsole.Core.Layout.StackWidget"/> (used to compose a
///   Scrollable's/Panel's multiple children), both of which clamp a child's arranged size to
///   <c>Math.Min(child.DesiredSize, availableSpace)</c> using the now-stale (too-large)
///   DesiredSize from Measure.
/// - Critically, <see cref="RazorConsole.Core.Layout.StackWidget.ArrangeCore"/> arranges children
///   sequentially top-to-bottom and clamps each one to whatever height remains
///   (<c>bounds.Bottom - y</c>), so when a stack's total desired height exceeds what it was
///   actually given, the LAST child in document order absorbs the entire shortfall - all the way
///   down to a literal 0 if the deficit is large enough. In this repro, the second (ol) Scrollable
///   is always the later sibling of the first (table) Scrollable inside "Embedded scrollbar", and
///   that Panel/&lt;ol&gt; pairing happens to end up last overall, so it is the one that collapses.
/// - Confirmed NOT list-specific: swapping the &lt;ol&gt;/&lt;li&gt; content for a plain
///   &lt;div&gt;/&lt;Markup&gt; loop inside the same nested Scrollable/Panel reproduces an
///   identical collapse (arranged height 0), with no &lt;ol&gt;/&lt;li&gt; involved at all.
/// - A single Scrollable+Panel+&lt;ol&gt; with no sibling Expand Panel in the same Rows renders
///   correctly - the second top-level Expand Panel is what triggers the measure/arrange split.
///
/// This means the currently-uncommitted <see cref="RazorConsole.Core.Layout.HtmlListItemWidget"/>
/// / WidgetTranslationContext.cs changes (which fix &lt;li&gt; content being flattened to plain
/// text and losing styling) are addressing a real but UNRELATED, orthogonal bug. They do not
/// cause and cannot fix this one.
/// </remarks>
public sealed class ScrollablePanelListReproTests
{
    [Fact]
    public async Task ScrollableWithPanelAndOrderedList_RendersListItemsInsidePanel()
    {
        await using var terminal = await TestTerminal.StartAsync<ScrollablePanelListIntegrationApp>(
            80,
            30,
            cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = terminal.Snapshot;
        var screenText = snapshot.ScreenText;

        // This is what SHOULD render: the ordered list items "1. A" through "6. F" (page size 6)
        // inside the bordered Panel, plus the trailing "Page: 1 / 10" markup below it.
        // As of this diagnosis, the Panel body renders empty - these assertions fail.
        screenText.ShouldContain("1. A");
        screenText.ShouldContain("2. B");
        screenText.ShouldContain("Page: 1 / 10");
    }
}
