// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Components;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Components;

public sealed class TextInputTests
{
    [Fact]
    public async Task TextInput_RendersFocusMetadataAndValue()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(services);

        var snapshot = await renderer.MountComponentAsync<ValueHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<RazorConsole.Core.Vdom.VNode>();
        root.TagName.ShouldBe("div");
        root.Attributes["data-focusable"].ShouldBe("true");
        root.Attributes.ContainsKey("data-text-input").ShouldBeTrue();
        root.Attributes["value"].ShouldBe("Alice");
        root.Attributes["data-has-value"].ShouldBe("true");
    }

    [Fact]
    public async Task TextInput_WithoutValue_ExposesPlaceholderMetadata()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(services);

        var snapshot = await renderer.MountComponentAsync<PlaceholderHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<RazorConsole.Core.Vdom.VNode>();
        root.Attributes["value"].ShouldBe("");
        root.Attributes["data-has-value"].ShouldBe("false");
        root.Attributes["data-placeholder"].ShouldBe("Type here");
    }

    [Fact]
    public async Task TextInput_WithExpand_SetsExpandAttribute()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(services);

        var snapshot = await renderer.MountComponentAsync<ExpandedHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<RazorConsole.Core.Vdom.VNode>();
        root.Attributes["data-expand"].ShouldBe("true");
    }

    [Fact]
    public async Task TextInput_WithMaskInput_RendersMaskedValue()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(services);

        var snapshot = await renderer.MountComponentAsync<MaskedHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldBeOfType<VNode>();
        root.Attributes["data-mask-input"].ShouldBe("true");

        var maskedSpan = FindNode(root, static node =>
            node.TryGetAttributeValue<string>("data-text", out var textFlag) &&
            textFlag == "true" &&
            node.Attributes.ContainsKey("data-content"));

        maskedSpan.ShouldNotBeNull();
        maskedSpan!.Attributes["data-content"].ShouldBe("••••••");
    }

    private sealed class ValueHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TextInput>(0);
            builder.AddAttribute(1, "Value", "Alice");
            builder.AddAttribute(2, "Label", "Name");
            builder.AddAttribute(3, "Placeholder", "Type here");
            builder.CloseComponent();
        }
    }

    private sealed class ExpandedHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TextInput>(0);
            builder.AddAttribute(1, "Label", "Name");
            builder.AddAttribute(2, "Expand", true);
            builder.CloseComponent();
        }
    }

    private sealed class MaskedHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TextInput>(0);
            builder.AddAttribute(1, "Value", "Secret");
            builder.AddAttribute(2, "MaskInput", true);
            builder.CloseComponent();
        }
    }

    private sealed class PlaceholderHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TextInput>(0);
            builder.AddAttribute(1, "Label", "Name");
            builder.AddAttribute(2, "Placeholder", "Type here");
            builder.CloseComponent();
        }
    }

    private static VNode? FindNode(VNode node, Func<VNode, bool> predicate)
    {
        if (predicate(node))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var match = FindNode(child, predicate);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}

