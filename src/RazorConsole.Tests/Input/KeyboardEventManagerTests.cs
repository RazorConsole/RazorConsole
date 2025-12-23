// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Input;

public class KeyboardEventManagerTests
{
    [Fact]
    public async Task HandleKeyAsync_Enter_DispatchesClickAndChange()
    {
        await using var harness = await KeyboardHarness.CreateAsync(
            new FocusElementSpec(
                key: "first",
                value: "seed",
                events: new Dictionary<string, ulong>
                {
                    ["onclick"] = 1,
                    ["oninput"] = 2,
                    ["onchange"] = 3,
                }));

        var enter = new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false);
        await harness.Manager.HandleKeyAsync(enter, CancellationToken.None);

        harness.Dispatcher.Events.Count.ShouldBe(2);
        harness.Dispatcher.Events[0].HandlerId.ShouldBe(1UL);
        harness.Dispatcher.Events[0].Args.ShouldBeOfType<MouseEventArgs>();
        harness.Dispatcher.Events[1].HandlerId.ShouldBe(3UL);
        var change = harness.Dispatcher.Events[1].Args.ShouldBeOfType<ChangeEventArgs>();
        change.Value.ShouldBe("seed");
    }

    [Fact]
    public async Task HandleKeyAsync_InputCharacter_RaisesOnInput()
    {
        await using var harness = await KeyboardHarness.CreateAsync(
            new FocusElementSpec(
                key: "input",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 5,
                    ["onchange"] = 6,
                }));

        var character = new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false);
        await harness.Manager.HandleKeyAsync(character, CancellationToken.None);

        harness.Dispatcher.Events.ShouldHaveSingleItem();
        var @event = harness.Dispatcher.Events.Single();
        @event.HandlerId.ShouldBe(5UL);
        var changeArgs = @event.Args.ShouldBeOfType<ChangeEventArgs>();
        changeArgs.Value.ShouldBe("x");
    }

    [Fact]
    public async Task HandleKeyAsync_Tab_MovesFocusToNext()
    {
        await using var harness = await KeyboardHarness.CreateAsync(
            new FocusElementSpec("first"),
            new FocusElementSpec("second"));

        harness.FocusManager.CurrentFocusKey.ShouldBe("first");

        var tab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: false, alt: false, control: false);
        await harness.Manager.HandleKeyAsync(tab, CancellationToken.None);

        harness.FocusManager.CurrentFocusKey.ShouldBe("second");
        harness.Dispatcher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleKeyAsync_PrintableKey_DispatchesKeyboardLifecycleEvents()
    {
        await using var harness = await KeyboardHarness.CreateAsync(
            new FocusElementSpec(
                key: "input",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["onkeydown"] = 10,
                    ["onkeypress"] = 11,
                    ["onkeyup"] = 12,
                    ["oninput"] = 13,
                }));

        var character = new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false);
        await harness.Manager.HandleKeyAsync(character, CancellationToken.None);

        harness.Dispatcher.Events.Count.ShouldBe(4);
        harness.Dispatcher.Events[0].HandlerId.ShouldBe(10UL);
        var keydownArgs = harness.Dispatcher.Events[0].Args.ShouldBeOfType<KeyboardEventArgs>();
        keydownArgs.Type.ShouldBe("keydown");
        keydownArgs.Key.ShouldBe("a");
        keydownArgs.Code.ShouldBe("KeyA");

        harness.Dispatcher.Events[1].HandlerId.ShouldBe(11UL);
        var keypressArgs = harness.Dispatcher.Events[1].Args.ShouldBeOfType<KeyboardEventArgs>();
        keypressArgs.Type.ShouldBe("keypress");
        keypressArgs.Key.ShouldBe("a");
        keypressArgs.Code.ShouldBe("KeyA");

        harness.Dispatcher.Events[2].HandlerId.ShouldBe(13UL);
        var changeArgs = harness.Dispatcher.Events[2].Args.ShouldBeOfType<ChangeEventArgs>();
        changeArgs.Value.ShouldBe("a");

        harness.Dispatcher.Events[3].HandlerId.ShouldBe(12UL);
        var keyupArgs = harness.Dispatcher.Events[3].Args.ShouldBeOfType<KeyboardEventArgs>();
        keyupArgs.Type.ShouldBe("keyup");
        keyupArgs.Key.ShouldBe("a");
        keyupArgs.Code.ShouldBe("KeyA");
    }

    [Fact]
    public async Task HandleKeyAsync_Tab_DispatchesKeyEventsWithFallback()
    {
        await using var harness = await KeyboardHarness.CreateAsync(
            new FocusElementSpec(
                key: "first",
                events: new Dictionary<string, ulong>
                {
                    ["onkeydown"] = 21,
                    ["onkeyup"] = 22,
                }),
            new FocusElementSpec("second"));

        var tab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: false, alt: false, control: false);
        await harness.Manager.HandleKeyAsync(tab, CancellationToken.None);

        harness.FocusManager.CurrentFocusKey.ShouldBe("second");

        harness.Dispatcher.Events.Count.ShouldBe(2);
        harness.Dispatcher.Events[0].HandlerId.ShouldBe(21UL);
        var keydownArgs = harness.Dispatcher.Events[0].Args.ShouldBeOfType<KeyboardEventArgs>();
        keydownArgs.Type.ShouldBe("keydown");
        keydownArgs.Key.ShouldBe("Tab");

        harness.Dispatcher.Events[1].HandlerId.ShouldBe(22UL);
        var keyupArgs = harness.Dispatcher.Events[1].Args.ShouldBeOfType<KeyboardEventArgs>();
        keyupArgs.Type.ShouldBe("keyup");
        keyupArgs.Key.ShouldBe("Tab");
    }

    [Fact]
    public async Task FocusChange_ClearsBufferedInput()
    {
        await using var harness = await KeyboardHarness.CreateAsync(
            new FocusElementSpec(
                key: "first",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 101,
                }),
            new FocusElementSpec(
                key: "second",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 201,
                }));

        var firstChar = new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false);
        await harness.Manager.HandleKeyAsync(firstChar, CancellationToken.None);

        harness.Dispatcher.Reset();

        await harness.FocusManager.FocusNextAsync(harness.FocusToken);
        await harness.FocusManager.FocusPreviousAsync(harness.FocusToken);

        var nextChar = new ConsoleKeyInfo('b', ConsoleKey.B, shift: false, alt: false, control: false);
        await harness.Manager.HandleKeyAsync(nextChar, CancellationToken.None);

        harness.Dispatcher.Events.ShouldHaveSingleItem();
        var @event = harness.Dispatcher.Events.Single();
        @event.HandlerId.ShouldBe(101UL);
        var change = @event.Args.ShouldBeOfType<ChangeEventArgs>();
        change.Value.ShouldBe("b");
    }

    [Fact]
    public async Task HandleKeyAsync_MultipleSequentialCharacters_EachTriggersOninput()
    {
        // This test verifies HandleKeyAsync behavior when processing keys one at a time.
        // Each call to HandleKeyAsync triggers its own oninput event.
        // This represents what happens during normal typing OR when paste is processed
        // sequentially (which was the bug - causing hundreds of render cycles).
        await using var harness = await KeyboardHarness.CreateAsync(
            new FocusElementSpec(
                key: "input",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 100,
                }));

        // Simulate typing "hello" one character at a time
        var chars = new[] { 'h', 'e', 'l', 'l', 'o' };
        foreach (var ch in chars)
        {
            var key = new ConsoleKeyInfo(ch, (ConsoleKey)char.ToUpper(ch), shift: false, alt: false, control: false);
            await harness.Manager.HandleKeyAsync(key, CancellationToken.None);
        }

        // Each character should have triggered an oninput event
        harness.Dispatcher.Events.Count.ShouldBe(5);

        // Verify each event
        for (int i = 0; i < 5; i++)
        {
            var evt = harness.Dispatcher.Events[i];
            evt.HandlerId.ShouldBe(100UL);
            var args = evt.Args.ShouldBeOfType<ChangeEventArgs>();
            // Each event should contain the accumulated text up to that point
            args.Value.ShouldBe(new string(chars.Take(i + 1).ToArray()));
        }
    }

    [Fact]
    public async Task HandleKeyAsync_LargeSequentialInput_AccumulatesCorrectly()
    {
        // This test verifies that HandleKeyAsync correctly accumulates text when called
        // repeatedly. It demonstrates the performance issue: processing 100 characters
        // one-by-one results in 100 oninput events = 100 render cycles.
        // The batching fix (in RunAsync/HandleBatchedTextInputAsync) detects when
        // Console.KeyAvailable is true and batches multiple keys into a single event.
        await using var harness = await KeyboardHarness.CreateAsync(
            new FocusElementSpec(
                key: "input",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 200,
                }));

        // Process 100 characters sequentially (simulates worst-case paste scenario)
        var largeText = new string('a', 100);
        foreach (var ch in largeText)
        {
            var key = new ConsoleKeyInfo(ch, ConsoleKey.A, shift: false, alt: false, control: false);
            await harness.Manager.HandleKeyAsync(key, CancellationToken.None);
        }

        // Each call to HandleKeyAsync triggers an oninput event
        // This is the bottleneck the batching fix addresses
        harness.Dispatcher.Events.Count.ShouldBe(100);

        // Verify the final accumulated value is correct
        var finalEvent = harness.Dispatcher.Events.Last();
        finalEvent.HandlerId.ShouldBe(200UL);
        var finalArgs = finalEvent.Args.ShouldBeOfType<ChangeEventArgs>();
        finalArgs.Value.ShouldBe(largeText);
    }

    [Fact]
    public async Task HandleKeyAsync_BackspaceInSequence_UpdatesBufferCorrectly()
    {
        // Test that backspace works correctly when processing sequential input
        await using var harness = await KeyboardHarness.CreateAsync(
            new FocusElementSpec(
                key: "input",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 300,
                }));

        // Type "hello", then backspace twice, then type "p!"
        var keys = new[]
        {
            ('h', ConsoleKey.H, false),
            ('e', ConsoleKey.E, false),
            ('l', ConsoleKey.L, false),
            ('l', ConsoleKey.L, false),
            ('o', ConsoleKey.O, false),
            ('\b', ConsoleKey.Backspace, false),
            ('\b', ConsoleKey.Backspace, false),
            ('p', ConsoleKey.P, false),
            ('!', ConsoleKey.D1, true), // Shift+1 produces '!'
        };

        foreach (var (ch, consoleKey, shift) in keys)
        {
            var key = new ConsoleKeyInfo(ch, consoleKey, shift: shift, alt: false, control: false);
            await harness.Manager.HandleKeyAsync(key, CancellationToken.None);
        }

        // Should have 9 events (5 chars + 2 backspaces + 2 chars)
        harness.Dispatcher.Events.Count.ShouldBe(9);

        // Verify the final value
        var finalEvent = harness.Dispatcher.Events.Last();
        var finalArgs = finalEvent.Args.ShouldBeOfType<ChangeEventArgs>();
        finalArgs.Value.ShouldBe("help!");
    }

    [Fact]
    public async Task HandleBatchedTextInput_MultipleCharacters_TriggersSingleOninputEvent()
    {
        // This test verifies that when multiple keys are available (simulating paste),
        // HandleBatchedTextInputAsync batches them into a single oninput event,
        // reducing render cycles from N to 1.
        var mockConsole = Substitute.For<IConsoleInput>();
        var keys = new Queue<ConsoleKeyInfo>();
        foreach (var ch in "hello")
        {
            var key = char.ToUpper(ch);
            var consoleKey = key >= 'A' && key <= 'Z' ? (ConsoleKey)key : ConsoleKey.A;
            keys.Enqueue(new ConsoleKeyInfo(ch, consoleKey, false, false, false));
        }
        mockConsole.KeyAvailable.Returns(_ => keys.Count > 0);
        mockConsole.ReadKey(Arg.Any<bool>()).Returns(_ => keys.Dequeue());

        await using var harness = await KeyboardHarness.CreateAsync(
            mockConsole,
            new FocusElementSpec(
                key: "input",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 400,
                }));

        // Read the first key (which triggers batching since more keys are available)
        var firstKey = mockConsole.ReadKey(intercept: true);

        // Manually invoke HandleBatchedTextInputAsync to test batching logic
        // In real usage, RunAsync detects KeyAvailable and calls this automatically
        await harness.Manager.HandleBatchedTextInputAsync(firstKey, CancellationToken.None);

        // Should have exactly 1 oninput event (batched all 5 characters)
        harness.Dispatcher.Events.ShouldHaveSingleItem();

        var evt = harness.Dispatcher.Events.Single();
        evt.HandlerId.ShouldBe(400UL);
        var args = evt.Args.ShouldBeOfType<ChangeEventArgs>();
        args.Value.ShouldBe("hello");
    }

    [Fact]
    public async Task HandleBatchedTextInput_LargePaste_TriggersSingleEvent()
    {
        // Test batching with a large paste operation (100 characters)
        var mockConsole = Substitute.For<IConsoleInput>();
        var largeText = new string('a', 100);
        var keys = new Queue<ConsoleKeyInfo>();
        foreach (var ch in largeText)
        {
            keys.Enqueue(new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false));
        }
        mockConsole.KeyAvailable.Returns(_ => keys.Count > 0);
        mockConsole.ReadKey(Arg.Any<bool>()).Returns(_ => keys.Dequeue());

        await using var harness = await KeyboardHarness.CreateAsync(
            mockConsole,
            new FocusElementSpec(
                key: "input",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 500,
                }));

        var firstKey = mockConsole.ReadKey(intercept: true);
        await harness.Manager.HandleBatchedTextInputAsync(firstKey, CancellationToken.None);

        // Should have exactly 1 oninput event (batched all 100 characters)
        harness.Dispatcher.Events.ShouldHaveSingleItem();

        var evt = harness.Dispatcher.Events.Single();
        evt.HandlerId.ShouldBe(500UL);
        var args = evt.Args.ShouldBeOfType<ChangeEventArgs>();
        args.Value.ShouldBe(largeText);
    }

    [Fact]
    public async Task HandleBatchedTextInput_PasteWithEnter_StopsBatchingAndHandlesEnter()
    {
        // Test that batching stops when a special key (Enter) is encountered
        var mockConsole = Substitute.For<IConsoleInput>();
        var keys = new Queue<ConsoleKeyInfo>();
        foreach (var ch in "hello")
        {
            var key = char.ToUpper(ch);
            var consoleKey = key >= 'A' && key <= 'Z' ? (ConsoleKey)key : ConsoleKey.A;
            keys.Enqueue(new ConsoleKeyInfo(ch, consoleKey, false, false, false));
        }
        keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        mockConsole.KeyAvailable.Returns(_ => keys.Count > 0);
        mockConsole.ReadKey(Arg.Any<bool>()).Returns(_ => keys.Dequeue());

        await using var harness = await KeyboardHarness.CreateAsync(
            mockConsole,
            new FocusElementSpec(
                key: "input",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 600,
                    ["onclick"] = 601,
                    ["onchange"] = 602,
                }));

        var firstKey = mockConsole.ReadKey(intercept: true);
        await harness.Manager.HandleBatchedTextInputAsync(firstKey, CancellationToken.None);

        // Should have:
        // 1. One oninput event for "hello"
        // 2. One onclick event for Enter
        // 3. One onchange event for Enter
        harness.Dispatcher.Events.Count.ShouldBe(3);

        harness.Dispatcher.Events[0].HandlerId.ShouldBe(600UL);
        var inputArgs = harness.Dispatcher.Events[0].Args.ShouldBeOfType<ChangeEventArgs>();
        inputArgs.Value.ShouldBe("hello");

        harness.Dispatcher.Events[1].HandlerId.ShouldBe(601UL);
        harness.Dispatcher.Events[1].Args.ShouldBeOfType<MouseEventArgs>();

        harness.Dispatcher.Events[2].HandlerId.ShouldBe(602UL);
        var changeArgs = harness.Dispatcher.Events[2].Args.ShouldBeOfType<ChangeEventArgs>();
        changeArgs.Value.ShouldBe("hello");
    }

    [Fact]
    public async Task HandleBatchedTextInput_WithBackspaces_AccumulatesCorrectly()
    {
        // Test batching with backspaces mixed in
        var mockConsole = Substitute.For<IConsoleInput>();
        var keys = new Queue<ConsoleKeyInfo>();
        foreach (var ch in "hello")
        {
            var key = char.ToUpper(ch);
            var consoleKey = key >= 'A' && key <= 'Z' ? (ConsoleKey)key : ConsoleKey.A;
            keys.Enqueue(new ConsoleKeyInfo(ch, consoleKey, false, false, false));
        }
        keys.Enqueue(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        keys.Enqueue(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        foreach (var ch in "p!")
        {
            var key = char.ToUpper(ch);
            var consoleKey = key >= 'A' && key <= 'Z' ? (ConsoleKey)key : ConsoleKey.A;
            keys.Enqueue(new ConsoleKeyInfo(ch, consoleKey, false, false, false));
        }
        mockConsole.KeyAvailable.Returns(_ => keys.Count > 0);
        mockConsole.ReadKey(Arg.Any<bool>()).Returns(_ => keys.Dequeue());

        await using var harness = await KeyboardHarness.CreateAsync(
            mockConsole,
            new FocusElementSpec(
                key: "input",
                value: string.Empty,
                events: new Dictionary<string, ulong>
                {
                    ["oninput"] = 700,
                }));

        var firstKey = mockConsole.ReadKey(intercept: true);
        await harness.Manager.HandleBatchedTextInputAsync(firstKey, CancellationToken.None);

        // Should have exactly 1 oninput event with final accumulated value
        harness.Dispatcher.Events.ShouldHaveSingleItem();

        var evt = harness.Dispatcher.Events.Single();
        evt.HandlerId.ShouldBe(700UL);
        var args = evt.Args.ShouldBeOfType<ChangeEventArgs>();
        args.Value.ShouldBe("help!");
    }

    private sealed class KeyboardHarness : IAsyncDisposable
    {
        private readonly ConsoleRenderer _renderer;
        private readonly ConsoleLiveDisplayContext _context;
        private readonly FocusManager.FocusSession _session;

        private KeyboardHarness(
            FocusManager focusManager,
            KeyboardEventManager manager,
            TestKeyboardEventDispatcher dispatcher,
            ConsoleRenderer renderer,
            ConsoleLiveDisplayContext context,
            FocusManager.FocusSession session)
        {
            FocusManager = focusManager;
            Manager = manager;
            Dispatcher = dispatcher;
            _renderer = renderer;
            _context = context;
            _session = session;
        }

        public FocusManager FocusManager { get; }

        public KeyboardEventManager Manager { get; }

        public TestKeyboardEventDispatcher Dispatcher { get; }

        public CancellationToken FocusToken => _session.Token;

        public static async Task<KeyboardHarness> CreateAsync(params FocusElementSpec[] elements)
        {
            return await CreateAsync(console: null, elements).ConfigureAwait(false);
        }

        public static async Task<KeyboardHarness> CreateAsync(IConsoleInput? console, params FocusElementSpec[] elements)
        {
            if (console is null)
            {
                var defaultMock = Substitute.For<IConsoleInput>();
                defaultMock.KeyAvailable.Returns(false);
                console = defaultMock;
            }

            if (elements is null || elements.Length == 0)
            {
                throw new ArgumentException("At least one focus element is required.", nameof(elements));
            }

            var focusManager = new FocusManager();
            var dispatcher = new TestKeyboardEventDispatcher();
            var manager = new KeyboardEventManager(focusManager, dispatcher, console, NullLogger<KeyboardEventManager>.Instance);

            var services = new ServiceCollection().BuildServiceProvider();
            var renderer = TestHelpers.CreateTestRenderer(services);
            var canvas = new NoopCanvas();
            var view = BuildView(elements);
            var context = new ConsoleLiveDisplayContext(canvas, renderer, view);

            var session = focusManager.BeginSession(context, view, CancellationToken.None);
            if (view.VdomRoot is not null)
            {
                var snapshot = new ConsoleRenderer.RenderSnapshot(view.VdomRoot, view.Renderable, view.AnimatedRenderables);
                ((IObserver<ConsoleRenderer.RenderSnapshot>)focusManager).OnNext(snapshot);
            }
            await session.InitializationTask.ConfigureAwait(false);

            return new KeyboardHarness(focusManager, manager, dispatcher, renderer, context, session);
        }

        public ValueTask DisposeAsync()
        {
            _session.Dispose();
            _context.Dispose();
            GC.KeepAlive(_renderer);
            return ValueTask.CompletedTask;
        }

        private static ConsoleViewResult BuildView(IEnumerable<FocusElementSpec> elements)
        {
            var root = VNode.CreateElement("div");
            foreach (var element in elements)
            {
                var node = VNode.CreateElement(element.TagName);
                node.SetAttribute("data-focusable", "true");
                node.SetKey(element.Key);
                if (!string.IsNullOrEmpty(element.Value))
                {
                    node.SetAttribute("value", element.Value);
                }

                foreach (var evt in element.Events)
                {
                    node.SetEvent(evt.Key, evt.Value);
                }

                root.AddChild(node);
            }

            return ConsoleViewResult.Create(
                html: "<div></div>",
                vdomRoot: root,
                renderable: new FakeRenderable(),
                animatedRenderables: Array.Empty<IAnimatedConsoleRenderable>());
        }
    }

    private sealed class FocusElementSpec
    {
        public FocusElementSpec(string key, string? value = null, IReadOnlyDictionary<string, ulong>? events = null, string tagName = "div")
        {
            Key = key;
            Value = value;
            Events = events ?? new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            TagName = tagName;
        }

        public string Key { get; }

        public string? Value { get; }

        public IReadOnlyDictionary<string, ulong> Events { get; }

        public string TagName { get; }
    }

    private sealed class TestKeyboardEventDispatcher : IKeyboardEventDispatcher
    {
        private readonly List<DispatchedEvent> _events = new();

        public IReadOnlyList<DispatchedEvent> Events => _events;

        public void Reset()
            => _events.Clear();

        public Task DispatchAsync(ulong handlerId, EventArgs eventArgs, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _events.Add(new DispatchedEvent(handlerId, eventArgs));
            return Task.CompletedTask;
        }
    }

    private readonly record struct DispatchedEvent(ulong HandlerId, EventArgs Args);

    private sealed class NoopCanvas : ConsoleLiveDisplayContext.ILiveDisplayCanvas
    {
        public void UpdateTarget(IRenderable? renderable)
        {
        }

        public bool TryReplaceNode(IReadOnlyList<int> path, IRenderable renderable) => false;

        public bool TryUpdateText(IReadOnlyList<int> path, string? text) => false;

        public bool TryUpdateAttributes(IReadOnlyList<int> path, IReadOnlyDictionary<string, object?> attributes) => false;

        public void Refresh()
        {
            Refreshed?.Invoke();
        }

        public event Action? Refreshed;
    }

    private sealed class FakeRenderable : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth)
            => new(0, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
            => Array.Empty<Segment>();
    }
}

