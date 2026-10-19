// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Layout;
using RazorConsole.Testing;
using RazorConsole.Testing.Vt;
using Spectre.Console;

namespace RazorConsole.Tests.Testing;

public sealed class VtEmulatorTests
{
    private const string Esc = "\u001b";

    [Fact]
    public void Sgr_SetsBasicBrightAndDefaultColors()
    {
        var emulator = Feed(20, 2, $"{Esc}[31;42mA{Esc}[91;102mB{Esc}[39mC{Esc}[49mD{Esc}[0mE");

        Cell(emulator, 0, 0).Style.ShouldBe(new Style(Color.FromInt32(1), Color.FromInt32(2)));
        Cell(emulator, 1, 0).Style.ShouldBe(new Style(Color.FromInt32(9), Color.FromInt32(10)));
        Cell(emulator, 2, 0).Style.ShouldBe(new Style(background: Color.FromInt32(10)));
        Cell(emulator, 3, 0).Style.ShouldBeNull();
        Cell(emulator, 4, 0).Style.ShouldBeNull();
    }

    [Fact]
    public void Sgr_SetsIndexedAndTrueColors_InSemicolonAndColonForms()
    {
        var emulator = Feed(20, 2, $"{Esc}[38;5;208;48;2;1;2;3mA{Esc}[m{Esc}[38:2::10:20:30;48:5:21mB{Esc}[m{Esc}[38:2:40:50:60mC");

        Cell(emulator, 0, 0).Style.ShouldBe(new Style(Color.FromInt32(208), new Color(1, 2, 3)));
        Cell(emulator, 1, 0).Style.ShouldBe(new Style(new Color(10, 20, 30), Color.FromInt32(21)));
        Cell(emulator, 2, 0).Style.ShouldBe(new Style(new Color(40, 50, 60)));
    }

    [Fact]
    public void Sgr_SetsAndResetsAttributes()
    {
        var emulator = Feed(
            20,
            2,
            $"{Esc}[1;3;4;5;7;8;9mA{Esc}[22;23;24;25;27;28;29mB{Esc}[2mC{Esc}[22mD{Esc}[4mE{Esc}[4:0mF{Esc}[21mG");

        Cell(emulator, 0, 0).Style!.Decoration.ShouldBe(
            Decoration.Bold | Decoration.Italic | Decoration.Underline | Decoration.SlowBlink
            | Decoration.Invert | Decoration.Conceal | Decoration.Strikethrough);
        Cell(emulator, 1, 0).Style.ShouldBeNull();
        Cell(emulator, 2, 0).Style!.Decoration.ShouldBe(Decoration.Dim);
        Cell(emulator, 3, 0).Style.ShouldBeNull();
        Cell(emulator, 4, 0).Style!.Decoration.ShouldBe(Decoration.Underline);
        Cell(emulator, 5, 0).Style.ShouldBeNull();
        Cell(emulator, 6, 0).Style!.Decoration.ShouldBe(Decoration.Underline);
    }

    [Fact]
    public void Sgr_InvalidExtendedColor_KeepsTheCurrentColor()
    {
        var emulator = Feed(10, 1, $"{Esc}[31mA{Esc}[38;5;999mB{Esc}[38;7mC");

        Cell(emulator, 1, 0).Style.ShouldBe(new Style(Color.FromInt32(1)));
        Cell(emulator, 2, 0).Style.ShouldBe(new Style(Color.FromInt32(1)));
    }

    [Fact]
    public void CursorMovement_PositionsAndClamps()
    {
        var emulator = Feed(10, 5, $"{Esc}[3;5HA{Esc}[2;2fB{Esc}[99;99HC{Esc}[HD{Esc}[3BE{Esc}[2CF{Esc}[1AG{Esc}[3DH{Esc}[8GI{Esc}[5dJ");

        // CUP, HVP, clamped CUP, home, CUD, CUF, CUU, CUB, CHA, VPA (keeps the column).
        Line(emulator, 0).ShouldBe("D");
        Line(emulator, 1).ShouldBe(" B");
        Line(emulator, 2).ShouldBe("   HAG I");
        Line(emulator, 3).ShouldBe(" E  F");
        Line(emulator, 4).ShouldBe("        JC");
        (emulator.CursorX, emulator.CursorY).ShouldBe((9, 4));

        Feed(emulator, $"{Esc}[1;1H{Esc}[5A{Esc}[20D");
        (emulator.CursorX, emulator.CursorY).ShouldBe((0, 0));
    }

