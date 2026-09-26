// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;

namespace RazorConsole.Tests.Input;

public sealed class TerminalInputDecoderTests
{
    [Fact]
    public void MousePackets_AtEverySplitBoundary_NeverBecomeText()
    {
        const string packet = "\u001b[<65;120;35M";
        for (var split = 1; split < packet.Length; split++)
        {
            var decoder = new TerminalInputDecoder();
            decoder.Feed(packet.AsSpan(0, split), 0);
            decoder.Feed(packet.AsSpan(split), 20);
            decoder.TryReadMouse(out var mouse).ShouldBeTrue();
            mouse.ShouldBe(new(TerminalMouseKind.Wheel, 119, 34, DeltaY: 3));
            decoder.KeyAvailable.ShouldBeFalse();
        }
    }

    [Fact]
    public void BurstsAndPartialPackets_PreserveInputOrderAndKeys()
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed("hi\u001b[<64;1;2M\u001b[<65;1;2M你\u001b[B\u001b[1;2D", 0);
        decoder.ReadKey().KeyChar.ShouldBe('h');
        decoder.ReadKey().KeyChar.ShouldBe('i');
        decoder.TryReadMouse(out var up).ShouldBeTrue();
        up.DeltaY.ShouldBe(-3);
        decoder.TryReadMouse(out var down).ShouldBeTrue();
        down.DeltaY.ShouldBe(3);
        decoder.ReadKey().KeyChar.ShouldBe('你');
        decoder.ReadKey().Key.ShouldBe(ConsoleKey.DownArrow);
        var shiftLeft = decoder.ReadKey();
        shiftLeft.Key.ShouldBe(ConsoleKey.LeftArrow);
        shiftLeft.Modifiers.ShouldBe(ConsoleModifiers.Shift);
        decoder.Feed("\u001b[<65;1;", 0);
        decoder.FlushEscape(1000);
        decoder.KeyAvailable.ShouldBeFalse();
        decoder.Feed("2M!", 1001);
        decoder.TryReadMouse(out _).ShouldBeTrue();
        decoder.ReadKey().KeyChar.ShouldBe('!');
    }

    [Fact]
    public void EscapeAndMalformedSequences_DoNotLeakControlBytes()
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed("\u001b", 0);
        decoder.FlushEscape(49);
        decoder.KeyAvailable.ShouldBeFalse();
        decoder.FlushEscape(50);
        decoder.ReadKey().Key.ShouldBe(ConsoleKey.Escape);
        decoder.Feed("\u001b[<999;2;3M\u001b[<65;\u001b[Aa", 60);
        decoder.ReadKey().Key.ShouldBe(ConsoleKey.UpArrow);
        decoder.ReadKey().KeyChar.ShouldBe('a');
        decoder.KeyAvailable.ShouldBeFalse();
    }
}
