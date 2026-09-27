// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Tests.Integration.Infrastructure;
using Tutorial.Components.Chapters;

namespace RazorConsole.Tests.Integration;

public sealed class TutorialChapterTests
{
    [Fact]
    public async Task StateAndEvents_UpdatesStateFromFocusedButtons()
    {
        await using var terminal = await Start<StateAndEvents>(70, 14);
        terminal.Snapshot.ContainsText("Count: 0").ShouldBeTrue();

        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(
            frame => frame.ContainsText("Count: -1") && frame.ContainsText("Decrement callback handled."),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TextInputAndFocus_AcceptsRealTerminalInput()
    {
        await using var terminal = await Start<TextInputAndFocus>(80, 18);

        foreach (var character in "Ada")
        {
            await terminal.SendTerminalInputAsync(character.ToString(), TestContext.Current.CancellationToken);
        }

        await terminal.WaitUntilAsync(
            frame => frame.ContainsText("Name: Ada"),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MouseEvents_HandlesHoverClickWheelAndCapturedDrag()
    {
        await using var terminal = await Start<MouseEvents>(80, 28);
        var action = terminal.GetLayout("mouse-action");
        var actionX = action.Left!.Value + 2;
        var actionY = action.Top!.Value + 1;

        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, actionX, actionY), TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(frame => frame.ContainsText("Hover active"), cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, actionX, actionY), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, actionX, actionY), TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(frame => frame.ContainsText("Clicks: 1"), cancellationToken: TestContext.Current.CancellationToken);

        var surface = terminal.GetLayout("mouse-surface");
        await terminal.SendMouseAsync(
            new(TerminalMouseKind.Wheel, surface.Left!.Value + 40, surface.Top!.Value + 5, DeltaY: 3),
            TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(frame => frame.ContainsText("Wheel: +1"), cancellationToken: TestContext.Current.CancellationToken);

        var card = terminal.GetLayout("mouse-drag-card");
        var cardX = card.Left!.Value + 2;
        var cardY = card.Top!.Value + 1;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, cardX, cardY), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, cardX + 4, cardY + 2), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, 79, 27), TestContext.Current.CancellationToken);

        await terminal.WaitUntilAsync(
            frame => frame.ContainsText("Card: 36, 5 · Ready"),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WidgetLayoutAndResize_ReflowsAtNarrowWidth()
    {
        await using var terminal = await Start<WidgetLayoutAndResize>(80, 28);
        terminal.Resize(44, 32);

        terminal.Snapshot.Width.ShouldBe(44);
        terminal.Snapshot.ContainsText("Build").ShouldBeTrue();
        terminal.Snapshot.ContainsText("Warnings").ShouldBeTrue();
    }

    [Fact]
    public async Task AsyncWork_TransitionsFromLoadingToSuccess()
    {
        await using var terminal = await Start<AsyncWork>(80, 18);
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);

        await terminal.WaitUntilAsync(
            frame => frame.ContainsText("Loaded 3 releases successfully."),
            timeout: TimeSpan.FromSeconds(3),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RoutingDemo_NavigatesBetweenPagesAndNotFound()
    {
        await using var terminal = await Start<RoutingDemo>(80, 18);

        await terminal.WaitUntilAsync(
            frame => frame.ContainsText("Welcome to the routed dashboard."),
            cancellationToken: TestContext.Current.CancellationToken);

        await terminal.SendKeyAsync(ConsoleKey.Tab, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(
            frame => frame.ContainsText("Notifications: On"),
            cancellationToken: TestContext.Current.CancellationToken);

        await terminal.SendKeyAsync(ConsoleKey.Tab, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(
            frame => frame.ContainsText("Page not found."),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CompleteApp_RendersIntegratedTaskBoard()
    {
        await using var terminal = await Start<CompleteApp>(80, 28);

        terminal.Snapshot.ContainsText("Task Board").ShouldBeTrue();
        terminal.Snapshot.ContainsText("1/3 complete").ShouldBeTrue();
        terminal.Snapshot.ContainsText("Ship an app").ShouldBeTrue();
    }

    private static Task<TestTerminal> Start<TComponent>(int width, int height)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
        => TestTerminal.StartAsync<TComponent>(
            width,
            height,
            cancellationToken: TestContext.Current.CancellationToken);
}
