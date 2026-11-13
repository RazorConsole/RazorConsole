// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class ScrollbarMouseTests
{
    // The panel is 11 columns wide (7 for "line N", one column of padding on each side and the border), so its scrollbar sits on column 10.
    private const int BarX = 10;

    [Fact]
    public async Task Scrollbar_IsPaintedOverTheRightBorder_AndPublishedToLayout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(fill: false, ct);

        var frame = await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        frame.GetLine(1)[BarX].ShouldBe('█');
        frame.GetLine(2)[BarX].ShouldBe('│');
        frame.GetLine(4)[BarX].ShouldBe('┘');
    }

    [Fact]
    public async Task ClickingBelowTheThumb_PagesDown_AndAboveItPagesBackUp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(fill: false, ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        // Each click below the thumb moves one page (3 rows) down.
        await terminal.ClickAsync(BarX, 3, ct);
        var down = await terminal.WaitForTextAsync("line 3", cancellationToken: ct);
        down.ContainsText("line 0").ShouldBeFalse();
        await terminal.ClickAsync(BarX, 3, ct);
        await terminal.WaitForTextAsync("line 6", cancellationToken: ct);
        await terminal.ClickAsync(BarX, 3, ct);
        await terminal.WaitForTextAsync("line 9", cancellationToken: ct);

        // The thumb has left the top of the track, so a click above it pages back up.
        await terminal.ClickAsync(BarX, 1, ct);
        var up = await terminal.WaitForTextAsync("line 6", cancellationToken: ct);
        up.ContainsText("line 9").ShouldBeFalse();
    }

    [Fact]
    public async Task DraggingTheThumb_ScrollsProportionally_AndStopsOnRelease()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(fill: false, ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Down, BarX, 1, Button: 0), ct);
        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Move, BarX, 3, Button: 0), ct);
        var dragged = await terminal.WaitForTextAsync("line 19", cancellationToken: ct);
        dragged.ContainsText("line 17").ShouldBeTrue();

        // The pointer may leave the track sideways while dragging; only its row matters.
        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Move, 0, 3, Button: 0), ct);
        await terminal.WaitForTextAsync("line 17", cancellationToken: ct);
        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Up, 0, 3, Button: 0), ct);

        // Released: moving no longer scrolls.
        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Move, BarX, 1, Button: 0), ct);
        await Task.Delay(100, ct);
        terminal.CurrentFrame.ContainsText("line 17").ShouldBeTrue();
    }

    [Fact]
    public async Task DraggingTheThumb_FollowsAResizeDuringTheDrag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(fill: true, ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Down, BarX, 1, Button: 0), ct);
        terminal.Resize(30, 12);
        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Move, BarX, 5, Button: 0), ct);

        // The track is now 10 rows and the thumb 5; the thumb top is 4, which is 8 of the 10 scrollable rows.
        var frame = await terminal.WaitForTextAsync("line 8", cancellationToken: ct);
        frame.ContainsText("line 0").ShouldBeFalse();
        frame.ContainsText("line 17").ShouldBeTrue();

        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Up, BarX, 5, Button: 0), ct);
    }

    [Fact]
    public async Task PressingOutsideTheScrollbar_DoesNotScroll()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(fill: false, ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        await terminal.ClickAsync(2, 3, ct);
        await Task.Delay(100, ct);

        terminal.CurrentFrame.ContainsText("line 0").ShouldBeTrue();
    }

    private static Task<HeadlessTerminal> StartAsync(bool fill, CancellationToken ct)
        => HeadlessTerminal.StartAsync<ScrollPanelHost>(
            new HeadlessTerminalOptions
            {
                Width = 30,
                Height = 8,
                Parameters = new Dictionary<string, object?> { [nameof(ScrollPanelHost.Fill)] = fill },
            },
            ct);
}
