// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using RazorConsole.Tests.Integration.Infrastructure;

namespace RazorConsole.Tests.Integration;

public sealed class TerminalInputStreamTests
{
    [Fact]
    public async Task RawStream_DispatchesKeysClickAndWheel_ToRazorHandlers()
    {
        var events = new List<EventArgs>();
        await using var terminal = await StartAsync(events);
        await terminal.SendTerminalInputAsync("a\u001b[<0;3;2M\u001b[<0;3;2m\u001b[<65;3;2Mb", TestContext.Current.CancellationToken);

        events.Select(e => e is KeyboardEventArgs key ? key.Key : ((MouseEventArgs)e).Type)
            .ShouldBe(new[] { "a", "mousedown", "mouseup", "click", "wheel", "b" });
        var down = events[1].ShouldBeOfType<MouseEventArgs>();
        down.ClientX.ShouldBe(2);
        down.ClientY.ShouldBe(1);
        down.OffsetX.ShouldBe(2);
        down.OffsetY.ShouldBe(1);
        events[4].ShouldBeOfType<WheelEventArgs>().DeltaY.ShouldBe(3);
    }

    [Fact]
    public async Task FragmentedMousePacket_DoesNotLeakIntoKeyboard_AndDragStaysCaptured()
    {
        var events = new List<EventArgs>();
        await using var terminal = await StartAsync(events);
        await terminal.SendTerminalInputAsync("\u001b[<0;3;", TestContext.Current.CancellationToken);
        await terminal.SendTerminalInputAsync("", TestContext.Current.CancellationToken);
        events.ShouldBeEmpty();
        await terminal.SendTerminalInputAsync("2M\u001b[<32;40;20M\u001b[<0;40;20m", TestContext.Current.CancellationToken);
        events.Select(e => ((MouseEventArgs)e).Type).ShouldBe(new[] { "mousedown", "mousemove", "mouseup" });
    }

    [Fact]
    public async Task Resize_UpdatesHitBounds_ForRawMouseInput()
    {
        var events = new List<EventArgs>();
        await using var terminal = await StartAsync(events);
        await terminal.SendTerminalInputAsync("\u001b[<65;25;2M", TestContext.Current.CancellationToken);
        events.ShouldBeEmpty();
        terminal.Resize(30, 4);
        await terminal.SendTerminalInputAsync("\u001b[<65;25;2M", TestContext.Current.CancellationToken);
        events.ShouldHaveSingleItem().ShouldBeOfType<WheelEventArgs>();
    }

    [Fact]
    public async Task IdleFlush_ResolvesEscapeOnce()
    {
        var events = new List<EventArgs>();
        await using var terminal = await StartAsync(events);
        await terminal.SendTerminalInputAsync("\u001b", TestContext.Current.CancellationToken);
        events.ShouldBeEmpty();
        await Task.Delay(80, TestContext.Current.CancellationToken);
        await terminal.SendTerminalInputAsync("", TestContext.Current.CancellationToken);
        await terminal.SendTerminalInputAsync("", TestContext.Current.CancellationToken);
        events.ShouldHaveSingleItem().ShouldBeAssignableTo<KeyboardEventArgs>().Key.ShouldBe("Escape");
    }

    private static Task<TestTerminal> StartAsync(List<EventArgs> events)
        => TestTerminal.StartAsync<InputSurface>(20, 4,
            new Dictionary<string, object?> { [nameof(InputSurface.Events)] = events },
            TestContext.Current.CancellationToken);

    public sealed class InputSurface : ComponentBase
    {
        [Parameter] public List<EventArgs> Events { get; set; } = [];

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "data-layout", "flex");
            builder.AddAttribute(2, "data-fill-width", "true");
            builder.AddAttribute(3, "data-height", "4");
            builder.AddAttribute(4, "data-focusable", "true");
            builder.AddAttribute(5, "data-focus-key", "surface");
            builder.AddAttribute(6, "data-input-managed", "true");
            builder.AddAttribute(7, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, e => Events.Add(e)));
            builder.AddAttribute(8, "onmousedown", EventCallback.Factory.Create<MouseEventArgs>(this, e => Events.Add(e)));
            builder.AddAttribute(9, "onmouseup", EventCallback.Factory.Create<MouseEventArgs>(this, e => Events.Add(e)));
            builder.AddAttribute(10, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, e => Events.Add(e)));
            builder.AddAttribute(11, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(this, e => Events.Add(e)));
            builder.AddAttribute(12, "onmousemove", EventCallback.Factory.Create<MouseEventArgs>(this, e => Events.Add(e)));
            builder.AddContent(13, "input surface");
            builder.CloseElement();
        }
    }
}
