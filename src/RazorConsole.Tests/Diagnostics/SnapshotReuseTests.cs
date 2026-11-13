// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using RazorConsole.Core.Layout;

namespace RazorConsole.Tests.Diagnostics;

public sealed class SnapshotReuseTests
{
    [Fact]
    public async Task RefreshSnapshot_WithNothingChanged_ReturnsTheSameSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var renderer = DiagnosticsHooksTests.CreateRenderer();
        var mounted = await renderer.MountComponentAsync<TextComponent>(ParameterView.Empty, ct);

        var first = renderer.RefreshSnapshot();
        var second = renderer.RefreshSnapshot();

        first.Sequence.ShouldBe(mounted.Sequence);
        second.Sequence.ShouldBe(first.Sequence);
        second.Renderable.ShouldBeSameAs(first.Renderable);
    }

    [Fact]
    public async Task RefreshSnapshot_AfterTheScrollStateChanged_BuildsANewSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var renderer = DiagnosticsHooksTests.CreateRenderer();
        var mounted = await renderer.MountComponentAsync<TextComponent>(ParameterView.Empty, ct);
        var registry = renderer.ScrollRegistry.ShouldNotBeNull();

        registry.SetTop("a", 2, ScrollChangeSource.Parameter);
        var afterOffset = renderer.RefreshSnapshot();
        registry.SetSticky("a", true);
        var afterSticky = renderer.RefreshSnapshot();
        registry.Remove("a");
        var afterRemove = renderer.RefreshSnapshot();

        afterOffset.Sequence.ShouldBeGreaterThan(mounted.Sequence);
        afterSticky.Sequence.ShouldBeGreaterThan(afterOffset.Sequence);
        afterRemove.Sequence.ShouldBeGreaterThan(afterSticky.Sequence);
    }

    [Fact]
    public async Task RefreshSnapshot_AfterAnUnchangedOffsetIsSetAgain_KeepsTheSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var renderer = DiagnosticsHooksTests.CreateRenderer();
        await renderer.MountComponentAsync<TextComponent>(ParameterView.Empty, ct);
        var registry = renderer.ScrollRegistry.ShouldNotBeNull();
        registry.SetTop("a", 2, ScrollChangeSource.Parameter);
        var first = renderer.RefreshSnapshot();

        registry.SetTop("a", 2, ScrollChangeSource.Parameter);
        registry.ReportLayout("a", 10, 4);
        var again = renderer.RefreshSnapshot();

        again.Sequence.ShouldBe(first.Sequence);
        again.Renderable.ShouldBeSameAs(first.Renderable);
    }

    private sealed class TextComponent : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddContent(1, "hello");
            builder.CloseElement();
        }
    }
}
