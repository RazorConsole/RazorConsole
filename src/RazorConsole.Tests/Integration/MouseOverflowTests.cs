// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class MouseOverflowTests
{
    [Fact]
    public async Task Click_OnAnAbsoluteChildOutsideItsParent_ReachesTheChild()
    {
        var ct = TestContext.Current.CancellationToken;
        var clicks = new List<string>();
        await using var terminal = await HeadlessTerminal.StartAsync<OverflowingPopupHost>(
            new HeadlessTerminalOptions
            {
                Width = 20,
                Height = 6,
                Parameters = new Dictionary<string, object?> { [nameof(OverflowingPopupHost.Clicks)] = clicks },
            },
            ct);
        var frame = await terminal.WaitForTextAsync("third", cancellationToken: ct);
        var y = Enumerable.Range(0, frame.Height).First(i => frame.GetLine(i).StartsWith("third", StringComparison.Ordinal));

        await terminal.ClickAsync(1, y, ct);
        await terminal.WaitUntilAsync(_ => clicks.Count > 0, cancellationToken: ct);

        clicks.ShouldBe(["third"]);
    }
}
