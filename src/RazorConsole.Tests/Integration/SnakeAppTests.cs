// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Snake.Components;
using RazorConsole.Testing;

namespace RazorConsole.Tests.Integration;

public sealed class SnakeAppTests
{
    [Theory]
    [InlineData(80, 24)]
    [InlineData(100, 40)]
    public async Task WidthAndTraps_FitAlongsideExistingControls(int width, int height)
    {
        await using var terminal = await HeadlessTerminal.StartAsync<App>(width, height,
            cancellationToken: TestContext.Current.CancellationToken,
            configureServices: services => services.AddSingleton(Substitute.For<IHostApplicationLifetime>()));

        terminal.CurrentFrame.ContainsText("WIDTH 01").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.CurrentFrame.ContainsText("TRAPS 00").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.CurrentFrame.ContainsText("REBOOT").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.CurrentFrame.ContainsText("QUIT").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.CurrentFrame.ContainsText("razorconsole.com").ShouldBeTrue(terminal.DumpDiagnostics());
    }
}
