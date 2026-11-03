// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;

namespace RazorConsole.Tests.Input;

public sealed class WindowsKeyTranslatorTests
{
    private const ushort Alt = 0x12;
    private const uint LeftAltPressed = 0x02;
    private const uint ShiftPressed = 0x10;

    [Fact]
    public void AltNumpadSequence_YieldsOnlyTheComposedCharacter()
    {
        // conhost (CharToKeyEvents, SynthesizeNumpadEvents) types a character the layout cannot, such as U+2713, as
        // Alt down, numpad digits with Alt held, then an Alt key-up whose character is the composed one.
        WindowsKeyTranslator.TryTranslateKey(true, Alt, '\0', LeftAltPressed, out _).ShouldBeFalse();
        foreach (var digit in new ushort[] { 0x62, 0x65, 0x61 })
        {
            WindowsKeyTranslator.TryTranslateKey(true, digit, '\0', LeftAltPressed, out _).ShouldBeFalse();
            WindowsKeyTranslator.TryTranslateKey(false, digit, '\0', LeftAltPressed, out _).ShouldBeFalse();
        }
        WindowsKeyTranslator.TryTranslateKey(false, Alt, '\u2713', 0, out var key).ShouldBeTrue();
        key.ShouldBe(new ConsoleKeyInfo('\u2713', (ConsoleKey)Alt, shift: false, alt: false, control: false));
    }

    [Fact]
    public void KeyDowns_KeepCharacterKeyAndModifiers()
    {
        WindowsKeyTranslator.TryTranslateKey(true, 0x41, 'A', ShiftPressed, out var shiftA).ShouldBeTrue();
        shiftA.ShouldBe(new ConsoleKeyInfo('A', ConsoleKey.A, shift: true, alt: false, control: false));
        WindowsKeyTranslator.TryTranslateKey(true, 0x46, 'f', LeftAltPressed, out var altF).ShouldBeTrue();
        altF.ShouldBe(new ConsoleKeyInfo('f', ConsoleKey.F, shift: false, alt: true, control: false));
        WindowsKeyTranslator.TryTranslateKey(true, 0x25, '\0', 0, out var left).ShouldBeTrue();
        left.Key.ShouldBe(ConsoleKey.LeftArrow);
        WindowsKeyTranslator.TryTranslateKey(false, 0x41, 'a', 0, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x14)]
    [InlineData(0x90)]
    [InlineData(0x91)]
    public void ModifierKeysOnTheirOwn_ProduceNoKey(int virtualKey)
    {
        WindowsKeyTranslator.TryTranslateKey(true, (ushort)virtualKey, '\0', 0, out _).ShouldBeFalse();
        WindowsKeyTranslator.TryTranslateKey(false, (ushort)virtualKey, '\0', 0, out _).ShouldBeFalse();
    }
}
