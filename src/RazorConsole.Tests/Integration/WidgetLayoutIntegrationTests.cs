// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Tests.Integration.Fixtures;
using RazorConsole.Tests.Integration.Infrastructure;

namespace RazorConsole.Tests.Integration;

public sealed class WidgetLayoutIntegrationTests
{
    [Fact]
    public async Task ComponentTree_RendersToExactCellsAndPublishesWidgetBounds()
    {
        await using var terminal = await TestTerminal.StartAsync<WidgetLayoutIntegrationApp>(
            40,
            10,
            cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = terminal.Snapshot;

        snapshot.Width.ShouldBe(40);
        snapshot.Height.ShouldBe(10);
        snapshot.GetLayout("root").ShouldMatch(new LayoutRect(0, 0, 40, 5));
        snapshot.GetLayout("navigation").ShouldMatch(new LayoutRect(0, 0, 10, 5));
        snapshot.GetLayout("content").ShouldMatch(new LayoutRect(12, 0, 28, 5));
        snapshot[0, 0].Text.ShouldBe("┌");
        snapshot[1, 1].Text.ShouldBe("N");
        snapshot[12, 0].Text.ShouldBe("┌");
        snapshot[13, 1].Text.ShouldBe("C");
    }

    [Fact]
    public async Task Resize_ReflowsComponentAndCapturesBeforeAndAfterFrames()
    {
        await using var terminal = await TestTerminal.StartAsync<WidgetLayoutIntegrationApp>(
            40,
            10,
            cancellationToken: TestContext.Current.CancellationToken);
        var before = terminal.Snapshot;

        terminal.Resize(30, 8);
        var after = terminal.Snapshot;

        before.GetLayout("content").ShouldMatch(new LayoutRect(12, 0, 28, 5));
        after.Width.ShouldBe(30);
        after.Height.ShouldBe(8);
        after.GetLayout("root").ShouldMatch(new LayoutRect(0, 0, 30, 5));
        after.GetLayout("navigation").ShouldMatch(new LayoutRect(0, 0, 10, 5));
        after.GetLayout("content").ShouldMatch(new LayoutRect(12, 0, 18, 5));
        terminal.Frames.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task KeyboardActivation_RendersUpdatedComponentState()
    {
        await using var terminal = await TestTerminal.StartAsync<InteractiveWidgetIntegrationApp>(
            30,
            8,
            cancellationToken: TestContext.Current.CancellationToken);

        await terminal.SendKeyAsync(
            ConsoleKey.Enter,
            cancellationToken: TestContext.Current.CancellationToken);
        var snapshot = await terminal.WaitUntilAsync(
            frame => frame.ContainsText("Count: 1"),
            description: "the activated button to render Count: 1",
            cancellationToken: TestContext.Current.CancellationToken);

        snapshot.ContainsText("Increment").ShouldBeTrue();
        snapshot.ContainsText("Count: 1").ShouldBeTrue();
        terminal.Frames.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Diagnostics_ExposeScreenFocusAndNamedLayouts()
    {
        await using var terminal = await TestTerminal.StartAsync<WidgetLayoutIntegrationApp>(
            40,
            10,
            cancellationToken: TestContext.Current.CancellationToken);

        var diagnostics = terminal.DumpDiagnostics();

        diagnostics.ShouldContain("Captured frames:");
        diagnostics.ShouldContain("current focus:");
        diagnostics.ShouldContain("NAV");
        diagnostics.ShouldContain("navigation: left=0, top=0, width=10, height=5");
    }
}

internal static class LayoutAssertionExtensions
{
    public static void ShouldMatch(this RazorConsole.Core.Vdom.VNodeLayoutInfo layout, LayoutRect expected)
    {
        layout.Left.ShouldBe(expected.X);
        layout.Top.ShouldBe(expected.Y);
        layout.Width.ShouldBe(expected.Width);
        layout.Height.ShouldBe(expected.Height);
    }
}
