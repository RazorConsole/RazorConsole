// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Focus;

public sealed class FocusManagerTests
{
    [Fact]
    public async Task BeginSession_SelectsFirstFocusable()
    {
        var manager = new FocusManager();
        var keys = new[] { "first", "second" };
        var initial = CreateView(keys, focusedKey: null);

        using var context = ConsoleLiveDisplayContext.CreateForTesting(
            new TestCanvas(),
            initial,
            new VdomDiffService());
        using var session = manager.BeginSession(context, initial, CancellationToken.None);
        await session.InitializationTask;

        PushInitialSnapshot(manager, initial);

        manager.HasFocusables.ShouldBeTrue();
        manager.CurrentFocusKey.ShouldBe("first");
    }

    [Fact]
    public async Task FocusPreviousAsync_WrapsAround()
    {
        var manager = new FocusManager();
        var keys = new[] { "first", "second", "third" };
        var initial = CreateView(keys, focusedKey: null);

        using var context = ConsoleLiveDisplayContext.CreateForTesting(
            new TestCanvas(),
            initial,
            new VdomDiffService());
        using var session = manager.BeginSession(context, initial, CancellationToken.None);
        await session.InitializationTask;

        PushInitialSnapshot(manager, initial);

        await manager.FocusNextAsync(session.Token);
        await manager.FocusNextAsync(session.Token);
        manager.CurrentFocusKey.ShouldBe("third");

        var changed = await manager.FocusPreviousAsync(session.Token);

        changed.ShouldBeTrue();
        manager.CurrentFocusKey.ShouldBe("second");
    }

    [Fact]
    public async Task BeginSession_SelectsElementWithInteractiveEventsWhenNoFocusableAttribute()
    {
        var manager = new FocusManager();

        var interactive = VNode.CreateElement("button");
        interactive.SetKey("interactive");
        interactive.SetEvent("onclick", 1UL);
        interactive.AddChild(VNode.CreateText("Click"));

        var root = VNode.CreateElement("div");
        root.AddChild(interactive);

        var view = ConsoleViewResult.Create(
            "interactive",
            root,
            new FakeRenderable("interactive"),
            Array.Empty<IAnimatedConsoleRenderable>());

        using var context = ConsoleLiveDisplayContext.CreateForTesting(
            new TestCanvas(),
            view,
            new VdomDiffService());

        using var session = manager.BeginSession(context, view, CancellationToken.None);
        await session.InitializationTask;

        PushInitialSnapshot(manager, view);

        manager.HasFocusables.ShouldBeTrue();
        manager.CurrentFocusKey.ShouldBe("interactive");
    }

    [Fact]
    public async Task FocusChange_DispatchesFocusEvents()
    {
        var dispatcher = new TestFocusEventDispatcher();
        var manager = new FocusManager(dispatcher);

        var first = VNode.CreateElement("input");
        first.SetKey("first");
        first.SetEvent("onfocus", 1UL);
        first.SetEvent("onfocusin", 2UL);
        first.SetEvent("onfocusout", 3UL);

        var second = VNode.CreateElement("input");
        second.SetKey("second");
        second.SetEvent("onfocus", 4UL);
        second.SetEvent("onfocusin", 5UL);
        second.SetEvent("onfocusout", 6UL);

        var root = VNode.CreateElement("div");
        root.AddChild(first);
        root.AddChild(second);

        var view = ConsoleViewResult.Create(
            "focus",
            root,
            new FakeRenderable("focus"),
            Array.Empty<IAnimatedConsoleRenderable>());

        using var context = ConsoleLiveDisplayContext.CreateForTesting(
            new TestCanvas(),
            view,
            new VdomDiffService());

        using var session = manager.BeginSession(context, view, CancellationToken.None);
        await session.InitializationTask;

        PushInitialSnapshot(manager, view);

        dispatcher.Events.ShouldBe(new[]
        {
            ("focusin", 2UL),
            ("focus", 1UL)
        });

        dispatcher.Events.Clear();

        await manager.FocusNextAsync(session.Token);

        dispatcher.Events.ShouldBe(new[]
        {
            ("focusout", 3UL),
            ("focusin", 5UL),
            ("focus", 4UL)
        });
    }

