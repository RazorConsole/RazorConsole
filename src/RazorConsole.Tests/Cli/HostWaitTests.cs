// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using RazorConsole.Cli.Host;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The pure parts of <c>wait</c> and <c>assert</c>: which values name the current route, and what a failed assertion carries.
/// </summary>
public sealed class HostWaitTests
{
    [Theory]
    [InlineData("textinput")]
    [InlineData("/textinput")]
    [InlineData("textinput/")]
    [InlineData("app:///textinput")]
    public void ARoute_IsMatchedByItsRelativePathOrItsFullUri(string value)
        => HostWait.RouteMatches("app:///textinput", "textinput", value).ShouldBeTrue();

    [Theory]
    [InlineData("other")]
    [InlineData("/other")]
    [InlineData("app:///other")]
    [InlineData("app:///textinput/more")]
    [InlineData("http://localhost/textinput")]
    public void ADifferentRoute_IsNotMatched(string value)
        => HostWait.RouteMatches("app:///textinput", "textinput", value).ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("app:///")]
    public void TheRoot_IsMatchedByEveryFormOfIt(string value)
        => HostWait.RouteMatches("app:///", string.Empty, value).ShouldBeTrue();

    [Theory]
    [InlineData("search?q=1#top", true)]
    [InlineData("/search?q=1#top", true)]
    [InlineData("app:///search?q=1#top", true)]
    [InlineData("search", false)]
    [InlineData("search?q=2#top", false)]
    public void AQueryAndFragment_AreSignificant(string value, bool expected)
        => HostWait.RouteMatches("app:///search?q=1#top", "search?q=1#top", value).ShouldBe(expected);

    [Fact]
    public void APathThatMerelyContainsAScheme_IsStillAPath()
        => HostWait.RouteMatches("app:///go?next=http://x", "go?next=http://x", "go?next=http://x").ShouldBeTrue();

    [Fact]
    public void AShortFailure_KeepsTheConditionTheObservedValueAndTheWholeScreen()
    {
        var data = HostWait.CreateAssertionData("focus on 'a'", false, "line 1\nline 2", "b", null, "disabled");

        data.Condition.ShouldBe("focus on 'a'");
        data.Observed.ShouldBe("b");
        data.Screen.ShouldBe("line 1\nline 2");
        data.ScreenTruncated.ShouldBeNull();
        data.TreeReason.ShouldBe("disabled");
    }

    [Fact]
    public void ALongScreen_IsCutWithATruncatedMarker()
    {
        var screen = new string('x', HostWait.MaxScreenChars + 500);

        var data = HostWait.CreateAssertionData("the screen to contain \"y\"", false, screen, null, null, null);

        data.Screen.Length.ShouldBe(HostWait.MaxScreenChars);
        data.ScreenTruncated.ShouldBe(true);
    }

    [Fact]
    public void ASmallTree_IsKept_AndABigOneIsLeftToGetTree()
    {
        using var small = JsonDocument.Parse("""{"root":{"tag":"div"},"frame":1}""");
        using var big = JsonDocument.Parse("{\"root\":\"" + new string('n', HostWait.MaxTreeChars) + "\",\"frame\":1}");

        var kept = HostWait.CreateAssertionData("c", false, "s", null, small.RootElement.Clone(), null);
        var dropped = HostWait.CreateAssertionData("c", false, "s", null, big.RootElement.Clone(), null);

        kept.Tree.ShouldNotBeNull();
        kept.TreeReason.ShouldBeNull();
        dropped.Tree.ShouldBeNull();
        dropped.TreeReason.ShouldBe("too_large");
    }
}
