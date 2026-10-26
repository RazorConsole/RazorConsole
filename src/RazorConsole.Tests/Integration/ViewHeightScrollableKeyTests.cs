// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class ViewHeightScrollableKeyTests
{
    [Fact]
    public async Task FocusOrderWins_AndUnhandledKeysAreForwarded()
    {
        var ct = TestContext.Current.CancellationToken;
        var forwarded = new List<string>();
        await using var terminal = await HeadlessTerminal.StartAsync<ScrollableKeysHost>(
            new HeadlessTerminalOptions
            {
                Width = 20,
                Height = 4,
                Parameters = new Dictionary<string, object?> { [nameof(ScrollableKeysHost.Forwarded)] = forwarded },
            },
            ct);
        await terminal.WaitForTextAsync("line 1", cancellationToken: ct);

        await terminal.SendKeyAsync(ConsoleKey.DownArrow, '\0', cancellationToken: ct);
        await terminal.SendKeyAsync(ConsoleKey.F1, '\0', cancellationToken: ct);

        await terminal.WaitUntilAsync(_ => forwarded.Count > 0, cancellationToken: ct);
        forwarded.ShouldBe(["F1"]);
    }
}
