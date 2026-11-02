// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Host;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The raw output buffer of a PTY session: byte offsets, the dropped prefix, and never a broken character.
/// </summary>
public sealed class OutputRingTests
{
    [Fact]
    public void Read_ReturnsUtf8Offsets_AndContinuesFromNext()
    {
        var ring = new OutputRing(64);
        ring.Append("hello");

        ring.Read(null).ShouldBe((0L, 5L, "hello"));
        ring.Read(3).ShouldBe((3L, 5L, "lo"));

        ring.Append(" wörld");
        ring.Read(5).ShouldBe((5L, 12L, " wörld"));
        ring.End.ShouldBe(12L);
    }

    [Fact]
    public void Read_PastTheEnd_ReturnsNothingAtTheEnd()
    {
        var ring = new OutputRing(64);
        ring.Append("abc");

        ring.Read(100).ShouldBe((3L, 3L, string.Empty));
    }

    [Fact]
    public void DroppedPrefix_MovesTheOffsetForward()
    {
        var ring = new OutputRing(8);
        ring.Append("abcdefghij");

        ring.Read(0).ShouldBe((2L, 10L, "cdefghij"));
    }

    [Fact]
    public void DroppedPrefix_InsideACharacter_SkipsItsRest()
    {
        // 1 + 3 × 3 bytes: the oldest kept byte is the middle of the first euro sign.
        var ring = new OutputRing(8);
        ring.Append("a€€€");

        ring.Read(0).ShouldBe((4L, 10L, "€€"));
    }

    [Fact]
    public void MaxBytes_NeverEndsInsideACharacter()
    {
        var ring = new OutputRing(64);
        ring.Append("€€");

        ring.Read(0, 4).ShouldBe((0L, 3L, "€"));
        ring.Read(3, 4).ShouldBe((3L, 6L, "€"));
    }

    [Fact]
    public void SurrogatePair_SplitAcrossAppends_IsEncodedOnce()
    {
        var ring = new OutputRing(64);
        var emoji = "😀";
        ring.Append(emoji.AsSpan(0, 1));
        ring.Append(emoji.AsSpan(1, 1));

        ring.Read(null).ShouldBe((0L, 4L, emoji));
    }
}
