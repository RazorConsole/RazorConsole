// Copyright (c) RazorConsole. All rights reserved.

#pragma warning disable BL0006 // RenderTree types are "internal-ish"; acceptable for tests exercising ConsoleRenderer.
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Input;

/// <summary>
/// Regression tests for the scroll (wheel) event leakage bug: a <c>@onwheel:stopPropagation="true"</c>
/// directive must stop a wheel event dispatched on an inner element from also reaching an ancestor's
/// own "onwheel" handler.
/// </summary>
public sealed class MouseEventManagerScrollPropagationTests
{
    [Fact]
    public async Task Wheel_WithStopPropagationOnInner_OnlyDispatchesInnerHandler()
    {
        await using var harness = await MouseHarness.CreateAsync(stopInnerPropagation: true);

        await harness.DispatchWheelAtHookAsync("inner-hook");

        harness.Component.InnerWheelCount.ShouldBe(1);
        harness.Component.OuterWheelCount.ShouldBe(0);
    }

    [Fact]
    public async Task Wheel_WithoutStopPropagationOnInner_DispatchesBothInnerAndOuterHandlers()
    {
        await using var harness = await MouseHarness.CreateAsync(stopInnerPropagation: false);

        await harness.DispatchWheelAtHookAsync("inner-hook");

        harness.Component.InnerWheelCount.ShouldBe(1);
        harness.Component.OuterWheelCount.ShouldBe(1);
    }

    private sealed class MouseHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        private MouseHarness(ServiceProvider provider, MouseEventManager manager, IVNodeLayoutAccessor layouts, NestedWheelComponent component)
        {
            _provider = provider;
            Manager = manager;
            Layouts = layouts;
            Component = component;
        }

        public MouseEventManager Manager { get; }

        public IVNodeLayoutAccessor Layouts { get; }

        public NestedWheelComponent Component { get; }

        public static async Task<MouseHarness> CreateAsync(bool stopInnerPropagation)
        {
            var services = new ServiceCollection();
            services.AddRazorConsoleServices();
            services.Configure<ConsoleAppOptions>(options => options.RenderingPipeline = RazorConsoleRenderingPipeline.WidgetLayout);
            var provider = services.BuildServiceProvider();

            var renderer = provider.GetRequiredService<ConsoleRenderer>();
            var layouts = provider.GetRequiredService<IVNodeLayoutAccessor>();
            var manager = provider.GetRequiredService<MouseEventManager>();

            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(NestedWheelComponent.StopInnerPropagation)] = stopInnerPropagation,
            });

            var snapshot = await renderer.MountComponentAsync<NestedWheelComponent>(parameters, CancellationToken.None).ConfigureAwait(false);
            snapshot.Root.ShouldNotBeNull();

            var component = NestedWheelComponent.Instance ?? throw new InvalidOperationException("Component was not instantiated.");

            return new MouseHarness(provider, manager, layouts, component);
        }

        public async Task DispatchWheelAtHookAsync(string hookKey)
        {
            var layout = Layouts.GetLayoutByHookKeyOrDefault(hookKey);
            layout.ShouldNotBeNull($"Expected layout metadata for hook '{hookKey}'.");
            layout!.Value.Left.ShouldNotBeNull();
            layout.Value.Top.ShouldNotBeNull();

            var wheelEvent = new TerminalMouseEvent(TerminalMouseKind.Wheel, layout.Value.Left!.Value, layout.Value.Top!.Value, Button: 0, DeltaY: 1);
            await Manager.HandleAsync(wheelEvent, CancellationToken.None).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            _provider.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NestedWheelComponent : ComponentBase
    {
        public static NestedWheelComponent? Instance;

        public NestedWheelComponent() => Instance = this;

        [Parameter]
        public bool StopInnerPropagation { get; set; }

        public int OuterWheelCount { get; private set; }

        public int InnerWheelCount { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(this, HandleOuterWheel));
            builder.AddAttribute(2, IVNodeIdAccessor.HookAttributeName, "outer-hook");

            builder.OpenElement(3, "div");
            builder.AddAttribute(4, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(this, HandleInnerWheel));
            if (StopInnerPropagation)
            {
                builder.AddEventStopPropagationAttribute(5, "onwheel", true);
            }

            builder.AddAttribute(6, IVNodeIdAccessor.HookAttributeName, "inner-hook");
            builder.AddContent(7, "Inner wheel target");
            builder.CloseElement();

            builder.CloseElement();
        }

        private void HandleOuterWheel(WheelEventArgs e) => OuterWheelCount++;

        private void HandleInnerWheel(WheelEventArgs e) => InnerWheelCount++;
    }
}
#pragma warning restore BL0006