    [Fact]
    public void ControlCharacters_MoveTheCursor()
    {
        var emulator = Feed(20, 3, "ab\bX\tT\r\nsecond\rS\u001b7\nthird\u001b8!");

        Line(emulator, 0).ShouldBe("aX      T");
        Line(emulator, 1).ShouldBe("S!cond");
        Line(emulator, 2).ShouldBe(" third");
    }

    [Fact]
    public void AutoWrap_WrapsAtTheRightMargin_AndCanBeDisabled()
    {
        var emulator = Feed(5, 3, "abcdefg");
        Line(emulator, 0).ShouldBe("abcde");
        Line(emulator, 1).ShouldBe("fg");

        Feed(emulator, $"{Esc}[3;1H{Esc}[?7lvwxyz123");
        Line(emulator, 2).ShouldBe("vwxy3");
    }

    [Fact]
    public void EraseInDisplay_AndEraseInLine_ClearTheRequestedCells()
    {
        var emulator = Feed(5, 3, "aaaaa\r\nbbbbb\r\nccccc");

        Feed(emulator, $"{Esc}[2;3H{Esc}[K");
        Line(emulator, 1).ShouldBe("bb");
        Feed(emulator, $"{Esc}[1;3H{Esc}[1K");
        Line(emulator, 0).ShouldBe("   aa");
        Feed(emulator, $"{Esc}[3;2H{Esc}[2K");
        Line(emulator, 2).ShouldBe(string.Empty);

        emulator = Feed(5, 3, "aaaaa\r\nbbbbb\r\nccccc");
        Feed(emulator, $"{Esc}[2;3H{Esc}[J");
        Lines(emulator).ShouldBe(["aaaaa", "bb", string.Empty]);

        emulator = Feed(5, 3, "aaaaa\r\nbbbbb\r\nccccc");
        Feed(emulator, $"{Esc}[2;3H{Esc}[1J");
        Lines(emulator).ShouldBe([string.Empty, "   bb", "ccccc"]);

        Feed(emulator, $"{Esc}[2J");
        Lines(emulator).ShouldBe([string.Empty, string.Empty, string.Empty]);
    }

    [Fact]
    public void Erase_UsesTheCurrentBackground()
    {
        var emulator = Feed(4, 1, $"abcd{Esc}[1;2H{Esc}[44m{Esc}[K");

        Cell(emulator, 0, 0).Style.ShouldBeNull();
        Cell(emulator, 1, 0).ShouldBe(new TerminalCell(" ", new Style(background: Color.FromInt32(4))));
        Cell(emulator, 3, 0).ShouldBe(new TerminalCell(" ", new Style(background: Color.FromInt32(4))));
    }

    [Fact]
    public void CharacterAndLineEditing_ShiftsCells()
    {
        var emulator = Feed(8, 4, $"abcdefgh{Esc}[1;3H{Esc}[2X");
        Line(emulator, 0).ShouldBe("ab  efgh");
        Feed(emulator, $"{Esc}[2P");
        Line(emulator, 0).ShouldBe("abefgh");
        Feed(emulator, $"{Esc}[3@");
        Line(emulator, 0).ShouldBe("ab   efg");

        emulator = Feed(3, 4, "1\r\n2\r\n3\r\n4");
        Feed(emulator, $"{Esc}[2;1H{Esc}[L");
        Lines(emulator).ShouldBe(["1", string.Empty, "2", "3"]);
        Feed(emulator, $"{Esc}[2M");
        Lines(emulator).ShouldBe(["1", "3", string.Empty, string.Empty]);
    }

