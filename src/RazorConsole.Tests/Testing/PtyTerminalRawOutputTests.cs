// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Testing;

namespace RazorConsole.Tests.Testing;

/// <summary>
/// The internal raw-output observer and capacity, which let a long-lived session keep its own bounded copy of the
/// output instead of the terminal's unbounded one.
/// </summary>
public sealed class PtyTerminalRawOutputTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Observer_ReceivesEveryChunkInOrder_AndRawOutputKeepsEverythingByDefault()
    {
        var observed = new Observed();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, observed, capacity: null);

        connection.Output("ab");
        connection.Output([(byte)'c', 0xC3]);
        connection.Output([0xA9, (byte)'d']);
        await observed.WaitForAsync("abcéd", terminal);

        terminal.RawOutput.ShouldBe("abcéd");
        observed.Chunks.ShouldBe(["ab", "c", "éd"]);
    }

    [Fact]
    public async Task Capacity_KeepsOnlyTheMostRecentCharacters()
    {
        var observed = new Observed();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, observed, capacity: 4);

        connection.Output("abc");
        await observed.WaitForAsync("abc", terminal);
        terminal.RawOutput.ShouldBe("abc");

        connection.Output("def");
        await observed.WaitForAsync("abcdef", terminal);
        terminal.RawOutput.ShouldBe("cdef");

        connection.Output("ghijklmn");
        await observed.WaitForAsync("abcdefghijklmn", terminal);
        terminal.RawOutput.ShouldBe("klmn");
        terminal.DumpDiagnostics().ShouldContain("Output: 14 chars");
    }

    [Fact]
    public async Task ZeroCapacity_KeepsNothing_WhileTheObserverSeesEverything()
    {
        var observed = new Observed();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, observed, capacity: 0);

        connection.Output("hello");
        await observed.WaitForAsync("hello", terminal);

        terminal.RawOutput.ShouldBeEmpty();
        terminal.CurrentFrame.ContainsText("hello").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task Capacity_NeverKeepsHalfOfASurrogatePair()
    {
        var observed = new Observed();
        await using var connection = new FakePtyConnection();
        await using var terminal = Start(connection, observed, capacity: 2);

        connection.Output("a\U0001F600b");
        await observed.WaitForAsync("a\U0001F600b", terminal);

        terminal.RawOutput.ShouldBe("b");
    }

    [Fact]
    public async Task ThrowingObserver_StopsReadingAndIsReported()
    {
        await using var connection = new FakePtyConnection();
        var options = new PtyTerminalOptions { FileName = "fake", Width = 20, Height = 3 };
        options.RawOutputObserver = _ => throw new InvalidOperationException("observer failed");
        await using var terminal = PtyTerminal.Start(connection, options, TimeProvider.System);

        connection.Output("x");

        var limit = DateTime.UtcNow + WaitLimit;
        while (!terminal.DumpDiagnostics().Contains("observer failed", StringComparison.Ordinal))
        {
            DateTime.UtcNow.ShouldBeLessThan(limit, terminal.DumpDiagnostics());
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task NegativeCapacity_IsRejected()
    {
        await using var connection = new FakePtyConnection();
        var options = new PtyTerminalOptions { FileName = "fake", RawOutputCapacity = -1 };

        Should.Throw<ArgumentOutOfRangeException>(() => PtyTerminal.Start(connection, options, TimeProvider.System));
    }

    private static PtyTerminal Start(FakePtyConnection connection, Observed observed, int? capacity)
    {
        var options = new PtyTerminalOptions { FileName = "fake", Width = 20, Height = 3, RawOutputCapacity = capacity };
        options.RawOutputObserver = observed.Add;
        return PtyTerminal.Start(connection, options, TimeProvider.System);
    }

    private sealed class Observed
    {
        private readonly object _sync = new();
        private readonly List<string> _chunks = [];
        private readonly StringBuilder _all = new();

        public IReadOnlyList<string> Chunks
        {
            get
            {
                lock (_sync)
                {
                    return [.. _chunks];
                }
            }
        }

        public void Add(ReadOnlySpan<char> output)
        {
            lock (_sync)
            {
                _chunks.Add(output.ToString());
                _all.Append(output);
            }
        }

        public async Task WaitForAsync(string expected, PtyTerminal terminal)
        {
            var limit = DateTime.UtcNow + WaitLimit;
            while (true)
            {
                lock (_sync)
                {
                    if (_all.ToString() == expected)
                    {
                        return;
                    }
                }

                DateTime.UtcNow.ShouldBeLessThan(limit, terminal.DumpDiagnostics());
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }
        }
    }
}
