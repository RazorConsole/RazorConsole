// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core.Input;
using RazorConsole.Testing;
using RazorConsole.Tests.Integration.Fixtures;

namespace RazorConsole.Tests.Integration;

public sealed class MouseStaleHandlerTests
{
    [Fact]
    public async Task Click_OnAHandlerThatReRenderedAfterTheHit_ReachesTheNewestHandler()
    {
        var ct = TestContext.Current.CancellationToken;
        var clicks = new List<string>();
        Action? rerender = null;
        await using var terminal = await HeadlessTerminal.StartAsync<StaleClickHost>(
            new HeadlessTerminalOptions
            {
                Width = 20,
                Height = 3,
                Parameters = new Dictionary<string, object?>
                {
                    [nameof(StaleClickHost.Clicks)] = clicks,
                    [nameof(StaleClickHost.Register)] = (Action<Action>)(action => rerender = action),
                },
                ConfigureServices = services =>
                {
                    services.AddSingleton<IKeyboardEventDispatcher>(sp =>
                        new ReRenderOnceDispatcher(sp.GetRequiredService<RendererKeyboardEventDispatcher>(), () => rerender?.Invoke()));
                },
            },
            ct);
        await terminal.WaitForTextAsync("clickme 0", cancellationToken: ct);

        await terminal.ClickAsync(1, 0, ct);
        await terminal.WaitUntilAsync(_ => clicks.Count > 0, cancellationToken: ct);

        clicks.ShouldBe(["render 1"]);
    }

    // Re-renders the component right before the first click is delivered, so the id the manager holds is stale.
    private sealed class ReRenderOnceDispatcher(IKeyboardEventDispatcher inner, Action rerender) : IKeyboardEventDispatcher
    {
        private int _done;

        public async Task DispatchAsync(ulong handlerId, EventArgs eventArgs, CancellationToken cancellationToken)
        {
            if (eventArgs is MouseEventArgs { Type: "click" } && Interlocked.Exchange(ref _done, 1) == 0)
            {
                rerender();
                await Task.Delay(100, cancellationToken);
            }

            await inner.DispatchAsync(handlerId, eventArgs, cancellationToken);
        }
    }
}
