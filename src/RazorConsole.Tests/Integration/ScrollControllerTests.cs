// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class ScrollControllerTests
{
    // 20 lines and the target make 21 rows in a view of 4.
    private const int ContentRows = 21;

    [Fact]
    public async Task Controller_ReportsMetricsOnceTheViewHasBeenLaidOut()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = new ScrollController();
        await using var terminal = await StartAsync(controller, ct: ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        controller.IsAttached.ShouldBeTrue();
        controller.Metrics.ContentHeight.ShouldBe(ContentRows);
        controller.Metrics.ViewportHeight.ShouldBe(4);
        controller.Metrics.MaxTop.ShouldBe(ContentRows - 4);
        controller.Metrics.IsAtStart.ShouldBeTrue();
        controller.Metrics.IsAtEnd.ShouldBeFalse();
    }

    [Fact]
    public async Task ScrollTo_ScrollBy_AndTheEnds_MoveTheView()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = new ScrollController();
        await using var terminal = await StartAsync(controller, ct: ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        await controller.ScrollToAsync(5);
        var frame = await terminal.WaitForTextAsync("line 5", cancellationToken: ct);
        frame.ContainsText("line 0").ShouldBeFalse();

        await controller.ScrollByAsync(2);
        await terminal.WaitForTextAsync("line 7", cancellationToken: ct);
        await controller.ScrollByAsync(-3);
        await terminal.WaitForTextAsync("line 4", cancellationToken: ct);

        await controller.ScrollToEndAsync();
        await terminal.WaitForTextAsync("Target", cancellationToken: ct);
        controller.Metrics.IsAtEnd.ShouldBeTrue();

        await controller.ScrollToStartAsync();
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);
        controller.Metrics.IsAtStart.ShouldBeTrue();
    }

    [Fact]
    public async Task ScrollTo_IsLimitedToTheScrollableRange()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = new ScrollController();
        await using var terminal = await StartAsync(controller, ct: ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        await controller.ScrollToAsync(-10);
        controller.Metrics.Top.ShouldBe(0);
        await controller.ScrollToAsync(10_000);

        controller.Metrics.Top.ShouldBe(ContentRows - 4);
    }

    [Fact]
    public async Task MetricsChanged_IsRaisedWhenTheViewScrolls()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = new ScrollController();
        var seen = new List<ScrollViewMetrics>();
        controller.MetricsChanged += seen.Add;
        await using var terminal = await StartAsync(controller, ct: ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        await controller.ScrollToAsync(3);

        await terminal.WaitUntilAsync(_ => seen.Any(m => m.Top == 3), cancellationToken: ct);
    }

    [Fact]
    public async Task ScrollIntoView_BringsAnElementFoundByHookIntoView()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = new ScrollController();
        await using var terminal = await StartAsync(controller, ct: ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        (await controller.ScrollIntoViewAsync("target")).ShouldBeTrue();
        await terminal.WaitForTextAsync("Target", cancellationToken: ct);

        (await controller.ScrollIntoViewAsync("no-such-element")).ShouldBeFalse();
    }

    [Fact]
    public async Task StickToBottom_FollowsGrowingContent_UntilTheUserScrollsUp()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = new ScrollController();
        var grow = new ScrollControllerHost.GrowHandle();
        await using var terminal = await StartAsync(controller, stick: true, grow: grow, ct: ct);
        await terminal.WaitForTextAsync("Target", cancellationToken: ct);

        await grow.Add!();
        await terminal.WaitUntilAsync(_ => controller.Metrics.ContentHeight == ContentRows + 1 && controller.Metrics.IsAtEnd, cancellationToken: ct);
        terminal.CurrentFrame.ContainsText("Target").ShouldBeTrue();

        // Scrolling up leaves the end, so new content no longer moves the view.
        await controller.ScrollByAsync(-5);
        await terminal.WaitUntilAsync(_ => !controller.Metrics.IsAtEnd, cancellationToken: ct);
        var top = controller.Metrics.Top;
        await grow.Add!();
        await terminal.WaitUntilAsync(_ => controller.Metrics.ContentHeight == ContentRows + 2, cancellationToken: ct);
        controller.Metrics.Top.ShouldBe(top);

        // Back at the end, it follows the content again.
        await controller.ScrollToEndAsync();
        await grow.Add!();
        await terminal.WaitUntilAsync(_ => controller.Metrics.ContentHeight == ContentRows + 3 && controller.Metrics.IsAtEnd, cancellationToken: ct);
    }

    [Fact]
    public async Task WithoutStickToBottom_GrowingContentKeepsThePosition()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = new ScrollController();
        var grow = new ScrollControllerHost.GrowHandle();
        await using var terminal = await StartAsync(controller, grow: grow, ct: ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        await grow.Add!();
        await terminal.WaitUntilAsync(_ => controller.Metrics.ContentHeight == ContentRows + 1, cancellationToken: ct);

        controller.Metrics.Top.ShouldBe(0);
    }

    [Fact]
    public async Task ScrollView_WithoutAController_ScrollsWithItsFocusedKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new ScrollController(), focusable: true, ct: ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        await terminal.SendKeyAsync(ConsoleKey.Tab, '\0', cancellationToken: ct);
        await terminal.SendKeyAsync(ConsoleKey.End, '\0', cancellationToken: ct);

        await terminal.WaitForTextAsync("Target", cancellationToken: ct);
    }

    [Fact]
    public async Task Controller_BeforeItIsAttached_DoesNothing()
    {
        var controller = new ScrollController();

        controller.IsAttached.ShouldBeFalse();
        controller.Metrics.ShouldBe(default);
        await controller.ScrollToAsync(3);
        (await controller.ScrollIntoViewAsync("x")).ShouldBeFalse();
    }

    private static Task<HeadlessTerminal> StartAsync(
        ScrollController controller,
        bool stick = false,
        bool focusable = false,
        ScrollControllerHost.GrowHandle? grow = null,
        CancellationToken ct = default)
        => HeadlessTerminal.StartAsync<ScrollControllerHost>(
            new HeadlessTerminalOptions
            {
                Width = 30,
                Height = 8,
                Parameters = new Dictionary<string, object?>
                {
                    [nameof(ScrollControllerHost.Controller)] = controller,
                    [nameof(ScrollControllerHost.StickToBottom)] = stick,
                    [nameof(ScrollControllerHost.Focusable)] = focusable,
                    [nameof(ScrollControllerHost.Grow)] = grow ?? new ScrollControllerHost.GrowHandle(),
                },
            },
            ct);
}