    [Fact]
    public void AlternateScreen1049_PreservesTheNormalScreenAndCursor()
    {
        var emulator = Feed(10, 3, "normal\r\nscreen");
        var cursor = (emulator.CursorX, emulator.CursorY);

        Feed(emulator, $"{Esc}[?1049h");
        emulator.ActiveBuffer.ShouldBe(TerminalScreenBuffer.Alternate);
        Lines(emulator).ShouldBe([string.Empty, string.Empty, string.Empty]);
        Feed(emulator, $"{Esc}[Halternate");
        Line(emulator, 0).ShouldBe("alternate");

        Feed(emulator, $"{Esc}[?1049l");
        emulator.ActiveBuffer.ShouldBe(TerminalScreenBuffer.Normal);
        Lines(emulator).ShouldBe(["normal", "screen", string.Empty]);
        (emulator.CursorX, emulator.CursorY).ShouldBe(cursor);

        Feed(emulator, $"{Esc}[?1049h");
        Line(emulator, 0).ShouldBe(string.Empty);
    }

    [Fact]
    public void ScrollRegion_ScrollsOnlyTheRegion()
    {
        var emulator = Feed(3, 5, "1\r\n2\r\n3\r\n4\r\n5");

        Feed(emulator, $"{Esc}[2;4r");
        (emulator.CursorX, emulator.CursorY).ShouldBe((0, 0));
        Feed(emulator, $"{Esc}[4;1H\nX");
        Lines(emulator).ShouldBe(["1", "3", "4", "X", "5"]);

        Feed(emulator, $"{Esc}[2;1H{Esc}MY");
        Lines(emulator).ShouldBe(["1", "Y", "3", "4", "5"]);

        Feed(emulator, $"{Esc}[S");
        Lines(emulator).ShouldBe(["1", "3", "4", string.Empty, "5"]);
        Feed(emulator, $"{Esc}[2T");
        Lines(emulator).ShouldBe(["1", string.Empty, string.Empty, "3", "5"]);

        Feed(emulator, $"{Esc}[r{Esc}[5;1H\nZ");
        Lines(emulator).ShouldBe([string.Empty, string.Empty, "3", "5", "Z"]);
    }

    [Fact]
    public void ScrollRegion_WithOriginMode_AddressesRowsRelativeToTheRegion()
    {
        var responses = new List<string>();
        var emulator = new VtEmulator(5, 6, responses.Add);

        emulator.Feed($"{Esc}[3;5r{Esc}[?6h{Esc}[2;2HA{Esc}[99;1HB{Esc}[6n");

        Cell(emulator, 1, 3).Text.ShouldBe("A");
        Cell(emulator, 0, 4).Text.ShouldBe("B");
        responses.ShouldBe([$"{Esc}[3;2R"]);
    }

    [Fact]
    public void WideCharacters_OccupyTwoCells_AndOverwritingHalfBlanksTheOtherHalf()
    {
        var emulator = Feed(6, 2, "漢字ab");

        Cell(emulator, 0, 0).Text.ShouldBe("漢");
        Cell(emulator, 1, 0).Text.ShouldBe(string.Empty);
        Cell(emulator, 2, 0).Text.ShouldBe("字");
        Cell(emulator, 4, 0).Text.ShouldBe("a");
        emulator.CursorX.ShouldBe(5);

        Feed(emulator, $"{Esc}[1;2Hx");
        Line(emulator, 0).ShouldBe(" x字ab");

        Feed(emulator, $"{Esc}[1;3Hy");
        Line(emulator, 0).ShouldBe(" xy ab");
    }

    [Fact]
    public void WideCharacters_WrapWhenTheyDoNotFitOnTheLine()
    {
        var emulator = Feed(5, 2, "abcd漢");

        Line(emulator, 0).ShouldBe("abcd");
        Cell(emulator, 0, 1).Text.ShouldBe("漢");
    }

    [Fact]
    public void Graphemes_SurrogatePairsAndCombiningMarks_StayInOneCell_AcrossFeeds()
    {
        var emulator = new VtEmulator(10, 1);
        var emoji = "😀";

        emulator.Feed(emoji[..1]);
        emulator.Feed(emoji[1..] + "e");
        emulator.Feed("́x");

        Cell(emulator, 0, 0).Text.ShouldBe(emoji);
        Cell(emulator, 2, 0).Text.ShouldBe("é");
        Cell(emulator, 3, 0).Text.ShouldBe("x");
    }

    [Fact]
    public void Utf8_SplitAcrossChunks_IsDecodedOnce()
    {
        var bytes = Encoding.UTF8.GetBytes($"é漢😀{Esc}[31mok");
        var emulator = new VtEmulator(10, 1);

        foreach (var b in bytes)
        {
            emulator.Feed([b]);
        }

        Line(emulator, 0).ShouldBe("é漢😀ok");
        Cell(emulator, 5, 0).Style.ShouldBe(new Style(Color.FromInt32(1)));
    }

