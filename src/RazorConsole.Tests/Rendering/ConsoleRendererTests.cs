// Copyright (c) RazorConsole. All rights reserved.

#pragma warning disable BL0006 // RenderTree types are "internal-ish"; acceptable for console renderer tests.
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;
using RazorConsole.Tests.TestComponents;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Rendering;

public sealed class ConsoleRendererTests
{
    [Fact]
    public async Task ElementCapturesComponentChildren_AsRenderableNodes()
    {
        using var renderer = TestHelpers.CreateTestRenderer();

        var snapshot = await renderer.MountComponentAsync<ContainerComponent>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<Core.Vdom.VNode>();
        root.Kind.ShouldBe(Core.Vdom.VNodeKind.Element);
        root.TagName.ShouldBe("div");
        Enumerate(root).Skip(1).ShouldNotContain(node => node.Kind == Core.Vdom.VNodeKind.Component);

        root.Children.ShouldHaveSingleItem();
        var span = root.Children.Single();
        span.Kind.ShouldBe(Core.Vdom.VNodeKind.Element);
        span.TagName.ShouldBe("span");
        span.Children.ShouldHaveSingleItem();
        var text = span.Children.Single();
        text.Kind.ShouldBe(Core.Vdom.VNodeKind.Text);
        text.Text.ShouldBe("child");
    }

    [Fact]
    public async Task MountComponentAsync_WithValidComponent_ReturnsSnapshot()
    {
        using var renderer = TestHelpers.CreateTestRenderer();

        var snapshot = await renderer.MountComponentAsync<SimpleComponent>(ParameterView.Empty, CancellationToken.None);

        snapshot.Root.ShouldNotBeNull();
        snapshot.Renderable.ShouldNotBeNull();
    }

    [Fact]
    public async Task WidgetLayoutPipeline_WithSimpleComponent_ReturnsCanvasRenderableOutput()
    {
        using var renderer = CreateWidgetLayoutRenderer(out _);

        var snapshot = await renderer.MountComponentAsync<SimpleComponent>(ParameterView.Empty, CancellationToken.None);

        snapshot.Root.ShouldNotBeNull();
        snapshot.Renderable.ShouldNotBeNull();
        RenderToText(snapshot.Renderable!, maxWidth: 20).ShouldBe("Simple");
    }

    [Fact]
    public async Task WidgetLayoutPipeline_CollectsAnimatedFallbackRenderables()
    {
        using var renderer = CreateWidgetLayoutRenderer(out _);

        var snapshot = await renderer.MountComponentAsync<SpinnerComponent>(ParameterView.Empty, CancellationToken.None);

        snapshot.Renderable.ShouldNotBeNull();
        snapshot.AnimatedRenderables.Count.ShouldBe(1);
        RenderToText(snapshot.Renderable!, maxWidth: 20).ShouldContain("Loading");
    }

    [Fact]
    public async Task WidgetLayoutPipeline_PopulatesLayoutAccessorByHook()
    {
        using var renderer = CreateWidgetLayoutRenderer(out var serviceProvider);
        var layoutAccessor = serviceProvider.GetRequiredService<IVNodeLayoutAccessor>();

        await renderer.MountComponentAsync<HookedRowsComponent>(ParameterView.Empty, CancellationToken.None);

        layoutAccessor.TryGetLayoutByHookKey("root-hook", out var rootLayout).ShouldBeTrue();
        rootLayout.Top.ShouldBe(0);
        rootLayout.Left.ShouldBe(0);
        rootLayout.Width.ShouldBe(1);
        rootLayout.Height.ShouldBe(2);

        layoutAccessor.TryGetLayoutByHookKey("first-hook", out var firstLayout).ShouldBeTrue();
        firstLayout.Top.ShouldBe(0);
        firstLayout.Left.ShouldBe(0);
        firstLayout.Width.ShouldBe(1);
        firstLayout.Height.ShouldBe(1);

        layoutAccessor.TryGetLayoutByFocusKey("first-focus", out var focusedLayout).ShouldBeTrue();
        focusedLayout.VNodeId.ShouldBe(firstLayout.VNodeId);
        focusedLayout.Top.ShouldBe(firstLayout.Top);
        focusedLayout.Left.ShouldBe(firstLayout.Left);
        focusedLayout.Width.ShouldBe(firstLayout.Width);
        focusedLayout.Height.ShouldBe(firstLayout.Height);

        var ancestry = layoutAccessor.GetLayoutAncestorsByFocusKey("first-focus");
        ancestry.Count.ShouldBe(2);
        ancestry[0].VNodeId.ShouldBe(rootLayout.VNodeId);
        ancestry[1].VNodeId.ShouldBe(firstLayout.VNodeId);
    }

