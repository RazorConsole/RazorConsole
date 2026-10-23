// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Commands;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The value syntaxes: durations, keys, regions, escapes, environment variables, buttons and session names.
/// </summary>
public sealed class CliValueParsersTests
{
    [Theory]
    [InlineData("500ms", 500)]
    [InlineData("2s", 2000)]
    [InlineData("1.5s", 1500)]
    [InlineData("1m", 60_000)]
    [InlineData("0ms", 0)]
    [InlineData("10S", 10_000)]
    [InlineData("1440m", 86_400_000)]
    public void Duration_ParsesUnits(string text, int milliseconds)
    {
        CliValueParsers.TryParseDuration(text, out var value, out var error).ShouldBeTrue(error);
        value.ShouldBe(TimeSpan.FromMilliseconds(milliseconds));
    }

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("ms")]
    [InlineData("-1s")]
    [InlineData("1h")]
    [InlineData("1,5s")]
    [InlineData("1e3ms")]
    [InlineData("1441m")]
    public void Duration_RejectsOtherText(string text)
    {
        CliValueParsers.TryParseDuration(text, out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("Enter", '\r', ConsoleKey.Enter, ConsoleModifiers.None)]
    [InlineData("return", '\r', ConsoleKey.Enter, ConsoleModifiers.None)]
    [InlineData("tab", '\t', ConsoleKey.Tab, ConsoleModifiers.None)]
    [InlineData("Shift+Tab", '\t', ConsoleKey.Tab, ConsoleModifiers.Shift)]
    [InlineData("Esc", '\u001b', ConsoleKey.Escape, ConsoleModifiers.None)]
    [InlineData("Up", '\0', ConsoleKey.UpArrow, ConsoleModifiers.None)]
    [InlineData("PgDn", '\0', ConsoleKey.PageDown, ConsoleModifiers.None)]
    [InlineData("F5", '\0', ConsoleKey.F5, ConsoleModifiers.None)]
    [InlineData("Ctrl+Alt+Del", '\0', ConsoleKey.Delete, ConsoleModifiers.Control | ConsoleModifiers.Alt)]
    [InlineData("Space", ' ', ConsoleKey.Spacebar, ConsoleModifiers.None)]
    [InlineData(" ", ' ', ConsoleKey.Spacebar, ConsoleModifiers.None)]
    [InlineData("a", 'a', ConsoleKey.A, ConsoleModifiers.None)]
    [InlineData("A", 'A', ConsoleKey.A, ConsoleModifiers.Shift)]
    [InlineData("Shift+a", 'A', ConsoleKey.A, ConsoleModifiers.Shift)]
    [InlineData("ctrl+c", '\u0003', ConsoleKey.C, ConsoleModifiers.Control)]
    [InlineData("Ctrl+C", '\u0003', ConsoleKey.C, ConsoleModifiers.Control)]
    [InlineData("Ctrl+Shift+C", '\u0003', ConsoleKey.C, ConsoleModifiers.Control | ConsoleModifiers.Shift)]
    [InlineData("Alt+X", 'x', ConsoleKey.X, ConsoleModifiers.Alt)]
    [InlineData("Alt+x", 'x', ConsoleKey.X, ConsoleModifiers.Alt)]
    [InlineData("7", '7', ConsoleKey.D7, ConsoleModifiers.None)]
    [InlineData("+", '+', default(ConsoleKey), ConsoleModifiers.None)]
    [InlineData("é", 'é', default(ConsoleKey), ConsoleModifiers.None)]
    public void Key_ParsesNamesCharactersAndModifiers(string text, char keyChar, ConsoleKey key, ConsoleModifiers modifiers)
    {
        CliValueParsers.TryParseKey(text, out var value, out var error).ShouldBeTrue(error);
        value.ShouldBe(new ConsoleKeyInfo(
            keyChar,
            key,
            (modifiers & ConsoleModifiers.Shift) != 0,
            (modifiers & ConsoleModifiers.Alt) != 0,
            (modifiers & ConsoleModifiers.Control) != 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+a")]
    [InlineData("NotAKey")]
    [InlineData("12")]
    [InlineData("\u0007")]
    [InlineData("Play")]
    public void Key_RejectsUnknownOrUnencodableKeys(string text)
    {
        CliValueParsers.TryParseKey(text, out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Region_ParsesFourNumbers()
    {
        CliValueParsers.TryParseRegion("0, 2,40,10", out var region, out var error).ShouldBeTrue(error);
        region.ShouldBe(new DiagnosticsRegion(0, 2, 40, 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1,2,3")]
    [InlineData("1,2,3,4,5")]
    [InlineData("-1,0,1,1")]
    [InlineData("0,0,0,1")]
    [InlineData("0,0,1,0")]
    [InlineData("a,0,1,1")]
    public void Region_RejectsOtherText(string text)
    {
        CliValueParsers.TryParseRegion(text, out var region, out var error).ShouldBeFalse();
        region.ShouldBeNull();
        error.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(@"plain", "plain")]
    [InlineData(@"\e[A", "\u001b[A")]
    [InlineData(@"\x1b[1;5C", "\u001b[1;5C")]
    [InlineData(@"\u00e9\r\n\t\0\\", "é\r\n\t\0\\")]
    public void Escapes_AreDecoded(string text, string expected)
    {
        CliValueParsers.TryDecodeEscapes(text, out var value, out var error).ShouldBeTrue(error);
        value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"\")]
    [InlineData(@"\q")]
    [InlineData(@"\x1")]
    [InlineData(@"\xZZ")]
    [InlineData(@"\u12")]
    public void Escapes_RejectBadSequences(string text)
    {
        CliValueParsers.TryDecodeEscapes(text, out var value, out var error).ShouldBeFalse();
        value.ShouldBeNull();
        error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void EnvironmentVariable_SplitsAtTheFirstEquals()
    {
        CliValueParsers.TryParseEnvironmentVariable("A=b=c", out var value, out _).ShouldBeTrue();
        value.ShouldBe(new KeyValuePair<string, string>("A", "b=c"));
        CliValueParsers.TryParseEnvironmentVariable("A=", out value, out _).ShouldBeTrue();
        value.ShouldBe(new KeyValuePair<string, string>("A", string.Empty));
        CliValueParsers.TryParseEnvironmentVariable("=x", out _, out _).ShouldBeFalse();
        CliValueParsers.TryParseEnvironmentVariable("A", out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("left", 0)]
    [InlineData("Middle", 1)]
    [InlineData("RIGHT", 2)]
    public void Button_UsesDomNumbering(string text, int expected)
    {
        CliValueParsers.TryParseButton(text, out var value, out _).ShouldBeTrue();
        value.ShouldBe(expected);
        CliValueParsers.TryParseButton("back", out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("s1", true)]
    [InlineData("my-app_2.test", true)]
    [InlineData("9", true)]
    [InlineData("", false)]
    [InlineData(".hidden", false)]
    [InlineData("-x", false)]
    [InlineData("a/b", false)]
    [InlineData(@"a\b", false)]
    [InlineData("..", false)]
    [InlineData("café", false)]
    public void SessionName_AllowsOnlyFileSafeNames(string text, bool valid)
        => CliValueParsers.TryValidateSessionName(text, out _).ShouldBe(valid);

    [Fact]
    public void SessionName_IsLimitedTo64Characters()
    {
        CliValueParsers.TryValidateSessionName(new string('a', CliValueParsers.MaxSessionNameLength), out _).ShouldBeTrue();
        CliValueParsers.TryValidateSessionName(new string('a', CliValueParsers.MaxSessionNameLength + 1), out _).ShouldBeFalse();
    }
}
