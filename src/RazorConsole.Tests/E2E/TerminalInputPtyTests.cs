// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Testing;

namespace RazorConsole.Tests.E2E;

/// <summary>
/// Real terminal input against the offline (<c>--mock</c>) LLMAgentTUI session. No provider requests, API keys or
/// real tool execution are used.
/// </summary>
[Trait("Category", "E2E")]
public sealed class TerminalInputPtyTests
{
    private const string Esc = "\u001b";

    [Fact]
    public async Task WheelPackets_SlashCompletion_AndCtrlC_WorkThroughARealTerminal()
    {
        await using var terminal = await PtyE2E.StartAsync("LLMAgentTUI.dll", 87, 35, respondToTerminalQueries: true, "--mock");
        var ct = TestContext.Current.CancellationToken;

        await terminal.WaitForTextAsync("LLMAgentTUI", cancellationToken: ct);
        await PtyE2E.SendAsync(terminal, "hello");
        await terminal.WaitForTextAsync("› hello", cancellationToken: ct);

        // 201 wheel packets, including one split by a pause longer than the input parser waits for the rest of an
        // escape sequence, must preserve the draft.
        for (var i = 0; i < 100; i++)
        {
            await PtyE2E.SendAsync(terminal, $"{Esc}[<64;10;10M{Esc}[<65;10;10M");
            await Task.Delay(10, ct);
        }

        await PtyE2E.SendAsync(terminal, $"{Esc}[<65;");
        await Task.Delay(150, ct);
        await PtyE2E.SendAsync(terminal, "10;10M!");
        var frame = await terminal.WaitForTextAsync("› hello!", cancellationToken: ct);
        Assert.False(frame.ContainsText("<;M"), frame.ToDiagnosticString());

        // Native arrow and Tab complete the selected slash command.
        await PtyE2E.SendAsync(terminal, new string('\u007f', 6) + "/");
        await terminal.WaitForTextAsync("choose what LLMAgentTUI", cancellationToken: ct);
        await PtyE2E.SendAsync(terminal, $"{Esc}[B\t");
        await terminal.WaitForTextAsync("› /usage", cancellationToken: ct);

        // Ctrl+C disables mouse reporting. Check the parsed state, not literal bytes: ConPTY merges the resets into
        // one sequence. macOS invalidates the slave's settings after the session leader exits, so only the
        // observable teardown is checked.
        Assert.NotEqual(TerminalMouseTracking.None, terminal.CurrentFrame.MouseTracking);
        await PtyE2E.SendAsync(terminal, "\u0003");
        await terminal.WaitUntilAsync(
            f => f.MouseTracking == TerminalMouseTracking.None,
            TimeSpan.FromSeconds(3),
            "mouse reporting disabled",
            ct);
    }
}