    [Fact]
    public async Task MountComponentAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        using var renderer = TestHelpers.CreateTestRenderer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await renderer.MountComponentAsync<SimpleComponent>(ParameterView.Empty, cts.Token));
    }

    [Fact]
    public async Task MountComponentAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var renderer = TestHelpers.CreateTestRenderer();
        renderer.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(async () =>
            await renderer.MountComponentAsync<SimpleComponent>(ParameterView.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task MountComponentAsync_WithParameters_PassesParametersToComponent()
    {
        using var renderer = TestHelpers.CreateTestRenderer();

        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            { "Text", "Test" }
        });

        var snapshot = await renderer.MountComponentAsync<ParameterComponent>(parameters, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<Core.Vdom.VNode>();
        root.Kind.ShouldBe(Core.Vdom.VNodeKind.Element);
        root.TagName.ShouldBe("div");
        root.Children.ShouldHaveSingleItem();

        var text = root.Children.Single();
        text.Kind.ShouldBe(Core.Vdom.VNodeKind.Text);
        text.Text.ShouldBe("Test");
    }

    [Fact]
    public async Task UpdatesAttribute_InsideRegion_AppliesCorrectly()
    {
        // Arrange
        using var renderer = TestHelpers.CreateTestRenderer();
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            { "Value", "Start" }
        });

        // Act
        var snapshot = await renderer.MountComponentAsync<RegionTestComponent>(parameters, CancellationToken.None);

        var element = FindDiv(snapshot.Root!);
        element.ShouldNotBeNull().Attributes["data-val"].ShouldBe("Start");

        // Prepare for Update: catch the snapshot where value becomes "End"
        var tcs = new TaskCompletionSource<ConsoleRenderer.RenderSnapshot>();
        using var sub = renderer.Subscribe(new SimpleObserver(s =>
        {
            var el = FindDiv(s.Root);
            if (el != null && el.Attributes.TryGetValue("data-val", out var val) && val == "End")
            {
                tcs.TrySetResult(s);
            }
        }));

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            // Update via captured instance
            await RegionTestComponent.Instance!.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { "Value", "End" }
            }));
        });

        // Assert
        var updatedSnapshot = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var updatedElement = FindDiv(updatedSnapshot.Root!);
        updatedElement.ShouldNotBeNull().Attributes["data-val"].ShouldBe("End");
    }

    [Fact]
    public async Task NestedComponentRenderingTestAsync()
    {
        // Arrange
        using var renderer = TestHelpers.CreateTestRenderer();
        var component = new NestedComponent();
        await renderer.MountComponentAsync(component, ParameterView.Empty, CancellationToken.None);

        // Act
        var tcs = new TaskCompletionSource<ConsoleRenderer.RenderSnapshot>();
        using var sub = renderer.Subscribe(new SimpleObserver(s =>
        {
            var rootNode = s.Root;
            if (rootNode is not null &&
                rootNode.Children.Count() > 0 &&
                rootNode.Children[0].Children[0].Text.Contains("Component is ready."))
            {
                tcs.TrySetResult(s);
            }
        }));

        component.UpdateOffset(10);
        component.Ready();

        // Assert
        var updatedSnapshot = await tcs.Task.WaitAsync(TestContext.Current.CancellationToken);
        var root = updatedSnapshot.Root.ShouldNotBeNull();
        root.Children.Count().ShouldBe(2);
        foreach (var child in root.Children[0].Children)
        {
            // child might be Text
            if (child.Kind != VNodeKind.Element)
            {
                continue;
            }
            child.Children.Count().ShouldBe(2);
            child.Children[0].Text.ShouldBe("10");
            child.Children[1].Text.ShouldBe("10");
        }
    }


    private Core.Vdom.VNode? FindDiv(Core.Vdom.VNode? node)
    {
        if (node == null)
        {
            return null;
        }

        if (node.Kind == Core.Vdom.VNodeKind.Element && node.TagName == "div")
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindDiv(child);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static ConsoleRenderer CreateWidgetLayoutRenderer(out ServiceProvider serviceProvider)
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        services.Configure<ConsoleAppOptions>(options => options.RenderingPipeline = RazorConsoleRenderingPipeline.WidgetLayout);
        serviceProvider = services.BuildServiceProvider();
        return TestHelpers.CreateTestRenderer(serviceProvider);
    }

    private static string RenderToText(IRenderable renderable, int maxWidth)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        var options = new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(maxWidth, 25));
        var segments = renderable.Render(options, maxWidth);
        return string.Concat(segments.Select(segment => segment.IsLineBreak ? "\n" : segment.Text));
    }

    private sealed class RegionTestComponent : ComponentBase
    {
        public static RegionTestComponent? Instance;
        public RegionTestComponent() => Instance = this;

        [Parameter] public string? Value { get; set; }
        [Parameter] public bool HasAttribute { get; set; } = true;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenRegion(0);
            builder.OpenElement(1, "div");
            if (HasAttribute)
            {
                builder.AddAttribute(2, "data-val", Value);
            }
            builder.CloseElement();
            builder.CloseRegion();
        }
    }

    private sealed class SimpleObserver(Action<ConsoleRenderer.RenderSnapshot> onNext) : IObserver<ConsoleRenderer.RenderSnapshot>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(ConsoleRenderer.RenderSnapshot value) => onNext(value);
    }
    private static IEnumerable<Core.Vdom.VNode> Enumerate(Core.Vdom.VNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Enumerate(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class ContainerComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.OpenComponent<ChildComponent>(1);
            builder.CloseComponent();
            builder.CloseElement();
        }
    }

    private sealed class ChildComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddContent(1, "child");
            builder.CloseElement();
        }
    }

    private sealed class SimpleComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddContent(1, "Simple");
            builder.CloseElement();
        }
    }

    private sealed class SpinnerComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "spinner");
            builder.AddAttribute(2, "data-spinner", "true");
            builder.AddAttribute(3, "data-spinner-type", "Dots");
            builder.AddAttribute(4, "data-message", "Loading");
            builder.CloseElement();
        }
    }

    private sealed class HookedRowsComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "rows");
            builder.AddAttribute(2, IVNodeIdAccessor.HookAttributeName, "root-hook");

            builder.OpenElement(3, "span");
            builder.AddAttribute(4, IVNodeIdAccessor.HookAttributeName, "first-hook");
            builder.AddAttribute(5, "data-focus-key", "first-focus");
            builder.AddContent(6, "A");
            builder.CloseElement();

            builder.OpenElement(7, "span");
            builder.AddContent(8, "B");
            builder.CloseElement();

            builder.CloseElement();
        }
    }

    private sealed class ParameterComponent : ComponentBase
    {
        [Parameter]
        public string? Text { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddContent(1, Text ?? "Empty");
            builder.CloseElement();
        }
    }
}
#pragma warning restore BL0006

