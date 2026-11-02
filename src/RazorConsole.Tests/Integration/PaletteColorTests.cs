// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class PaletteColorTests
{
    [Fact]
    public async Task TableBorder_InAPaletteColor_KeepsThePaletteColor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await HeadlessTerminal.StartAsync<PaletteColorHost>(
            new HeadlessTerminalOptions { Width = 20, Height = 6 },
            ct);
        var frame = await terminal.WaitForTextAsync("row", cancellationToken: ct);

        // Markup names a palette color ("blue"); RGB would be "#0000ff", which ignores the terminal's theme.
        frame[0, 0].Style?.Foreground.ToMarkup().ShouldBe("blue");
    }
}