    [Fact]
    public async Task FocusNextAsync_AdvancesFocus()
    {
        var manager = new FocusManager();
        var keys = new[] { "first", "second", "third" };
        var initial = CreateView(keys, focusedKey: null);

        using var context = ConsoleLiveDisplayContext.CreateForTesting(
            new TestCanvas(),
            initial,
            new VdomDiffService());
        using var session = manager.BeginSession(context, initial, CancellationToken.None);
        await session.InitializationTask;

        PushInitialSnapshot(manager, initial);

        manager.HasFocusables.ShouldBeTrue();
        manager.CurrentFocusKey.ShouldBe("first");

        var changed = await manager.FocusNextAsync(session.Token);

        changed.ShouldBeTrue();
        manager.CurrentFocusKey.ShouldBe("second");
    }

    [Fact]
    public async Task OnNext_AutomaticallyRefocusesWhenCurrentElementRemoved()
    {
        var dispatcher = new TestFocusEventDispatcher();
        var manager = new FocusManager(dispatcher);

        var first = VNode.CreateElement("input");
        first.SetKey("first");
        first.SetEvent("onfocus", 1UL);
        first.SetEvent("onfocusin", 2UL);
        first.SetEvent("onfocusout", 3UL);

        var second = VNode.CreateElement("input");
        second.SetKey("second");
        second.SetEvent("onfocus", 4UL);
        second.SetEvent("onfocusin", 5UL);
        second.SetEvent("onfocusout", 6UL);

        var third = VNode.CreateElement("input");
        third.SetKey("third");
        third.SetEvent("onfocus", 7UL);
        third.SetEvent("onfocusin", 8UL);
        third.SetEvent("onfocusout", 9UL);

        var root = VNode.CreateElement("div");
        root.AddChild(first);
        root.AddChild(second);
        root.AddChild(third);

        var view = ConsoleViewResult.Create(
            "focus",
            root,
            new FakeRenderable("focus"),
            Array.Empty<IAnimatedConsoleRenderable>());

        using var context = ConsoleLiveDisplayContext.CreateForTesting(
            new TestCanvas(),
            view,
            new VdomDiffService());
        using var session = manager.BeginSession(context, view, CancellationToken.None);
        await session.InitializationTask;

        PushInitialSnapshot(manager, view);

        manager.CurrentFocusKey.ShouldBe("first");

        // Move focus to the second element
        await manager.FocusNextAsync(session.Token);
        manager.CurrentFocusKey.ShouldBe("second");

        dispatcher.Events.Clear();

        // Remove the currently focused element (second) from the render list
        var updatedRoot = VNode.CreateElement("div");
        updatedRoot.AddChild(first);
        updatedRoot.AddChild(third);

        var updatedView = ConsoleViewResult.Create(
            "focus",
            updatedRoot,
            new FakeRenderable("focus"),
            Array.Empty<IAnimatedConsoleRenderable>());

        var snapshot = new ConsoleRenderer.RenderSnapshot(
            updatedView.VdomRoot!,
            updatedView.Renderable,
            updatedView.AnimatedRenderables);

        ((IObserver<ConsoleRenderer.RenderSnapshot>)manager).OnNext(snapshot);
        await manager.FocusAsync("third", CancellationToken.None);

        // Wait a bit for the async dispatch to complete
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        // Focus should be on third after explicit focus call
        manager.CurrentFocusKey.ShouldBe("third");

        // Focus events should have been dispatched from both auto-refocus and explicit focus
        dispatcher.Events.ShouldBe(new[]
        {
            ("focusout", 6UL),  // second loses focus
            ("focusin", 2UL),   // first gains focus (auto-refocus)
            ("focus", 1UL),     // first focus event (auto-refocus)
            ("focusout", 3UL),  // first loses focus
            ("focusin", 8UL),   // third gains focus
            ("focus", 7UL)     // third focus event
        });
    }

