// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class MouseStopPropagationTests
{
    [Fact]
    public async Task Click_StopPropagation_DoesNotReachTheParentHandler()
    {
        var ct = TestContext.Current.CancellationToken;
        var clicks = new List<string>();
        await using var terminal = await HeadlessTerminal.StartAsync<StopPropagationHost>(
            new HeadlessTerminalOptions
            {
                Width = 20,
                Height = 3,
                Parameters = new Dictionary<string, object?> { [nameof(StopPropagationHost.Clicks)] = clicks },
            },
            ct);
        await terminal.WaitForTextAsync("inner", cancellationToken: ct);

        await terminal.ClickAsync(1, 0, ct);
        await terminal.WaitUntilAsync(_ => clicks.Count > 0, cancellationToken: ct);
        await Task.Delay(100, ct);

        clicks.ShouldBe(["inner"]);
    }
}
