// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Core.Layout;

namespace RazorConsole.Tests.Input;

public sealed class TerminalMouseTests
{
    [Fact]
    public void CursorReplies_AreConsumedWithoutKeyboardOrMouseLeakage()
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed("\u001b[12;", 0);
        decoder.Feed("40R\u001b[?1;1R\u001b[<65;10;10Mx\u001bOR", 1);
        decoder.TryReadMouse(out var mouse).ShouldBeTrue();
        mouse.Kind.ShouldBe(TerminalMouseKind.Wheel);
        decoder.ReadKey().KeyChar.ShouldBe('x');
        decoder.ReadKey().Key.ShouldBe(ConsoleKey.F3);
        decoder.KeyAvailable.ShouldBeFalse();
    }
    [Theory]
    [InlineData("\u001b[<0;3;22M", TerminalMouseKind.Down, 0, 0)]
    [InlineData("\u001b[<0;3;22m", TerminalMouseKind.Up, 0, 0)]
    [InlineData("\u001b[<32;3;22M", TerminalMouseKind.Move, 0, 0)]
    [InlineData("\u001b[<2;3;22M", TerminalMouseKind.Down, 2, 0)]
    [InlineData("\u001b[<64;3;22M", TerminalMouseKind.Wheel, 0, -3)]
    [InlineData("\u001b[<65;3;22M", TerminalMouseKind.Wheel, 0, 3)]
    public void SgrCoordinates_AreConvertedToZeroBasedCells(string packet, TerminalMouseKind kind, int button, int delta)
    {
        SgrMouseParser.TryParse(packet, out var input).ShouldBeTrue();
        input.ShouldBe(new TerminalMouseEvent(kind, 2, 21, button, delta));
    }

    [Theory]
    [InlineData("\u001b[<0;0;2M")]
    [InlineData("\u001b[<0;1;0M")]
    [InlineData("\u001b[<0;1;2")]
    [InlineData("\u001b[<999999999999;1;2M")]
    [InlineData("\u001b[<0;1;2;3M")]
    public void InvalidPackets_AreRejected(string packet)
        => SgrMouseParser.TryParse(packet, out _).ShouldBeFalse();

    [Fact]
    public void Selection_WordLineAndShiftExtend_UsesSourceText()
    {
        var text = new TextSelectionState();
        text.SetText("hello world\nnext line");
        text.Begin(7, 2, false);
        text.Selection.ShouldBe("world");
        text.Begin(7, 3, false);
        text.Selection.ShouldBe("hello world\n");
        text.End();
        text.Begin(16, 1, true);
        text.Selection.ShouldBe("hello world\nnext");
        text.Insert("replacement");
        text.Text.ShouldBe("replacement line");
    }

    [Fact]
    public void UnicodePointerPositions_MatchPaintedCells()
    {
        var text = new TextSelectionState();
        text.SetText("你e\u0301好");
        var row = text.Wrap(8)[0];
        text.PositionAt(row, 1).ShouldBe(0);
        text.PositionAt(row, 2).ShouldBe(1);
        text.PositionAt(row, 3).ShouldBe(3);
        var canvas = new TerminalCanvas(8, 1);
        canvas.Write(0, 0, text.Text);
        canvas[0, 0].Text.ShouldBe("你");
        canvas[1, 0].Text.ShouldBe("");
        canvas[2, 0].Text.ShouldBe("e\u0301");
        canvas[3, 0].Text.ShouldBe("好");
    }
}
