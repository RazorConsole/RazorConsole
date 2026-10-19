// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core.Input;
using RazorConsole.Testing;

namespace RazorConsole.Tests.Integration;

public sealed class HeadlessTerminalTests
{
    [Fact]
    public async Task TypeAsync_MapsCharactersToKeys_LikeAVtTerminal()
    {
        var events = new List<EventArgs>();
        await using var terminal = await StartKeySurfaceAsync(events);

        await terminal.TypeAsync("a B\r\n\t\b\u007f\u0003", TestContext.Current.CancellationToken);

        var keys = events.Cast<KeyboardEventArgs>()
            .Select(e => (e.Key, e.Code, e.ShiftKey, e.CtrlKey))
            .ToArray();
        keys.ShouldBe(new[]
        {
            ("a", "KeyA", false, false),
            (" ", "Spacebar", false, false),
            ("B", "KeyB", true, false),
            ("Enter", "Enter", false, false),
            ("Enter", "Enter", false, false),
            ("Tab", "Tab", false, false),
            ("Backspace", "Backspace", false, false),
            ("Backspace", "Backspace", false, false),
            ("C", "KeyC", false, true),
        });
    }

    [Fact]
    public async Task TypeAsync_AndPasteAsync_RejectEscape()
    {
        var events = new List<EventArgs>();
        await using var terminal = await StartKeySurfaceAsync(events);

        await Should.ThrowAsync<ArgumentException>(() => terminal.TypeAsync("a\u001b[A", TestContext.Current.CancellationToken));
        await Should.ThrowAsync<ArgumentException>(() => terminal.PasteAsync("a\u001b", TestContext.Current.CancellationToken));
        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task PasteAsync_DispatchesOneInputPerTextRun_AndHandlesEnterAndTabAsKeys()
    {
        var events = new List<string>();
        await using var terminal = await HeadlessTerminal.StartAsync<TextFields>(
            new HeadlessTerminalOptions
            {
                Width = 30,
                Height = 4,
                Parameters = new Dictionary<string, object?> { [nameof(TextFields.Events)] = events },
            },
            TestContext.Current.CancellationToken);
        terminal.FocusKey.ShouldBe("first");

        await terminal.PasteAsync("ab\rcd\tef", TestContext.Current.CancellationToken);

        events.ShouldBe(new[]
        {
            "first input ab",
            "first keydown Enter",
            "first input abcd",
            "first keydown Tab",
            "second input ef",
        });
        terminal.FocusKey.ShouldBe("second");
    }

    [Fact]
    public async Task Host_UsesHeadlessConsoleInput_NotTheRealConsole()
    {
        await using var terminal = await StartKeySurfaceAsync([]);

        var inputs = terminal.Services.GetServices<IConsoleInput>().ToArray();

        inputs.ShouldNotBeEmpty();
        foreach (var input in inputs)
        {
            input.ShouldNotBeOfType<ConsoleInput>();
            input.GetType().Assembly.ShouldBe(typeof(HeadlessTerminal).Assembly);
        }
    }

    [Fact]
    public async Task WaitUntilAsync_Timeout_ThrowsWithDiagnostics()
    {
        await using var terminal = await StartKeySurfaceAsync([]);

        var exception = await Should.ThrowAsync<TerminalTimeoutException>(
            () => terminal.WaitUntilAsync(_ => false, TimeSpan.FromMilliseconds(50), "an impossible state", TestContext.Current.CancellationToken));

        exception.ShouldBeAssignableTo<TimeoutException>();
        exception.Message.ShouldContain("an impossible state");
        exception.Diagnostics.ShouldContain("Captured frames:");
        exception.Diagnostics.ShouldContain("Last key: <none>");
        exception.Message.ShouldContain(exception.Diagnostics);
    }

    [Fact]
    public async Task WaitForTextAsync_ReturnsMatchingFrame_OrThrowsOnTimeout()
    {
        await using var terminal = await StartKeySurfaceAsync([]);

        await terminal.SendKeyAsync(ConsoleKey.X, 'x', cancellationToken: TestContext.Current.CancellationToken);
        var frame = await terminal.WaitForTextAsync("Last key: x", cancellationToken: TestContext.Current.CancellationToken);
        frame.ContainsText("Last key: x").ShouldBeTrue();

        var exception = await Should.ThrowAsync<TerminalTimeoutException>(
            () => terminal.WaitForTextAsync("Last key: y", TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));
        exception.Message.ShouldContain("text 'Last key: y'");
        exception.Diagnostics.ShouldContain("Last key: x");
    }

    [Fact]
    public async Task ClickAsync_SendsLeftButtonPressAndRelease()
    {
        var events = new List<EventArgs>();
        await using var terminal = await StartKeySurfaceAsync(events);

        await terminal.ClickAsync(3, 1, TestContext.Current.CancellationToken);

        events.Cast<MouseEventArgs>().Select(e => (e.Type, e.Button, e.ClientX, e.ClientY))
            .ShouldBe(new[]
            {
                ("mousedown", 0L, 3.0, 1.0),
                ("mouseup", 0L, 3.0, 1.0),
                ("click", 0L, 3.0, 1.0),
            });
    }

    [Fact]
    public async Task ConcurrentInput_IsSerialized()
    {
        var events = new List<EventArgs>();
        await using var terminal = await StartKeySurfaceAsync(events);

        var typing = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => terminal.TypeAsync("abc", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken))
            .ToArray();
        await Task.WhenAll(typing);

        var keys = string.Concat(events.Cast<KeyboardEventArgs>().Select(e => e.Key));
        keys.ShouldBe(string.Concat(Enumerable.Repeat("abc", 20)));
    }

