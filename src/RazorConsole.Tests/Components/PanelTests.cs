// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Components;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Components;

public sealed class PanelTests
{
    [Fact]
    public async Task Panel_ForwardsTitleParameterToBoxMarkup()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(services);

        var snapshot = await renderer.MountComponentAsync<TitledPanelHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<VNode>();
        root.Attributes["class"].ShouldBe("panel");
        root.Attributes["data-header"].ShouldBe("Actual title");
    }

    [Fact]
    public async Task Panel_WithoutTitleDoesNotEmitDefaultTitleLiteral()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(services);

        var snapshot = await renderer.MountComponentAsync<UntitledPanelHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<VNode>();
        root.Attributes["class"].ShouldBe("panel");
        root.Attributes.ContainsKey("data-header").ShouldBeFalse();
    }

    private sealed class TitledPanelHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Panel>(0);
            builder.AddAttribute(1, nameof(Panel.Title), "Actual title");
            builder.CloseComponent();
        }
    }

    private sealed class UntitledPanelHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Panel>(0);
            builder.CloseComponent();
        }
    }
}