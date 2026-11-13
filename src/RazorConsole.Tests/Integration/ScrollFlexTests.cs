// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Core.Layout;
using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class ScrollFlexTests
{
    [Theory]
    [InlineData("rows")]
    [InlineData("flex")]
    public async Task RowsAndFlex_ScrollTheirItemsInsideTheGivenHeight(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = new ScrollController();
        await using var terminal = await StartAsync(kind, controller, ct);

        var first = await terminal.WaitForTextAsync("row 0", cancellationToken: ct);
        first.ContainsText("row 3").ShouldBeTrue();
        first.ContainsText("row 4").ShouldBeFalse();
        controller.Metrics.ContentHeight.ShouldBe(12);
        controller.Metrics.ViewportHeight.ShouldBe(4);

        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Wheel, 1, 1, DeltaY: 3), ct);

        var scrolled = await terminal.WaitForTextAsync("row 6", cancellationToken: ct);
        scrolled.ContainsText("row 0").ShouldBeFalse();
    }

    [Fact]
    public async Task Columns_AcceptOverflow_AndAttachAController()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = new ScrollController();
        await using var terminal = await StartAsync("columns", controller, ct);
        await terminal.WaitForTextAsync("c0", cancellationToken: ct);

        controller.IsAttached.ShouldBeTrue();
        controller.Metrics.ContentHeight.ShouldBe(1);
    }

    private static Task<HeadlessTerminal> StartAsync(string kind, ScrollController controller, CancellationToken ct)
        => HeadlessTerminal.StartAsync<ScrollFlexHost>(
            new HeadlessTerminalOptions
            {
                Width = 20,
                Height = 8,
                Parameters = new Dictionary<string, object?>
                {
                    [nameof(ScrollFlexHost.Kind)] = kind,
                    [nameof(ScrollFlexHost.Controller)] = controller,
                },
            },
            ct);
}
