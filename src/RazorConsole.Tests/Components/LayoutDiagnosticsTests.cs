// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Components;
using RazorConsole.Core;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Components;

public sealed class LayoutDiagnosticsTests
{
    [Fact]
    public async Task LayoutDiagnostics_RendersDiagnosticHookAndMetadata()
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        using var provider = services.BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(provider);

        var snapshot = await renderer.MountComponentAsync<DiagnosticsHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldNotBeNull();
        var diagnosticsRoot = FindNode(root, node =>
            node.Attributes.TryGetValue(IVNodeIdAccessor.HookAttributeName, out var hook)
            && hook == "diag-hook");
        diagnosticsRoot.ShouldNotBeNull();

        var text = string.Join('\n', EnumerateTextContent(root));
        text.ShouldContain("Target: hook=target-hook");
        text.ShouldContain("Target vnode: <missing>");
        text.ShouldContain("Layout: <not available in current snapshot>");
        text.ShouldContain("Layout ancestry:");
        text.ShouldContain("Focus:");
    }

    [Fact]
    public async Task LayoutDiagnostics_WithoutTargetHook_FallsBackToSelfWhenNoFocusedElementExists()
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        using var provider = services.BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(provider);

        var snapshot = await renderer.MountComponentAsync<SelfDiagnosticsHost>(ParameterView.Empty, CancellationToken.None);

        var root = snapshot.Root.ShouldNotBeNull();
        var text = string.Join('\n', EnumerateTextContent(root));
        text.ShouldContain("Target: self hook=self-diag-hook");
        text.ShouldContain("Focus: <none>");
    }

    private sealed class DiagnosticsHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<LayoutDiagnostics>(0);
            builder.AddAttribute(1, nameof(LayoutDiagnostics.DiagnosticHookKey), "diag-hook");
            builder.AddAttribute(2, nameof(LayoutDiagnostics.TargetHookKey), "target-hook");
            builder.CloseComponent();
        }
    }

    private sealed class SelfDiagnosticsHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<LayoutDiagnostics>(0);
            builder.AddAttribute(1, nameof(LayoutDiagnostics.DiagnosticHookKey), "self-diag-hook");
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

    private static IEnumerable<string> EnumerateTextContent(VNode node)
    {
        if (node.Attributes.TryGetValue("data-content", out var content) && content is not null)
        {
            yield return content;
        }

        foreach (var child in node.Children)
        {
            foreach (var childContent in EnumerateTextContent(child))
            {
                yield return childContent;
            }
        }
    }
}
