// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Host;
using RazorConsole.Cli.Operations;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// <c>host.log</c> keeps what would vanish with the background host process: unexpected failures. It is capped.
/// </summary>
public sealed class HostLogTests : IDisposable
{
    private const int TestTimeoutMs = 60_000;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rc-log-" + Guid.NewGuid().ToString("N")[..8]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HostFiles Files => new(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void TheLog_RotatesAtTheCap_AndKeepsOnePreviousFile()
    {
        var files = Files;
        Directory.CreateDirectory(files.StateDirectory);
        var chunk = new string('x', 100 * 1024);

        for (var i = 0; i < 30; i++)
        {
            files.TryAppendLog(chunk);
        }

        // 30 x 100 KiB is about 3 MiB: the current file stays under the cap plus one line, one older file is kept.
        new FileInfo(files.LogFile).Length.ShouldBeLessThan(HostFiles.MaxLogBytes + chunk.Length + 100);
        File.Exists(files.RotatedLogFile).ShouldBeTrue();
        new FileInfo(files.RotatedLogFile).Length.ShouldBeGreaterThanOrEqualTo(HostFiles.MaxLogBytes);
        File.Exists(files.LogFile + ".2").ShouldBeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task AFailureInAClientConnection_IsLogged_AndTheHostKeepsServing()
    {
        var files = Files;
        var host = new SessionHost(files, TimeSpan.FromMinutes(5), "1.0.0");
        var run = Task.Run(() => host.RunAsync(CancellationToken.None), Token);
        try
        {
            var connector = new HostConnector(files, new InProcessLauncher(TimeSpan.FromMinutes(5), "1.0.0"), "1.0.0");
            await UntilAsync(async () => await connector.ConnectExistingAsync(Token) is { } probe && await Dispose(probe));

            await host.ServeAsync(new FailingStream(), Token);

            var log = File.ReadAllText(files.LogFile);
            log.ShouldContain("A client connection to the session host failed");
            log.ShouldContain("the stream broke");

            // The host is still there for the next client.
            await using var client = (await connector.ConnectExistingAsync(Token)).ShouldNotBeNull();
            run.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            host.RequestShutdown();
            await run.WaitAsync(Patience, Token);
        }
    }

    private static async Task<bool> Dispose(HostClient client)
    {
        await client.DisposeAsync();
        return true;
    }

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for the host.");
            }

            await Task.Delay(50, Token);
        }
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the stream broke");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => throw new InvalidOperationException("the stream broke");

        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("the stream broke");

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
