// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Testing.Vt;

namespace RazorConsole.Tests.Testing;

public sealed class VtInputEncoderTests
{
    private const string Esc = "\u001b";

    [Theory]
    [InlineData(ConsoleKey.UpArrow, '\0', (ConsoleModifiers)0, false, "\u001b[A")]
    [InlineData(ConsoleKey.UpArrow, '\0', (ConsoleModifiers)0, true, "\u001bOA")]
    [InlineData(ConsoleKey.LeftArrow, '\0', ConsoleModifiers.Control, true, "\u001b[1;5D")]
    [InlineData(ConsoleKey.RightArrow, '\0', ConsoleModifiers.Shift | ConsoleModifiers.Alt, false, "\u001b[1;4C")]
    [InlineData(ConsoleKey.Home, '\0', (ConsoleModifiers)0, false, "\u001b[H")]
    [InlineData(ConsoleKey.End, '\0', (ConsoleModifiers)0, true, "\u001bOF")]
    [InlineData(ConsoleKey.Delete, '\0', (ConsoleModifiers)0, false, "\u001b[3~")]
    [InlineData(ConsoleKey.PageDown, '\0', ConsoleModifiers.Control, false, "\u001b[6;5~")]
    [InlineData(ConsoleKey.F1, '\0', (ConsoleModifiers)0, false, "\u001bOP")]
    [InlineData(ConsoleKey.F4, '\0', ConsoleModifiers.Shift, false, "\u001b[1;2S")]
    [InlineData(ConsoleKey.F5, '\0', (ConsoleModifiers)0, false, "\u001b[15~")]
    [InlineData(ConsoleKey.F12, '\0', (ConsoleModifiers)0, false, "\u001b[24~")]
    [InlineData(ConsoleKey.Tab, '\t', ConsoleModifiers.Shift, false, "\u001b[Z")]
    [InlineData(ConsoleKey.Tab, '\t', (ConsoleModifiers)0, false, "\t")]
    [InlineData(ConsoleKey.Enter, '\r', (ConsoleModifiers)0, false, "\r")]
    [InlineData(ConsoleKey.Backspace, '\b', (ConsoleModifiers)0, false, "\u007f")]
    [InlineData(ConsoleKey.Escape, '\u001b', (ConsoleModifiers)0, false, "\u001b")]
    [InlineData(ConsoleKey.C, '\u0003', ConsoleModifiers.Control, false, "\u0003")]
    [InlineData(ConsoleKey.C, '\0', ConsoleModifiers.Control, false, "\u0003")]
    [InlineData(ConsoleKey.Spacebar, '\0', ConsoleModifiers.Control, false, "\0")]
    [InlineData(ConsoleKey.X, 'x', ConsoleModifiers.Alt, false, "\u001bx")]
    [InlineData(ConsoleKey.A, '\0', ConsoleModifiers.Shift, false, "A")]
    [InlineData(ConsoleKey.D7, '\0', (ConsoleModifiers)0, false, "7")]
    [InlineData(ConsoleKey.Oem2, '/', (ConsoleModifiers)0, false, "/")]
    public void EncodeKey_ProducesXtermSequences(ConsoleKey key, char keyChar, ConsoleModifiers modifiers, bool applicationCursorKeys, string expected)
    {
        VtInputEncoder.EncodeKey(Key(key, keyChar, modifiers), applicationCursorKeys).ShouldBe(expected);
    }

    [Fact]
    public void EncodeKey_WithoutAnEncoding_Throws()
    {
        Should.Throw<ArgumentException>(() => VtInputEncoder.EncodeKey(Key(ConsoleKey.Oem1, '\0', 0), false));
    }

