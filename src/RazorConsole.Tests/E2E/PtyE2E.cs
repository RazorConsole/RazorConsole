// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text;
using RazorConsole.Testing;

namespace RazorConsole.Tests.E2E;

/// <summary>
/// Shared helpers for end-to-end tests that run a sample application in a real pseudoterminal.
/// </summary>
internal static class PtyE2E
{
    /// <summary>
    /// A terminal accepts input as long as the application reads it. Input that cannot be written within this time
    /// means the application stopped reading.
    /// </summary>
    private static readonly TimeSpan s_writeTimeout = TimeSpan.FromSeconds(3);

    public static async Task<PtyTerminal> StartAsync(string assembly, int width, int height, bool respondToTerminalQueries, params string[] arguments)
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "Pseudoterminals are not supported here.");
        var options = PtyTerminal.CreateDotnetOptions(Path.Combine(AppContext.BaseDirectory, assembly), width, height, arguments);
        options.RespondToTerminalQueries = respondToTerminalQueries;
        options.DefaultTimeout = TimeSpan.FromSeconds(20);
        return await PtyTerminal.StartAsync(options, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Writes raw terminal input, failing with "Input blocked" when the application stops reading.
    /// </summary>
    public static async Task SendAsync(PtyTerminal terminal, string data)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(s_writeTimeout);
        try
        {
            await terminal.SendTerminalInputAsync(data, timeout.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            Assert.Fail($"Input blocked for {s_writeTimeout}.{Environment.NewLine}{terminal.DumpDiagnostics()}");
        }
    }

    /// <summary>
    /// Waits for <paramref name="text"/> to appear at or after <paramref name="minColumn"/> and clicks its first cell.
    /// Coordinates come from the current frame, so the click follows the layout at the current size.
    /// </summary>
    public static async Task ClickTextAsync(PtyTerminal terminal, string text, int minColumn = 0)
    {
        (int X, int Y)? target = null;
        await terminal.WaitUntilAsync(
            frame => (target = Find(frame, text, minColumn)) is not null,
            description: $"clickable '{text}' at column {minColumn.ToString(CultureInfo.InvariantCulture)} or later",
            cancellationToken: TestContext.Current.CancellationToken);
        var (x, y) = target!.Value;
        await SendAsync(terminal, $"\u001b[<0;{x + 1};{y + 1}M\u001b[<0;{x + 1};{y + 1}m");
    }

    private static (int X, int Y)? Find(TerminalFrame frame, string text, int minColumn)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            // Map each character of the row back to its cell; wide characters occupy two cells.
            var line = new StringBuilder();
            var columns = new List<int>();
            for (var x = 0; x < frame.Width; x++)
            {
                foreach (var character in frame[x, y].Text)
                {
                    line.Append(character);
                    columns.Add(x);
                }
            }

            var start = 0;
            while (start < columns.Count && columns[start] < minColumn)
            {
                start++;
            }

            var index = line.ToString().IndexOf(text, start, StringComparison.Ordinal);
            if (index >= 0)
            {
                return (columns[index], y);
            }
        }

        return null;
    }
}
