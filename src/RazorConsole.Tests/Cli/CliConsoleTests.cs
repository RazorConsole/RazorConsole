// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// Human-readable output is UTF-8 whatever the console's code page is; the JSON stays ASCII.
/// </summary>
public sealed class CliConsoleTests
{
    // Rounded corners, a bullet, scrollbar arrows and Cyrillic: none of it fits cp866 or cp437.
    private const string Screen = "╭──╮\n│● │ ▲\n╰──╯ ▼\nПривет";

    [Fact]
    public void TextOutput_RoundTripsAsUtf8_WithoutAByteOrderMark()
    {
        using var stream = new MemoryStream();
        using (var writer = CliConsole.CreateWriter(stream))
        {
            CliOutput.Write(CliResult.Success("s1", null, Screen), json: false, writer, TextWriter.Null);
        }

        var bytes = stream.ToArray();
        bytes.Take(3).ShouldNotBe([0xEF, 0xBB, 0xBF]);
        new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).ShouldBe(Screen + Environment.NewLine);
        bytes.ShouldBe(Encoding.UTF8.GetBytes(Screen + Environment.NewLine));
    }

    [Fact]
    public void ErrorOutput_RoundTripsAsUtf8()
    {
        using var stream = new MemoryStream();
        var data = System.Text.Json.JsonDocument.Parse("""{"screen":"╭─╮\n● ▼"}""").RootElement.Clone();
        using (var writer = CliConsole.CreateWriter(stream))
        {
            CliOutput.Write(CliResult.Failure("assertion_failed", "Expected ●.", data), json: false, TextWriter.Null, writer);
        }

        var text = Encoding.UTF8.GetString(stream.ToArray());
        text.ShouldContain("Expected ●. [assertion_failed]");
        text.ShouldContain("╭─╮");
        text.ShouldContain("● ▼");
    }

    [Fact]
    public void Writes_AreFlushedAtOnce()
    {
        using var stream = new MemoryStream();
        using var writer = CliConsole.CreateWriter(stream);

        writer.Write("▲");

        stream.Length.ShouldBe(Encoding.UTF8.GetByteCount("▲"));
    }

    [Fact]
    public void JsonOutput_StaysAscii()
    {
        using var stream = new MemoryStream();
        var data = System.Text.Json.JsonDocument.Parse("{\"lines\":[\"╭──╮\",\"● ▼\"]}").RootElement.Clone();
        using (var writer = CliConsole.CreateWriter(stream))
        {
            CliOutput.Write(CliResult.Success("s1", data, null), json: true, writer, TextWriter.Null);
        }

        stream.ToArray().ShouldAllBe(static value => value < 0x80);
    }
}
