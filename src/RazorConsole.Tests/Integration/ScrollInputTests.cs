// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class ScrollInputTests
{
    // Inside the inner container: row 0 is the outer border, row 1 the inner border, rows 2 and 3 the inner content.
    private const int InnerX = 3;
    private const int InnerY = 2;

    [Fact]
    public async Task Wheel_ScrollsTheInnermostContainerFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Options(), ct);

        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Wheel, InnerX, InnerY, DeltaY: 3), ct);

        var frame = await terminal.WaitForTextAsync("inner 3", cancellationToken: ct);
        frame.ContainsText("inner 0").ShouldBeFalse();
        frame.ContainsText("outer 4").ShouldBeFalse();
    }

    [Fact]
    public async Task Wheel_ChainsToTheOuterContainerOnceTheInnerOneIsAtItsLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Options(), ct);

        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Wheel, InnerX, InnerY, DeltaY: 100), ct);
        await terminal.WaitForTextAsync("inner 9", cancellationToken: ct);
        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Wheel, InnerX, InnerY, DeltaY: 3), ct);

        await terminal.WaitForTextAsync("outer 4", cancellationToken: ct);
    }

    [Fact]
    public async Task Wheel_OverscrollContain_KeepsTheOuterContainerStill()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Options { ContainInner = true }, ct);

        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Wheel, InnerX, InnerY, DeltaY: 100), ct);
        await terminal.WaitForTextAsync("inner 9", cancellationToken: ct);
        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Wheel, InnerX, InnerY, DeltaY: 3), ct);
        await Task.Delay(300, ct);

        terminal.CurrentFrame.ContainsText("outer 4").ShouldBeFalse();
        terminal.CurrentFrame.ContainsText("inner 9").ShouldBeTrue();
    }

    [Fact]
    public async Task Wheel_PreventDefault_SuppressesNativeScrolling_ButTheHandlerStillRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var wheels = new List<string>();
        await using var terminal = await StartAsync(new Options { PreventOuterWheel = true, Wheels = wheels }, ct);

        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Wheel, InnerX, InnerY, DeltaY: 3), ct);
        await terminal.WaitUntilAsync(_ => wheels.Count > 0, cancellationToken: ct);
        await Task.Delay(300, ct);

        wheels.ShouldBe(["outer"]);
        terminal.CurrentFrame.ContainsText("inner 0").ShouldBeTrue();
    }

    [Fact]
    public async Task Wheel_WithoutPreventDefault_StillReachesTheHandler()
    {
        var ct = TestContext.Current.CancellationToken;
        var wheels = new List<string>();
        await using var terminal = await StartAsync(new Options { Wheels = wheels }, ct);

        await terminal.SendMouseAsync(new TerminalMouseEvent(TerminalMouseKind.Wheel, InnerX, InnerY, DeltaY: 3), ct);

        await terminal.WaitForTextAsync("inner 3", cancellationToken: ct);
        wheels.ShouldBe(["outer"]);
    }

    [Fact]
    public async Task FocusedContainer_ScrollsWithPageKeysArrowsHomeAndEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Options { FocusableOuter = true }, ct);
        await terminal.SendKeyAsync(ConsoleKey.Tab, '\0', cancellationToken: ct);

        await terminal.SendKeyAsync(ConsoleKey.PageDown, '\0', cancellationToken: ct);
        var page = await terminal.WaitForTextAsync("outer 7", cancellationToken: ct);
        page.ContainsText("inner 0").ShouldBeFalse();

        await terminal.SendKeyAsync(ConsoleKey.Home, '\0', cancellationToken: ct);
        await terminal.WaitForTextAsync("inner 0", cancellationToken: ct);

        for (var i = 0; i < 4; i++)
        {
            await terminal.SendKeyAsync(ConsoleKey.DownArrow, '\0', cancellationToken: ct);
        }

        await terminal.WaitForTextAsync("outer 5", cancellationToken: ct);
        await terminal.SendKeyAsync(ConsoleKey.UpArrow, '\0', cancellationToken: ct);
        await terminal.WaitUntilAsync(frame => !frame.ContainsText("outer 5"), cancellationToken: ct);

        await terminal.SendKeyAsync(ConsoleKey.End, '\0', cancellationToken: ct);
        await terminal.WaitForTextAsync("Last", cancellationToken: ct);
    }

    [Fact]
    public async Task ContainerIsNotATabStopByDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Options(), ct);

        // Only the button is focusable, so arrows on the unfocused container do nothing.
        await terminal.SendKeyAsync(ConsoleKey.DownArrow, '\0', cancellationToken: ct);
        await Task.Delay(300, ct);

        terminal.CurrentFrame.ContainsText("inner 0").ShouldBeTrue();
    }

    [Fact]
    public async Task FocusingAnOffScreenButton_ScrollsItIntoView()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Options(), ct);
        await terminal.WaitForTextAsync("inner 0", cancellationToken: ct);

        await terminal.SendKeyAsync(ConsoleKey.Tab, '\0', cancellationToken: ct);

        await terminal.WaitForTextAsync("Last", cancellationToken: ct);
    }

    [Fact]
    public async Task PageKeysFromAFocusedButton_BubbleToTheScrollContainer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Options(), ct);
        await terminal.SendKeyAsync(ConsoleKey.Tab, '\0', cancellationToken: ct);
        await terminal.WaitForTextAsync("Last", cancellationToken: ct);

        await terminal.SendKeyAsync(ConsoleKey.PageUp, '\0', cancellationToken: ct);
        var up = await terminal.WaitUntilAsync(frame => !frame.ContainsText("Last"), cancellationToken: ct);
        up.ContainsText("outer 6").ShouldBeTrue();

        await terminal.SendKeyAsync(ConsoleKey.Home, '\0', cancellationToken: ct);
        await terminal.WaitForTextAsync("inner 0", cancellationToken: ct);
    }

    private static Task<HeadlessTerminal> StartAsync(Options options, CancellationToken ct)
        => HeadlessTerminal.StartAsync<ScrollInputHost>(
            new HeadlessTerminalOptions
            {
                Width = 30,
                Height = 10,
                Parameters = new Dictionary<string, object?>
                {
                    [nameof(ScrollInputHost.FocusableOuter)] = options.FocusableOuter,
                    [nameof(ScrollInputHost.ContainInner)] = options.ContainInner,
                    [nameof(ScrollInputHost.PreventOuterWheel)] = options.PreventOuterWheel,
                    [nameof(ScrollInputHost.Wheels)] = options.Wheels,
                },
            },
            ct);

    private sealed class Options
    {
        public bool FocusableOuter { get; init; }

        public bool ContainInner { get; init; }

        public bool PreventOuterWheel { get; init; }

        public List<string> Wheels { get; init; } = [];
    }
}
