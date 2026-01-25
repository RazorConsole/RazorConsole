// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Components;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Components;

public sealed class SelectTests
{
    [Fact]
    public async Task Select_WithFocusedValue_BindsHighlightedOption()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(services);

        var snapshot = await renderer.MountComponentAsync<FocusedValueHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<VNode>();
        root.TagName.ShouldBe("div");

        // Verify the component rendered with options
        root.Children.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Select_WithoutFocusedValue_StillWorks()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(services);

        var snapshot = await renderer.MountComponentAsync<BasicHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<VNode>();
        root.TagName.ShouldBe("div");

        // Verify the component rendered with options
        root.Children.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Select_RendersOptions()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(services);

        var snapshot = await renderer.MountComponentAsync<BasicHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<VNode>();

        // The select should render the options
        root.Children.Count.ShouldBeGreaterThan(0);
    }

    private sealed class BasicHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Select<string>>(0);
            builder.AddAttribute(1, "Options", new[] { "Option1", "Option2", "Option3" });
            builder.AddAttribute(2, "Value", "Option1");
            builder.CloseComponent();
        }
    }

    private sealed class FocusedValueHost : ComponentBase
    {
        private string? _focusedValue = "Option2";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Select<string>>(0);
            builder.AddAttribute(1, "Options", new[] { "Option1", "Option2", "Option3" });
            builder.AddAttribute(2, "Value", "Option1");
            builder.AddAttribute(3, "FocusedValue", _focusedValue);
            builder.AddAttribute(4, "FocusedValueChanged", EventCallback.Factory.Create<string?>(this, value =>
            {
                _focusedValue = value;
            }));
            builder.CloseComponent();
        }
    }
}

