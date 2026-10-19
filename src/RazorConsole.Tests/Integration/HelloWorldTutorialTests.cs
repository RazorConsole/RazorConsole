// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Testing;
using Tutorial.Components.Chapters;

namespace RazorConsole.Tests.Integration;

public sealed class HelloWorldTutorialTests
{
    [Fact]
    public async Task HelloWorld_RendersAndRespondsToItsFocusedButton()
    {
        await using var terminal = await HeadlessTerminal.StartAsync<HelloWorld>(
            60,
            12,
            cancellationToken: TestContext.Current.CancellationToken);

        terminal.CurrentFrame.ContainsText("Hello, RazorConsole!").ShouldBeTrue();
        terminal.CurrentFrame.ContainsText("Try the focused button below.").ShouldBeTrue();

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
        await using var first = await HeadlessTerminal.StartAsync<HelloWorld>(
            60,
            12,
            cancellationToken: TestContext.Current.CancellationToken);
        await first.SendKeyAsync(
            ConsoleKey.Enter,
            cancellationToken: TestContext.Current.CancellationToken);
        await first.WaitUntilAsync(
            frame => frame.ContainsText("Button pressed 1 time."),
            cancellationToken: TestContext.Current.CancellationToken);

        await using var restarted = await HeadlessTerminal.StartAsync<HelloWorld>(
            60,
            12,
            cancellationToken: TestContext.Current.CancellationToken);

        restarted.CurrentFrame.ContainsText("Try the focused button below.").ShouldBeTrue();
        restarted.CurrentFrame.ContainsText("Button pressed").ShouldBeFalse();
    }

    [Fact]
    public async Task HelloWorld_ReflowsWhenTerminalResizes()
    {
        await using var terminal = await HeadlessTerminal.StartAsync<HelloWorld>(
            60,
            12,
            cancellationToken: TestContext.Current.CancellationToken);

        terminal.Resize(42, 10);

        terminal.CurrentFrame.Width.ShouldBe(42);
        terminal.CurrentFrame.Height.ShouldBe(10);
        terminal.CurrentFrame.ContainsText("Hello, RazorConsole!").ShouldBeTrue();
    }
}
