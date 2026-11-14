// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core.Layout;
using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class ScrollOverflowTests
{
    [Fact]
    public async Task Panel_OverflowScroll_ShowsTopOfContentAndKeepsBorderFixed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Dictionary<string, object?>(), ct);

        var frame = await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        frame.ContainsText("line 2").ShouldBeTrue();
        frame.ContainsText("line 3").ShouldBeFalse();
        frame.ContainsText("┌").ShouldBeTrue();
        frame.ContainsText("└").ShouldBeTrue();
    }

    [Fact]
    public async Task EngineScroll_RepaintsWithoutRenderingTheHost()
    {
        var ct = TestContext.Current.CancellationToken;
        var counter = new ScrollPanelHost.RenderCounter();
        await using var terminal = await StartAsync(new Dictionary<string, object?> { [nameof(ScrollPanelHost.Counter)] = counter }, ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);
        var rendersBefore = counter.Renders;

        terminal.Services.GetRequiredService<ScrollStateRegistry>().SetTop("log", 5, ScrollChangeSource.Engine);
        var frame = await terminal.WaitForTextAsync("line 5", cancellationToken: ct);

        frame.ContainsText("line 0").ShouldBeFalse();
        frame.ContainsText("line 7").ShouldBeTrue();
        counter.Renders.ShouldBe(rendersBefore);
    }

    [Fact]
    public async Task EngineScroll_RaisesScrollTopChanged_AndBindingEchoDoesNotLoop()
    {
        var ct = TestContext.Current.CancellationToken;
        var changes = new List<int>();
        await using var terminal = await StartAsync(
            new Dictionary<string, object?>
            {
                [nameof(ScrollPanelHost.BindScrollTop)] = true,
                [nameof(ScrollPanelHost.Changes)] = changes,
            },
            ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        terminal.Services.GetRequiredService<ScrollStateRegistry>().SetTop("log", 4, ScrollChangeSource.Engine);
        await terminal.WaitForTextAsync("line 4", cancellationToken: ct);
        await terminal.WaitUntilAsync(_ => changes.Count > 0, cancellationToken: ct);
        await Task.Delay(150, ct);

        changes.ShouldBe([4]);
    }

    [Fact]
    public async Task ScrollTopParameter_IsAppliedOnChangeOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Dictionary<string, object?> { [nameof(ScrollPanelHost.ScrollTop)] = 3 }, ct);

        var frame = await terminal.WaitForTextAsync("line 3", cancellationToken: ct);

        frame.ContainsText("line 0").ShouldBeFalse();
    }

    [Fact]
    public async Task EngineScroll_PastTheEnd_IsClampedByLayoutInTheSameFrame()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var terminal = await StartAsync(new Dictionary<string, object?>(), ct);
        await terminal.WaitForTextAsync("line 0", cancellationToken: ct);

        var registry = terminal.Services.GetRequiredService<ScrollStateRegistry>();
        registry.SetTop("log", 500, ScrollChangeSource.Engine);
        var frame = await terminal.WaitForTextAsync("line 19", cancellationToken: ct);

        frame.ContainsText("line 17").ShouldBeTrue();
        registry.Get("log").Top.ShouldBe(17);
    }

    private static Task<HeadlessTerminal> StartAsync(Dictionary<string, object?> parameters, CancellationToken ct)
        => HeadlessTerminal.StartAsync<ScrollPanelHost>(
            new HeadlessTerminalOptions { Width = 30, Height = 8, Parameters = parameters },
            ct);
}