    [Theory]
    [InlineData(ConsoleKey.UpArrow, '\0', (ConsoleModifiers)0)]
    [InlineData(ConsoleKey.DownArrow, '\0', ConsoleModifiers.Control)]
    [InlineData(ConsoleKey.LeftArrow, '\0', ConsoleModifiers.Shift)]
    [InlineData(ConsoleKey.RightArrow, '\0', ConsoleModifiers.Alt)]
    [InlineData(ConsoleKey.Home, '\0', (ConsoleModifiers)0)]
    [InlineData(ConsoleKey.End, '\0', ConsoleModifiers.Control)]
    [InlineData(ConsoleKey.Insert, '\0', (ConsoleModifiers)0)]
    [InlineData(ConsoleKey.Delete, '\0', ConsoleModifiers.Shift)]
    [InlineData(ConsoleKey.PageUp, '\0', (ConsoleModifiers)0)]
    [InlineData(ConsoleKey.PageDown, '\0', (ConsoleModifiers)0)]
    [InlineData(ConsoleKey.F2, '\0', (ConsoleModifiers)0)]
    [InlineData(ConsoleKey.F9, '\0', ConsoleModifiers.Control)]
    [InlineData(ConsoleKey.Tab, '\t', ConsoleModifiers.Shift)]
    [InlineData(ConsoleKey.Enter, '\r', (ConsoleModifiers)0)]
    [InlineData(ConsoleKey.Backspace, '\b', (ConsoleModifiers)0)]
    [InlineData(ConsoleKey.A, 'a', (ConsoleModifiers)0)]
    [InlineData(ConsoleKey.Z, 'Z', ConsoleModifiers.Shift)]
    [InlineData(ConsoleKey.K, '\u000b', ConsoleModifiers.Control)]
    [InlineData(ConsoleKey.Spacebar, ' ', (ConsoleModifiers)0)]
    public void EncodeKey_RoundTripsThroughRazorConsoleInputDecoding(ConsoleKey key, char keyChar, ConsoleModifiers modifiers)
    {
        var decoder = new TerminalInputDecoder();

        decoder.Feed(VtInputEncoder.EncodeKey(Key(key, keyChar, modifiers), applicationCursorKeys: false), now: 0);

        decoder.KeyAvailable.ShouldBeTrue();
        var decoded = decoder.ReadKey();
        decoded.Key.ShouldBe(key);
        decoded.Modifiers.ShouldBe(modifiers);
        decoder.KeyAvailable.ShouldBeFalse();
    }

    [Theory]
    [InlineData(TerminalMouseKind.Down, 0, 0, 0, 0, (ConsoleModifiers)0, "\u001b[<0;1;1M")]
    [InlineData(TerminalMouseKind.Up, 9, 4, 0, 0, (ConsoleModifiers)0, "\u001b[<0;10;5m")]
    [InlineData(TerminalMouseKind.Down, 2, 3, 2, 0, ConsoleModifiers.Control, "\u001b[<18;3;4M")]
    [InlineData(TerminalMouseKind.Down, 0, 0, 1, 0, ConsoleModifiers.Shift | ConsoleModifiers.Alt, "\u001b[<13;1;1M")]
    [InlineData(TerminalMouseKind.Move, 5, 6, -1, 0, (ConsoleModifiers)0, "\u001b[<35;6;7M")]
    [InlineData(TerminalMouseKind.Move, 5, 6, 0, 0, (ConsoleModifiers)0, "\u001b[<32;6;7M")]
    [InlineData(TerminalMouseKind.Wheel, 7, 8, 0, -3, (ConsoleModifiers)0, "\u001b[<64;8;9M")]
    [InlineData(TerminalMouseKind.Wheel, 7, 8, 0, 3, ConsoleModifiers.Control, "\u001b[<81;8;9M")]
    public void EncodeMouse_ProducesSgrSequences_ThatRazorConsoleParsesBack(
        TerminalMouseKind kind, int x, int y, int button, int deltaY, ConsoleModifiers modifiers, string expected)
    {
        var input = new TerminalMouseEvent(kind, x, y, button, deltaY, modifiers);

        var encoded = VtInputEncoder.EncodeMouse(input);

        encoded.ShouldBe(expected);
        SgrMouseParser.TryParse(encoded, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(input with { DeltaY = Math.Sign(deltaY) * 3 });
    }

    [Fact]
    public void EncodeMouse_RejectsInvalidInput()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => VtInputEncoder.EncodeMouse(new TerminalMouseEvent(TerminalMouseKind.Down, -1, 0)));
        Should.Throw<ArgumentOutOfRangeException>(() => VtInputEncoder.EncodeMouse(new TerminalMouseEvent(TerminalMouseKind.Down, 0, 0, Button: 3)));
    }

    [Fact]
    public void EncodePaste_NormalizesLineBreaks_AndWrapsWhenBracketedPasteIsOn()
    {
        VtInputEncoder.EncodePaste("a\r\nb\nc", bracketedPaste: false).ShouldBe("a\rb\rc");
        VtInputEncoder.EncodePaste("a\nb", bracketedPaste: true).ShouldBe($"{Esc}[200~a\rb{Esc}[201~");
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char keyChar, ConsoleModifiers modifiers)
        => new(
            keyChar,
            key,
            (modifiers & ConsoleModifiers.Shift) != 0,
            (modifiers & ConsoleModifiers.Alt) != 0,
            (modifiers & ConsoleModifiers.Control) != 0);
}
