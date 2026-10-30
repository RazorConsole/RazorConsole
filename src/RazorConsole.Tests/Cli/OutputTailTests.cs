// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using RazorConsole.Cli.Host;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The plain-text end of a terminal's output that a <c>process_exited</c> error carries, and how the error is printed.
/// </summary>
public sealed class OutputTailTests
{
    [Fact]
    public void Clean_RemovesControlSequences_AndKeepsTheText()
    {
        const string raw = "\u001b[?1049h\u001b[2J\u001b[1;31mUnhandled exception.\u001b[0m System.InvalidOperationException: diag crash\r\n"
            + "\u001b]0;title\u0007   at Program.<Main>$(String[] args)\r\n\u001b(B\u001b[?25h";

        OutputTail.Clean(raw).ShouldBe("Unhandled exception. System.InvalidOperationException: diag crash\n   at Program.<Main>$(String[] args)");
    }

    [Fact]
    public void Clean_TreatsACursorPositionAsALineBreak_AndDropsEmptyLines()
    {
        // ConPTY writes the next line with a cursor position instead of a line break.
        OutputTail.Clean("first\u001b[3;1Hsecond\r\n\r\n\u001b[5;1H\u001b[6;1Hthird\rfourth").ShouldBe("first\nsecond\nthird\nfourth");
    }

    [Fact]
    public void Clean_KeepsTheLastFortyLines()
    {
        var raw = string.Concat(Enumerable.Range(1, 100).Select(static number => $"line {number}\r\n"));

        var lines = OutputTail.Clean(raw)!.Split('\n');

        lines.Length.ShouldBe(OutputTail.MaxLines);
        lines[0].ShouldBe("line 61");
        lines[^1].ShouldBe("line 100");
    }

    [Fact]
    public void Clean_KeepsAtMost4KB_FromTheEnd()
    {
        var raw = string.Concat(Enumerable.Range(1, 30).Select(static number => $"{number:00} {new string('x', 300)}\r\n"));

        var tail = OutputTail.Clean(raw)!;

        tail.Length.ShouldBe(OutputTail.MaxCharacters);
        tail.ShouldEndWith("30 " + new string('x', 300));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\u001b[2J\u001b[H\u001b[?25l")]
    [InlineData("  \r\n\r\n   \r\n")]
    public void Clean_ReturnsNull_WhenNoTextIsLeft(string raw) => OutputTail.Clean(raw).ShouldBeNull();

    [Fact]
    public void From_ReadsTheEndOfTheRing()
    {
        var ring = new OutputRing(256 * 1024);
        ring.Append(new string('a', 100_000) + "\r\n");
        ring.Append("\u001b[31mthe end\u001b[0m\r\n");

        OutputTail.From(ring).ShouldEndWith("the end");
        OutputTail.From(new OutputRing(64)).ShouldBeNull();
    }

    [Fact]
    public void AProcessExitedError_PrintsLastErrorsAndOutputTailAfterTheMessage()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            exitCode = 134,
            lastErrors = new[] { new { seq = 7, time = "2026-10-30T10:00:00+00:00", source = "render", type = "System.InvalidOperationException", message = "diag failure", stack = "at X" } },
            outputTail = "Unhandled exception. System.InvalidOperationException: diag crash",
        });
        var result = CliResult.Failure(DiagnosticsErrorCodes.ProcessExited, "The process of 's1' has exited with exit code 134.", data, "s1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        CliOutput.Write(result, json: false, output, error);

        var lines = error.ToString().ReplaceLineEndings("\n").TrimEnd().Split('\n');
        lines.ShouldBe(
        [
            "razorconsole: The process of 's1' has exited with exit code 134. [process_exited]",
            "--- last errors ---",
            "[7] 2026-10-30T10:00:00+00:00 render System.InvalidOperationException: diag failure",
            "--- output tail ---",
            "Unhandled exception. System.InvalidOperationException: diag crash",
        ]);
        output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void AProcessExitedError_WithOnlyAnExitCode_PrintsNoBlocks_AndJsonCarriesTheFields()
    {
        var plain = CliResult.Failure(DiagnosticsErrorCodes.ProcessExited, "gone", JsonSerializer.SerializeToElement(new { exitCode = 3 }), "s1");
        using var output = new StringWriter();
        using var error = new StringWriter();
        CliOutput.Write(plain, json: false, output, error);
        error.ToString().ReplaceLineEndings("\n").ShouldBe("razorconsole: gone [process_exited]\n");

        var full = CliResult.Failure(
            DiagnosticsErrorCodes.ProcessExited,
            "gone",
            JsonSerializer.SerializeToElement(new { exitCode = 1, outputTail = "boom", lastErrors = new[] { new { seq = 1, message = "m" } } }),
            "s1");
        using var document = JsonDocument.Parse(CliOutput.ToJson(full));
        var data = document.RootElement.GetProperty("error").GetProperty("data");
        data.GetProperty("exitCode").GetInt32().ShouldBe(1);
        data.GetProperty("outputTail").GetString().ShouldBe("boom");
        data.GetProperty("lastErrors").EnumerateArray().ShouldHaveSingleItem().GetProperty("message").GetString().ShouldBe("m");
    }
}
