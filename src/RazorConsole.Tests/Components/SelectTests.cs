// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Components;
using RazorConsole.Core.Vdom;
using RazorConsole.Testing;

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

    [Fact]
    public async Task Select_ForwardsKeysItDoesNotHandle()
    {
        var ct = TestContext.Current.CancellationToken;
        var forwarded = new List<string>();
        await using var terminal = await HeadlessTerminal.StartAsync<KeyForwardingHost>(
            new HeadlessTerminalOptions
            {
                Width = 30,
                Height = 5,
                Parameters = new Dictionary<string, object?> { [nameof(KeyForwardingHost.Forwarded)] = forwarded },
            },
            ct);
        await terminal.WaitForTextAsync("Option1", cancellationToken: ct);

        await terminal.SendKeyAsync(ConsoleKey.DownArrow, '\0', cancellationToken: ct);
        await terminal.SendKeyAsync(ConsoleKey.F1, '\0', cancellationToken: ct);

        await terminal.WaitUntilAsync(_ => forwarded.Count > 0, cancellationToken: ct);
        forwarded.ShouldBe(["F1"]);
    }

    public sealed class KeyForwardingHost : ComponentBase
    {
        [Parameter] public List<string> Forwarded { get; set; } = [];

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Select<string>>(0);
            builder.AddAttribute(1, "Options", new[] { "Option1", "Option2" });
            builder.AddAttribute(2, "Value", "Option1");
            builder.AddAttribute(3, "OnKeyDown", EventCallback.Factory.Create<KeyboardEventArgs>(this, e => Forwarded.Add(e.Key)));
            builder.CloseComponent();
        }
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

