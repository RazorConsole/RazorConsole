// Copyright (c) RazorConsole. All rights reserved.

using System.Text;

namespace RazorConsole.Tests.E2E;

/// <summary>
/// Wheel-storm regression for the Gallery in a real pseudoterminal.
/// </summary>
[Trait("Category", "E2E")]
[Collection(GalleryWheelStressCollection.Name)]
public sealed class GalleryScrollPtyTests
{
    private const string Esc = "\u001b";

    [Fact]
    public async Task WheelStorm_WithResizeAndStaleReplies_KeepsTheGalleryResponsive()
    {
        // Never answer cursor-position queries: rendering must not wait for a reply.
        await using var terminal = await PtyE2E.StartAsync("RazorConsole.Gallery.dll", 100, 35, respondToTerminalQueries: false);
        var ct = TestContext.Current.CancellationToken;

        await terminal.WaitForTextAsync("Hello, layout!", cancellationToken: ct);
        await PtyE2E.ClickTextAsync(terminal, "Scrollable");
        await terminal.WaitForTextAsync("Embedded scrollbar", cancellationToken: ct);
        await PtyE2E.ClickTextAsync(terminal, "Code", minColumn: 27);
        await terminal.WaitForTextAsync("AlphabetData", cancellationToken: ct);

        // Burst writes let input and rendering overlap; do not serialize per event.
        for (var batch = 0; batch < 40; batch++)
        {
            var code = batch % 2 == 0 ? 65 : 64;
            await PtyE2E.SendAsync(terminal, Repeat($"{Esc}[<{code};50;15M", 25));
            if (batch == 19)
            {
                terminal.Resize(80, 24);
            }

            // Input pacing, not synchronization.
            await Task.Delay(10, ct);
        }

        // Delayed or stale replies must not be interpreted as keys.
        await PtyE2E.SendAsync(terminal, $"{Esc}[12;");
        await Task.Delay(100, ct);
        await PtyE2E.SendAsync(terminal, "40R");
        await PtyE2E.SendAsync(terminal, Repeat($"{Esc}[<64;50;15M", 100));

        // Search stays at a fixed position while the main pane scrolls. Its focused placeholder is a FIFO barrier:
        // all queued wheel events have been handled before this click.
        await PtyE2E.ClickTextAsync(terminal, "Search");
        await terminal.WaitForTextAsync("Type to search", TimeSpan.FromSeconds(60), ct);
        await PtyE2E.SendAsync(terminal, "border");
        await terminal.WaitForTextAsync("border", cancellationToken: ct);
        await PtyE2E.ClickTextAsync(terminal, "Border");
        await terminal.WaitForTextAsync("Compare border styles", cancellationToken: ct);

        await PtyE2E.SendAsync(terminal, "\u0003");
        var exitCode = await terminal.WaitForExitAsync(TimeSpan.FromSeconds(3), ct);

        Assert.Equal(0, exitCode);

        // RawOutput is cumulative, so a sequence split across reads is still found. On Windows this checks what
        // ConPTY forwards to the hosting terminal; on Linux and macOS it is the application's own output.
        Assert.DoesNotContain($"{Esc}[6n", terminal.RawOutput, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Esc}[3J", terminal.RawOutput, StringComparison.Ordinal);
    }

    private static string Repeat(string value, int count)
        => new StringBuilder(value.Length * count).Insert(0, value, count).ToString();
}