    [Fact]
    public void EscapeSequences_SplitAcrossFeeds_AreParsedOnce()
    {
        var emulator = new VtEmulator(10, 1);

        foreach (var part in new[] { Esc, "[", "3", "8;5", ";20", "8m", "A", Esc, "]0;ti", "tle", Esc, "\\", "B" })
        {
            emulator.Feed(part);
        }

        Line(emulator, 0).ShouldBe("AB");
        Cell(emulator, 0, 0).Style.ShouldBe(new Style(Color.FromInt32(208)));
    }

    [Fact]
    public void DecPrivateModes_TrackCursorMouseFocusPasteAndSynchronizedOutput()
    {
        var emulator = new VtEmulator(10, 2);

        emulator.Feed($"{Esc}[?1h{Esc}[?25l{Esc}[?1000;1006h{Esc}[?1004h{Esc}[?2004h{Esc}[?2026h");
        emulator.ApplicationCursorKeys.ShouldBeTrue();
        emulator.CursorVisible.ShouldBeFalse();
        emulator.MouseTracking.ShouldBe(TerminalMouseTracking.Normal | TerminalMouseTracking.SgrEncoding);
        emulator.FocusReporting.ShouldBeTrue();
        emulator.BracketedPaste.ShouldBeTrue();
        emulator.SynchronizedOutput.ShouldBeTrue();

        emulator.Feed($"{Esc}[?1003h");
        emulator.MouseTracking.ShouldBe(TerminalMouseTracking.Normal | TerminalMouseTracking.AnyEvent | TerminalMouseTracking.SgrEncoding);

        emulator.Feed($"{Esc}[?1l{Esc}[?25h{Esc}[?1000;1003;1006l{Esc}[?1004l{Esc}[?2004l{Esc}[?2026l");
        emulator.ApplicationCursorKeys.ShouldBeFalse();
        emulator.CursorVisible.ShouldBeTrue();
        emulator.MouseTracking.ShouldBe(TerminalMouseTracking.None);
        emulator.FocusReporting.ShouldBeFalse();
        emulator.BracketedPaste.ShouldBeFalse();
        emulator.SynchronizedOutput.ShouldBeFalse();
    }

    [Fact]
    public void Queries_AreAnswered_ThroughTheResponseCallback()
    {
        var responses = new List<string>();
        var emulator = new VtEmulator(80, 24, responses.Add);

        emulator.Feed($"{Esc}[5;7H{Esc}[6n{Esc}[5n{Esc}[c{Esc}[>c{Esc}[18t{Esc}[?2004h{Esc}[?2004$p{Esc}[?1049$p{Esc}[?9999$p");

        responses.ShouldBe([
            $"{Esc}[5;7R",
            $"{Esc}[0n",
            $"{Esc}[?1;2c",
            $"{Esc}[>0;10;1c",
            $"{Esc}[8;24;80t",
            $"{Esc}[?2004;1$y",
            $"{Esc}[?1049;2$y",
            $"{Esc}[?9999;0$y",
        ]);
    }

    [Fact]
    public void Hyperlinks_AreAttachedToTheStyle()
    {
        var emulator = Feed(20, 1, $"{Esc}]8;id=1;https://example.com/a{Esc}\\link{Esc}]8;;{Esc}\\ plain");

        Cell(emulator, 0, 0).Style!.Link.ShouldBe("https://example.com/a");
        Cell(emulator, 3, 0).Style!.Link.ShouldBe("https://example.com/a");
        Cell(emulator, 4, 0).Style.ShouldBeNull();
        Line(emulator, 0).ShouldBe("link plain");
    }

    [Fact]
    public void Resize_KeepsContent_AndClampsTheCursor()
    {
        var emulator = Feed(10, 4, "abcdefghij\r\nsecond\r\n\r\nlast");

        emulator.Resize(4, 2);

        Lines(emulator).ShouldBe(["abcd", "seco"]);
        (emulator.CursorX, emulator.CursorY).ShouldBe((3, 1));

        emulator.Resize(6, 3);
        Lines(emulator).ShouldBe(["abcd", "seco", string.Empty]);
    }

