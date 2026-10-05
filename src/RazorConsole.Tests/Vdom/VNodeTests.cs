// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Vdom;

public sealed class VNodeTests
{
    [Fact]
    public void Equals_ReturnsTrue_ForIdenticalTrees()
    {
        var left = BuildSampleNode();
        var right = BuildSampleNode();

        (left == right).ShouldBeTrue();
        left.Equals(right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenAttributesDiffer()
    {
        var left = BuildSampleNode();
        var right = BuildSampleNode();
        right.SetAttribute("data-id", "other");

        (left != right).ShouldBeTrue();
        left.Equals(right).ShouldBeFalse();
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenChildStructureDiffers()
    {
        var left = BuildSampleNode();
        var right = BuildSampleNode();
        right.Children[0].AddChild(VNode.CreateText("extra"));

        (left != right).ShouldBeTrue();
    }

    [Fact]
    public void Equals_HandlesNullComparisons()
    {
        var node = BuildSampleNode();

        (node == null).ShouldBeFalse();
        (node != null).ShouldBeTrue();
        ((VNode?)null == (VNode?)null).ShouldBeTrue();
    }

    [Fact]
    public void TryGetEvent_ReturnsEventWithOptions_WhenPresent()
    {
        var node = VNode.CreateElement("div");
        node.SetEvent("onwheel", 42, new VNodeEventOptions(PreventDefault: false, StopPropagation: true));

        node.TryGetEvent("onwheel", out var vNodeEvent).ShouldBeTrue();
        vNodeEvent.HandlerId.ShouldBe(42UL);
        vNodeEvent.Options.StopPropagation.ShouldBeTrue();
        vNodeEvent.Options.PreventDefault.ShouldBeFalse();
    }

    [Fact]
    public void TryGetEvent_ReturnsFalse_WhenEventMissing()
    {
        var node = VNode.CreateElement("div");

        node.TryGetEvent("onwheel", out _).ShouldBeFalse();
    }

    private static VNode BuildSampleNode()
    {
        var root = VNode.CreateElement("div", "root");
        root.SetAttribute("class", "container");
        root.SetAttribute("data-id", "root");
        root.SetEvent("onclick", 1, new VNodeEventOptions(true, false));

        var child = VNode.CreateElement("span", "child");
        child.SetAttribute("role", "text");
        child.AddChild(VNode.CreateText("Hello"));

        root.AddChild(child);
        return root;
    }
}