    [Fact]
    public async Task OnNext_ClearsFocusWhenAllElementsRemoved()
    {
        var manager = new FocusManager();
        var keys = new[] { "first", "second" };
        var initial = CreateView(keys, focusedKey: null);

        using var context = ConsoleLiveDisplayContext.CreateForTesting(
            new TestCanvas(),
            initial,
            new VdomDiffService());
        using var session = manager.BeginSession(context, initial, CancellationToken.None);
        await session.InitializationTask;

        PushInitialSnapshot(manager, initial);

        manager.HasFocusables.ShouldBeTrue();
        manager.CurrentFocusKey.ShouldBe("first");

        // Remove all focusable elements
        var emptyView = CreateView(Array.Empty<string>(), focusedKey: null);
        var snapshot = new ConsoleRenderer.RenderSnapshot(
            emptyView.VdomRoot!,
            emptyView.Renderable,
            emptyView.AnimatedRenderables);

        ((IObserver<ConsoleRenderer.RenderSnapshot>)manager).OnNext(snapshot);

        // Focus should be cleared
        manager.HasFocusables.ShouldBeFalse();
        manager.CurrentFocusKey.ShouldBeNull();
    }

    private static void PushInitialSnapshot(FocusManager manager, ConsoleViewResult view)
    {
        if (view.VdomRoot is null)
        {
            return;
        }

        var snapshot = new ConsoleRenderer.RenderSnapshot(view.VdomRoot, view.Renderable, view.AnimatedRenderables);
        ((IObserver<ConsoleRenderer.RenderSnapshot>)manager).OnNext(snapshot);
    }

    private static ConsoleViewResult CreateView(IReadOnlyList<string> keys, string? focusedKey)
    {
        var children = new List<VNode>(keys.Count);
        foreach (var key in keys)
        {
            var element = VNode.CreateElement("span");
            element.SetAttribute("data-focusable", "true");
            element.SetKey(key);
            element.SetAttribute("data-text", "true");

            if (string.Equals(key, focusedKey, StringComparison.Ordinal))
            {
                element.SetAttribute("data-style", "yellow");
            }

            element.AddChild(VNode.CreateText(key));
            children.Add(element);
        }

        var root = VNode.CreateElement("div");
        root.SetAttribute("data-rows", "true");
        foreach (var child in children)
        {
            root.AddChild(child);
        }

        var html = focusedKey ?? "none";
        var renderable = new FakeRenderable(html);
        return ConsoleViewResult.Create(html, root, renderable, Array.Empty<IAnimatedConsoleRenderable>());
    }

    private sealed class TestCanvas : ConsoleLiveDisplayContext.ILiveDisplayCanvas
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
        private readonly string _content;

        public FakeRenderable(string content)
        {
            _content = content;
        }

        public Measurement Measure(RenderOptions options, int maxWidth)
            => new(0, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
            => new[] { new Segment(_content) };
    }

    private sealed class FakeComponent : IComponent
    {
        public void Attach(RenderHandle renderHandle)
        {
        }

        public Task SetParametersAsync(ParameterView parameters)
            => Task.CompletedTask;
    }

    private sealed class TestFocusEventDispatcher : IFocusEventDispatcher
    {
        public List<(string EventType, ulong HandlerId)> Events { get; } = new();

        public Task DispatchAsync(ulong handlerId, EventArgs eventArgs, CancellationToken cancellationToken)
        {
            var type = eventArgs switch
            {
                FocusEventArgs focus => focus.Type ?? string.Empty,
                _ => eventArgs.GetType().Name,
            };

            Events.Add((type, handlerId));
            return Task.CompletedTask;
        }
    }
}

