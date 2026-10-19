// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text;
using RazorConsole.Core.Layout;
using RazorConsole.Testing;
using RazorConsole.Tests.PtyApp;
using Spectre.Console;

namespace RazorConsole.Tests.Testing;

/// <summary>
/// The same component rendered in-process by <see cref="HeadlessTerminal"/> and out-of-process through a real
/// pseudoterminal by <see cref="PtyTerminal"/> must produce the same cells: text, colors, decorations and links.
/// </summary>
public sealed class PtyGoldenTests
{
    [Theory]
    [InlineData(80, 24)]
    [InlineData(57, 15)]
    public async Task GoldenView_RendersIdenticalCells_InPtyAndHeadlessTerminals(int width, int height)
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "Pseudoterminals are not supported here.");
        await using var headless = await HeadlessTerminal.StartAsync<GoldenView>(width, height, cancellationToken: TestContext.Current.CancellationToken);
        var expected = await headless.WaitForTextAsync("Right", cancellationToken: TestContext.Current.CancellationToken);

        var options = PtyTerminal.CreateDotnetOptions(
            Path.Combine(AppContext.BaseDirectory, "RazorConsole.Tests.PtyApp.dll"), width, height, ["golden"]);
        options.Environment["COLORTERM"] = "truecolor";
        await using var pty = await PtyTerminal.StartAsync(options, TestContext.Current.CancellationToken);

        string? lastDifference = null;
        try
        {
            await pty.WaitUntilAsync(
                frame => (lastDifference = Compare(expected, frame)) is null,
                TimeSpan.FromSeconds(30),
                "cells identical to the headless frame",
                TestContext.Current.CancellationToken);
        }
        catch (TerminalTimeoutException exception)
        {
            Assert.Fail($"{lastDifference}{Environment.NewLine}Headless frame:{Environment.NewLine}{expected.ToDiagnosticString()}{Environment.NewLine}{exception.Message}");
        }
    }

    private static string? Compare(TerminalFrame expected, TerminalFrame actual)
    {
        if ((expected.Width, expected.Height) != (actual.Width, actual.Height))
        {
            return $"Size differs: headless {expected.Width}x{expected.Height}, PTY {actual.Width}x{actual.Height}.";
        }

        var differences = new StringBuilder();
        var count = 0;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var want = expected[x, y];
                var got = actual[x, y];
                if (want.Text == got.Text && Visible(want).Equals(Visible(got)))
                {
                    continue;
                }

                if (count++ < 20)
                {
                    differences.Append(CultureInfo.InvariantCulture, $"({x},{y}): headless '{want.Text}' {Describe(want.Style)}, PTY '{got.Text}' {Describe(got.Style)}").AppendLine();
                }
            }
        }

        return count == 0 ? null : $"{count} cells differ:{Environment.NewLine}{differences}";
    }

    private static Style Normalize(Style? style) => style ?? Style.Plain;

    /// <summary>
    /// ConPTY re-renders the application's output and does not keep the foreground color or decorations of blank
    /// cells where they cannot be seen (for example, "│ \e[38;5;9mInside" for a space the application wrote in the
    /// border color). On Windows, compare only what is visible on a blank cell; elsewhere compare the full style.
    /// </summary>
    private static Style Visible(TerminalCell cell)
    {
        var style = Normalize(cell.Style);
        const Decoration visibleOnBlank = Decoration.Underline | Decoration.Strikethrough | Decoration.Invert;
        if (!OperatingSystem.IsWindows() || cell.Text != " " || (style.Decoration & visibleOnBlank) != 0)
        {
            return style;
        }

        return new Style(background: style.Background, link: style.Link);
    }

    private static string Describe(Style? style) => Normalize(style) is var s && s.Equals(Style.Plain)
        ? "[plain]"
        : $"[fg {s.Foreground.ToMarkup()} bg {s.Background.ToMarkup()} {s.Decoration} link {s.Link ?? "-"}]";
}
