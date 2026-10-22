// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Diagnostics;

public sealed class DiagnosticsTransportTests : IDisposable
{
    private const int TestTimeoutMs = 30_000;

    private readonly string _stateDirectory = Path.Combine(Path.GetTempPath(), "rc-diag-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ListenAndConnect_ExchangeHello()
    {
        var ct = TestContext.Current.CancellationToken;
        var endpoint = Endpoint();
        await using var listener = DiagnosticsTransport.Listen(endpoint);

        var acceptTask = listener.AcceptAsync(ct);
        await using var client = new DiagnosticsConnection(await DiagnosticsTransport.ConnectAsync(endpoint, TimeSpan.FromSeconds(5), ct));
        await using var server = new DiagnosticsConnection(await acceptTask);

        await client.WriteAsync(DiagnosticsMessage.Request(1, DiagnosticsProtocol.HelloMethod, new HelloParams(DiagnosticsProtocol.Version, "test"), DiagnosticsJsonContext.Default.HelloParams), ct);
        var request = await server.ReadAsync(ct);
        request!.GetParams(DiagnosticsJsonContext.Default.HelloParams)!.Client.ShouldBe("test");

        await server.WriteAsync(DiagnosticsMessage.Success(request.Id!.Value, new HelloResult(DiagnosticsProtocol.Version, "server", Environment.ProcessId), DiagnosticsJsonContext.Default.HelloResult), ct);
        var response = await client.ReadAsync(ct);
        response!.GetResult(DiagnosticsJsonContext.Default.HelloResult)!.ProcessId.ShouldBe(Environment.ProcessId);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task BothSidesWritingFarMoreThanTheBuffer_DoNotDeadlock()
    {
        // Both sides finish all their writes before either calls ReadAsync. Each side's write fills the pipe or
        // socket buffer many times over, so this only completes when each connection drains its peer on its own.
        const int MessageCount = 4;
        const int MessageChars = 1024 * 1024;

        var ct = TestContext.Current.CancellationToken;
        var endpoint = Endpoint();
        await using var listener = DiagnosticsTransport.Listen(endpoint);

        var acceptTask = listener.AcceptAsync(ct);
        await using var client = new DiagnosticsConnection(await DiagnosticsTransport.ConnectAsync(endpoint, TimeSpan.FromSeconds(5), ct));
        await using var server = new DiagnosticsConnection(await acceptTask);

        await Task.WhenAll(WriteAllAsync(client, 'a'), WriteAllAsync(server, 'n'));

        await ReadAllAsync(server, 'a');
        await ReadAllAsync(client, 'n');

        async Task WriteAllAsync(DiagnosticsConnection connection, char first)
        {
            for (var i = 0; i < MessageCount; i++)
            {
                var payload = new string((char)(first + i), MessageChars);
                await connection.WriteAsync(DiagnosticsMessage.Failure(i, new DiagnosticsError(DiagnosticsErrorCodes.Internal, payload)), ct);
            }
        }

        async Task ReadAllAsync(DiagnosticsConnection connection, char first)
        {
            for (var i = 0; i < MessageCount; i++)
            {
                var message = await connection.ReadAsync(ct);
                message.ShouldNotBeNull();
                message.Id.ShouldBe(i);
                message.Error.ShouldNotBeNull();
                message.Error.Message.Length.ShouldBe(MessageChars);
                message.Error.Message.AsSpan().IndexOfAnyExcept((char)(first + i)).ShouldBe(-1);
            }
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task PeerFloodingAnUnreadConnection_IsDisconnectedWithQueueOverflow_AndTheServerStaysHealthy()
    {
        const int Limit = 8;
        var ct = TestContext.Current.CancellationToken;
        var endpoint = Endpoint();
        await using var listener = DiagnosticsTransport.Listen(endpoint);

        var acceptTask = listener.AcceptAsync(ct);
        await using var flooder = new DiagnosticsConnection(await DiagnosticsTransport.ConnectAsync(endpoint, TimeSpan.FromSeconds(5), ct));
        await using var server = new DiagnosticsConnection(await acceptTask, maxQueuedMessages: Limit);

        // The server never reads while the peer sends far past its limit.
        try
        {
            for (var i = 0; i < 100 * Limit; i++)
            {
                await flooder.WriteAsync(DiagnosticsMessage.Failure(i, new DiagnosticsError(DiagnosticsErrorCodes.Internal, "flood")), ct);
            }
        }
        catch (IOException)
        {
            // The server closed the connection mid-flood.
        }

        await server.Completion.WaitAsync(ct);

        // The flooder is told why and then disconnected.
        var notice = await flooder.ReadAsync(ct);
        notice.ShouldNotBeNull();
        notice.Error.ShouldNotBeNull();
        notice.Error.Code.ShouldBe(DiagnosticsErrorCodes.QueueOverflow);
        var end = await Record.ExceptionAsync(async () => (await flooder.ReadAsync(ct)).ShouldBeNull());
        end?.ShouldBeAssignableTo<IOException>();

        // The server keeps what it accepted, then reports the overflow instead of growing.
        for (var i = 0; i < Limit; i++)
        {
            (await server.ReadAsync(ct))!.Id.ShouldBe(i);
        }

        (await Should.ThrowAsync<DiagnosticsProtocolException>(() => server.ReadAsync(ct).AsTask())).Code.ShouldBe(DiagnosticsErrorCodes.QueueOverflow);

        // And the listener still serves new clients.
        var nextAccept = listener.AcceptAsync(ct);
        await using var next = new DiagnosticsConnection(await DiagnosticsTransport.ConnectAsync(endpoint, TimeSpan.FromSeconds(5), ct));
        await using var nextServer = new DiagnosticsConnection(await nextAccept);
        await next.WriteAsync(DiagnosticsMessage.Request(1, DiagnosticsProtocol.HelloMethod, new HelloParams(DiagnosticsProtocol.Version, "next"), DiagnosticsJsonContext.Default.HelloParams), ct);
        (await nextServer.ReadAsync(ct))!.GetParams(DiagnosticsJsonContext.Default.HelloParams)!.Client.ShouldBe("next");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ReadingFreesQueueSpace()
    {
        var ct = TestContext.Current.CancellationToken;
        var endpoint = Endpoint();
        await using var listener = DiagnosticsTransport.Listen(endpoint);

        var acceptTask = listener.AcceptAsync(ct);
        await using var client = new DiagnosticsConnection(await DiagnosticsTransport.ConnectAsync(endpoint, TimeSpan.FromSeconds(5), ct));
        await using var server = new DiagnosticsConnection(await acceptTask, maxQueuedMessages: 1, maxQueuedBytes: 1024);

        // One message in flight at a time: far more than the limit in total, never more than it at once.
        for (var i = 0; i < 20; i++)
        {
            await client.WriteAsync(DiagnosticsMessage.Failure(i, new DiagnosticsError(DiagnosticsErrorCodes.Internal, new string('r', 200))), ct);
            (await server.ReadAsync(ct))!.Id.ShouldBe(i);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Listener_AcceptsSeveralClients()
    {
        var ct = TestContext.Current.CancellationToken;
        var endpoint = Endpoint();
        await using var listener = DiagnosticsTransport.Listen(endpoint);

        for (var i = 0; i < 3; i++)
        {
            var acceptTask = listener.AcceptAsync(ct);
            await using var client = await DiagnosticsTransport.ConnectAsync(endpoint, TimeSpan.FromSeconds(5), ct);
            await using var server = await acceptTask;

            await client.WriteAsync(new byte[] { (byte)i }, ct);
            var buffer = new byte[1];
            (await server.ReadAsync(buffer, ct)).ShouldBe(1);
            buffer[0].ShouldBe((byte)i);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Listen_FailsWhileAnotherListenerOwnsTheEndpoint()
    {
        var endpoint = Endpoint();
        await using var listener = DiagnosticsTransport.Listen(endpoint);

        Should.Throw<IOException>(() => DiagnosticsTransport.Listen(endpoint));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Connect_TimesOutWithoutAListener()
    {
        await Should.ThrowAsync<TimeoutException>(
            () => DiagnosticsTransport.ConnectAsync(Endpoint(), TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task UnixSocket_IsUserOnlyAndRemovedOnDispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix domain socket permissions only.");
            return;
        }

        var endpoint = Endpoint();
        await using (var listener = DiagnosticsTransport.Listen(endpoint))
        {
            File.GetUnixFileMode(endpoint).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Exists(endpoint).ShouldBeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task UnixSocket_ReplacesAStaleSocketFile()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix domain sockets only.");
            return;
        }

        var endpoint = Endpoint();
        await File.WriteAllTextAsync(endpoint, "stale", TestContext.Current.CancellationToken);

        await using var listener = DiagnosticsTransport.Listen(endpoint);
        await using var client = await DiagnosticsTransport.ConnectAsync(endpoint, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private string Endpoint()
    {
        DiagnosticsPaths.EnsureUserOnlyDirectory(_stateDirectory);
        return DiagnosticsPaths.HostEndpoint(_stateDirectory);
    }
}