    [Fact]
    public void LineDrawingCharacterSet_MapsToBoxCharacters()
    {
        var emulator = Feed(10, 1, $"{Esc}(0lqqk{Esc}(Bx");

        Line(emulator, 0).ShouldBe("┌──┐x");
    }

    // ConPTY-specific output. Each input is a short handwritten sequence of the kind ConPTY emits; real ConPTY
    // output is covered live by the E2E and PTY-vs-headless golden tests.
    [Fact]
    public void ConPty_MergedMouseModeReset_TurnsMouseTrackingOff()
    {
        // Applications reset modes one by one; ConPTY forwards them merged into one sequence.
        var emulator = Feed(10, 1, $"{Esc}[?1003h{Esc}[?1006h");
        emulator.MouseTracking.ShouldBe(TerminalMouseTracking.AnyEvent | TerminalMouseTracking.SgrEncoding);

        Feed(emulator, $"{Esc}[?1003;1006l");

        emulator.MouseTracking.ShouldBe(TerminalMouseTracking.None);
    }

    [Fact]
    public void ConPty_Win32InputModeAndFocusReporting_AddedAtStartup_AreTolerated()
    {
        var responses = new List<string>();
        var emulator = new VtEmulator(10, 1, responses.Add);

        emulator.Feed($"{Esc}[?9001h{Esc}[?1004hok");

        Line(emulator, 0).ShouldBe("ok");
        (emulator.CursorX, emulator.CursorY).ShouldBe((2, 0));
        emulator.FocusReporting.ShouldBeTrue();
        emulator.MouseTracking.ShouldBe(TerminalMouseTracking.None);
        emulator.ActiveBuffer.ShouldBe(TerminalScreenBuffer.Normal);
        responses.ShouldBeEmpty();
    }

    [Fact]
    public void ConPty_WindowSizeReportOnResize_IsIgnored()
    {
        var responses = new List<string>();
        var emulator = new VtEmulator(20, 3, responses.Add);

        emulator.Feed($"a{Esc}[8;24;80tb");

        Line(emulator, 0).ShouldBe("ab");
        (emulator.Width, emulator.Height).ShouldBe((20, 3));
        (emulator.CursorX, emulator.CursorY).ShouldBe((2, 0));
        responses.ShouldBeEmpty();
    }

    [Fact]
    public void ConPty_SwallowedSynchronizedUpdate_DoesNotHoldBackFrames()
    {
        // PtyTerminal captures a frame after each read unless a synchronized update (?2026) is open.
        // ConPTY swallows ?2026h/l, so the output arrives without them.
        var emulator = Feed(10, 1, $"{Esc}[Hframe");
        emulator.SynchronizedOutput.ShouldBeFalse();
        Line(emulator, 0).ShouldBe("frame");

        // A stray reset without a preceding set must not open an update either.
        Feed(emulator, $"{Esc}[?2026l{Esc}[H{Esc}[Knext");
        emulator.SynchronizedOutput.ShouldBeFalse();
        Line(emulator, 0).ShouldBe("next");
    }

    [Fact]
    public void SynchronizedUpdate_IsOpenOnlyBetweenSetAndReset()
    {
        var emulator = Feed(10, 1, $"{Esc}[?2026h{Esc}[Hab");
        emulator.SynchronizedOutput.ShouldBeTrue();

        Feed(emulator, $"c{Esc}[?2026l");

        emulator.SynchronizedOutput.ShouldBeFalse();
        Line(emulator, 0).ShouldBe("abc");
    }

    [Fact]
    public void SynchronizedUpdateCount_CountsStartedUpdates_NotRepeatedSets()
    {
        var emulator = Feed(10, 1, $"{Esc}[?2026h{Esc}[?2026h");
        emulator.SynchronizedUpdateCount.ShouldBe(1);

        Feed(emulator, $"{Esc}[?2026l{Esc}[?2026h");
        emulator.SynchronizedUpdateCount.ShouldBe(2);
        emulator.SynchronizedOutput.ShouldBeTrue();

        emulator.EndSynchronizedOutput();
        emulator.SynchronizedOutput.ShouldBeFalse();
        emulator.SynchronizedUpdateCount.ShouldBe(2);

        Feed(emulator, $"{Esc}[?2026h");
        emulator.SynchronizedUpdateCount.ShouldBe(3);
    }