    private static Task<HeadlessTerminal> StartKeySurfaceAsync(List<EventArgs> events)
        => HeadlessTerminal.StartAsync<KeySurface>(
            new HeadlessTerminalOptions
            {
                Width = 30,
                Height = 4,
                Parameters = new Dictionary<string, object?> { [nameof(KeySurface.Events)] = events },
            },
            TestContext.Current.CancellationToken);

    public sealed class KeySurface : ComponentBase
    {
        private string? _lastKey;

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
            builder.AddAttribute(7, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, e =>
            {
                Events.Add(e);
                _lastKey = e.Key;
            }));
            builder.AddAttribute(8, "onmousedown", EventCallback.Factory.Create<MouseEventArgs>(this, e => Events.Add(e)));
            builder.AddAttribute(9, "onmouseup", EventCallback.Factory.Create<MouseEventArgs>(this, e => Events.Add(e)));
            builder.AddAttribute(10, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, e => Events.Add(e)));
            builder.AddContent(11, $"Last key: {_lastKey ?? "<none>"}");
            builder.CloseElement();
        }
    }

    public sealed class TextFields : ComponentBase
    {
        private readonly Dictionary<string, (EventCallback<ChangeEventArgs> Input, EventCallback<KeyboardEventArgs> KeyDown)> _handlers = [];

        [Parameter] public List<string> Events { get; set; } = [];

        protected override void OnInitialized()
        {
            // Create the handlers once so re-renders keep their handler IDs.
            foreach (var key in new[] { "first", "second" })
            {
                _handlers[key] = (
                    EventCallback.Factory.Create<ChangeEventArgs>(this, e => Events.Add($"{key} input {e.Value}")),
                    EventCallback.Factory.Create<KeyboardEventArgs>(this, e => Events.Add($"{key} keydown {e.Key}")));
            }
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            Field(builder, 1, "first");
            Field(builder, 2, "second");
            builder.CloseElement();
        }

        private void Field(RenderTreeBuilder builder, int sequence, string key)
        {
            builder.OpenRegion(sequence);
            builder.OpenElement(0, "div");
            builder.SetKey(key);
            builder.AddAttribute(1, "data-focusable", "true");
            builder.AddAttribute(2, "oninput", _handlers[key].Input);
            builder.AddAttribute(3, "onkeydown", _handlers[key].KeyDown);
            builder.AddContent(4, key);
            builder.CloseElement();
            builder.CloseRegion();
        }
    }
}
