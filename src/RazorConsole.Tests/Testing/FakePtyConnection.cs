// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Concurrent;
using System.Text;
using RazorConsole.Testing.Pty;

namespace RazorConsole.Tests.Testing;

/// <summary>
/// A connection whose output the test writes chunk by chunk, so <see cref="RazorConsole.Testing.PtyTerminal"/> can be
/// tested without a real terminal.
/// </summary>
internal sealed class FakePtyConnection : PtyConnection, IAsyncDisposable
{
    private readonly BlockingCollection<byte[]> _output = [];

    public override int ProcessId => 1;

    public void Output(string text) => _output.Add(Encoding.UTF8.GetBytes(text));

    public void Output(byte[] data) => _output.Add(data);

    public void CloseOutput() => _output.CompleteAdding();

    public void Exit(int exitCode) => SetExited(exitCode);

    public override int Read(Span<byte> buffer)
    {
        if (!_output.TryTake(out var data, Timeout.Infinite))
        {
            return 0;
        }

        data.CopyTo(buffer);
        return data.Length;
    }

    public override void Write(ReadOnlySpan<byte> data)
    {
    }

    public override void Resize(int width, int height)
    {
    }

    public override void Kill() => SetExited(137);

    public override void StopReading() => _output.CompleteAdding();

    public override void Dispose(bool readerStopped)
    {
    }

    // The terminal is disposed first and stops the reader; the collection can then be released.
    public ValueTask DisposeAsync()
    {
        _output.Dispose();
        return ValueTask.CompletedTask;
    }
}