    [Fact]
    public void ConPty_WindowTitleTerminatedByBel_IsNotPrinted()
    {
        var emulator = Feed(20, 1, $"{Esc}]0;Gallery\aok");

        Line(emulator, 0).ShouldBe("ok");
        (emulator.CursorX, emulator.CursorY).ShouldBe((2, 0));
    }

    [Fact]
    public void ConPty_EraseCharactersThenCursorForward_ClearsWithoutMovingThenSkips()
    {
        // ConPTY writes runs of blanks as ECH + CUF instead of spaces.
        var emulator = Feed(10, 1, $"xxxxxxxxxx\rab{Esc}[3X{Esc}[3C|");

        Line(emulator, 0).ShouldBe("ab   |xxxx");
        (emulator.CursorX, emulator.CursorY).ShouldBe((6, 0));
        Cell(emulator, 2, 0).ShouldBe(TerminalCell.Empty);
    }

    [Fact]
    public void ConPty_EraseCharacters_WithABackground_LeavesBackgroundOnlyBlanks()
    {
        var emulator = Feed(10, 1, $"{Esc}[38;5;0m{Esc}[48;5;39mA{Esc}[3X{Esc}[m");

        Cell(emulator, 0, 0).ShouldBe(new TerminalCell("A", new Style(Color.FromInt32(0), Color.FromInt32(39))));
        Cell(emulator, 1, 0).ShouldBe(new TerminalCell(" ", new Style(background: Color.FromInt32(39))));
        Cell(emulator, 3, 0).ShouldBe(new TerminalCell(" ", new Style(background: Color.FromInt32(39))));
        Cell(emulator, 4, 0).ShouldBe(TerminalCell.Empty);
    }

    [Fact]
    public void ConPty_HyperlinkWithGeneratedId_IsAttachedToTheStyle()
    {
        // ConPTY replaces the application's link id with its own, such as "u-123%456".
        var emulator = Feed(10, 1, $"{Esc}[4mL{Esc}]8;id=u-12%34;https://e.x/a{Esc}\\K{Esc}]8;;{Esc}\\M");

        Cell(emulator, 0, 0).Style.ShouldBe(new Style(decoration: Decoration.Underline));
        Cell(emulator, 1, 0).Style.ShouldBe(new Style(decoration: Decoration.Underline, link: "https://e.x/a"));
        Cell(emulator, 2, 0).Style.ShouldBe(new Style(decoration: Decoration.Underline));
        Line(emulator, 0).ShouldBe("LKM");
    }

    [Fact]
    public void ConPtyTeardown_LeavesTheAlternateScreenAndDisablesMouseReporting()
    {
        // What ConPTY forwards when LLMAgentTUI exits on Ctrl+C.
        var emulator = Feed(80, 24, $"{Esc}[?1003;1006h{Esc}[?1049halt{Esc}[m{Esc}[?1049l{Esc}[H{Esc}[K\r\n{Esc}[K{Esc}[80C{Esc}[?1003;1006l");

        emulator.ActiveBuffer.ShouldBe(TerminalScreenBuffer.Normal);
        emulator.MouseTracking.ShouldBe(TerminalMouseTracking.None);
        Line(emulator, 0).ShouldBe(string.Empty);
        emulator.CursorX.ShouldBe(79);
    }

    private static VtEmulator Feed(int width, int height, string text)
    {
        var emulator = new VtEmulator(width, height);
        emulator.Feed(text);
        return emulator;
    }

    private static void Feed(VtEmulator emulator, string text) => emulator.Feed(text);

    private static TerminalCell Cell(VtEmulator emulator, int x, int y) => emulator.CreateFrame(0, DateTimeOffset.UnixEpoch)[x, y];

    private static string Line(VtEmulator emulator, int row) => emulator.CreateFrame(0, DateTimeOffset.UnixEpoch).GetLine(row).TrimEnd();

    private static string[] Lines(VtEmulator emulator)
    {
        var frame = emulator.CreateFrame(0, DateTimeOffset.UnixEpoch);
        return Enumerable.Range(0, frame.Height).Select(row => frame.GetLine(row).TrimEnd()).ToArray();
    }
}
