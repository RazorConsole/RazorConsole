// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Tests.Integration.Infrastructure;
using Tutorial.Components.Chapters;

namespace RazorConsole.Tests.Integration;

public sealed class HelloWorldTutorialTests
{
    [Fact]
    public async Task HelloWorld_RendersAndRespondsToItsFocusedButton()
    {
        await using var terminal = await TestTerminal.StartAsync<HelloWorld>(
            60,
            12,
            cancellationToken: TestContext.Current.CancellationToken);

        terminal.Snapshot.ContainsText("Hello, RazorConsole!").ShouldBeTrue();
        terminal.Snapshot.ContainsText("Try the focused button below.").ShouldBeTrue();

        await terminal.SendKeyAsync(
            ConsoleKey.Enter,
            cancellationToken: TestContext.Current.CancellationToken);

        var updated = await terminal.WaitUntilAsync(
            frame => frame.ContainsText("Button pressed 1 time."),
            description: "the tutorial greeting count to update",
            cancellationToken: TestContext.Current.CancellationToken);

        updated.ContainsText("Say hello").ShouldBeTrue();
    }

    [Fact]
    public async Task HelloWorld_SeparateInstancesDoNotShareState()
    {
        await using var first = await TestTerminal.StartAsync<HelloWorld>(
            60,
            12,
            cancellationToken: TestContext.Current.CancellationToken);
        await first.SendKeyAsync(
            ConsoleKey.Enter,
            cancellationToken: TestContext.Current.CancellationToken);
        await first.WaitUntilAsync(
            frame => frame.ContainsText("Button pressed 1 time."),
            cancellationToken: TestContext.Current.CancellationToken);

        await using var restarted = await TestTerminal.StartAsync<HelloWorld>(
            60,
            12,
            cancellationToken: TestContext.Current.CancellationToken);

        restarted.Snapshot.ContainsText("Try the focused button below.").ShouldBeTrue();
        restarted.Snapshot.ContainsText("Button pressed").ShouldBeFalse();
    }

    [Fact]
    public async Task HelloWorld_ReflowsWhenTerminalResizes()
    {
        await using var terminal = await TestTerminal.StartAsync<HelloWorld>(
            60,
            12,
            cancellationToken: TestContext.Current.CancellationToken);

        terminal.Resize(42, 10);

        terminal.Snapshot.Width.ShouldBe(42);
        terminal.Snapshot.Height.ShouldBe(10);
        terminal.Snapshot.ContainsText("Hello, RazorConsole!").ShouldBeTrue();
    }
}
