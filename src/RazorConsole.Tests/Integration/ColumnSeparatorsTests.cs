// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class ColumnSeparatorsTests
{
    [Fact]
    public async Task SpectreTable_ColumnSeparatorsFalse_DrawsNoLinesBetweenColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await HeadlessTerminal.StartAsync<ColumnSeparatorsHost>(
            new HeadlessTerminalOptions { Width = 20, Height = 6 },
            ct);
        var frame = await terminal.WaitForTextAsync("file", cancellationToken: ct);

        frame.GetLine(1).TrimEnd().ShouldBe("│ name   type │");
        frame.GetLine(2).TrimEnd().ShouldBe("├─────────────┤");
        frame.GetLine(3).TrimEnd().ShouldBe("│ row    file │");
    }
}
