// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;

namespace RazorConsole.Tests.Layout;

public sealed class GeometryTests
{
    [Fact]
    public void LayoutSize_WithNegativeWidth_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LayoutSize(-1, 0));
    }

    [Fact]
    public void LayoutRect_Intersect_ReturnsOverlappingArea()
    {
        var first = new LayoutRect(2, 3, 10, 4);
        var second = new LayoutRect(5, 1, 4, 6);

        var result = first.Intersect(second);

        result.ShouldBe(new LayoutRect(5, 3, 4, 4));
    }

    [Fact]
    public void LayoutRect_Intersect_WhenDisjoint_ReturnsEmpty()
    {
        var first = new LayoutRect(0, 0, 2, 2);
        var second = new LayoutRect(3, 3, 2, 2);

        var result = first.Intersect(second);

        result.ShouldBe(LayoutRect.Empty);
    }

    [Fact]
    public void BoxConstraints_Constrain_ClampsSize()
    {
        var constraints = new BoxConstraints(2, 5, 1, 3);

        var result = constraints.Constrain(new LayoutSize(10, 0));

        result.ShouldBe(new LayoutSize(5, 1));
    }

    [Fact]
    public void BoxConstraints_Deflate_ReducesConstraintsWithoutGoingNegative()
    {
        var constraints = new BoxConstraints(4, 10, 3, 8);

        var result = constraints.Deflate(left: 3, top: 2, right: 3, bottom: 2);

        result.ShouldBe(new BoxConstraints(0, 4, 0, 4));
    }
}
