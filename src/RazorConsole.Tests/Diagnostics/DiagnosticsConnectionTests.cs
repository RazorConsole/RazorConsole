// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using System.Text.Json;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Diagnostics;

public sealed class DiagnosticsConnectionTests
{
    private static readonly DiagnosticsJsonContext Json = DiagnosticsJsonContext.Default;

    [Fact]
    public async Task WriteThenRead_RoundTripsEveryMessageKind()
    {
        var stream = new MemoryStream();
        await using (var writer = new DiagnosticsConnection(new WriteOnlyStream(stream)))
        {
            await writer.WriteAsync(DiagnosticsMessage.Request(1, DiagnosticsProtocol.HelloMethod, new HelloParams(1, "test"), Json.HelloParams), TestContext.Current.CancellationToken);
            await writer.WriteAsync(DiagnosticsMessage.Success(1, new HelloResult(1, "server", 42, ["tree"]), Json.HelloResult), TestContext.Current.CancellationToken);
            await writer.WriteAsync(DiagnosticsMessage.Notification(DiagnosticsProtocol.CancelMethod, new CancelParams(7), Json.CancelParams), TestContext.Current.CancellationToken);
            await writer.WriteAsync(DiagnosticsMessage.Failure(2, new DiagnosticsError(DiagnosticsErrorCodes.ElementNotFound, "no match")), TestContext.Current.CancellationToken);
        }

        stream.Position = 0;
        await using var reader = new DiagnosticsConnection(stream);

        var request = await reader.ReadAsync(TestContext.Current.CancellationToken);
        request.ShouldNotBeNull();
        request.IsRequest.ShouldBeTrue();
        request.Method.ShouldBe("hello");
        request.GetParams(Json.HelloParams).ShouldBe(new HelloParams(1, "test"));

        var response = await reader.ReadAsync(TestContext.Current.CancellationToken);
        response.ShouldNotBeNull();
        response.IsResponse.ShouldBeTrue();
        var hello = response.GetResult(Json.HelloResult);
        hello.ShouldNotBeNull();
        hello.ProcessId.ShouldBe(42);
        hello.Features.ShouldBe(["tree"]);

        var notification = await reader.ReadAsync(TestContext.Current.CancellationToken);
        notification.ShouldNotBeNull();
        notification.IsNotification.ShouldBeTrue();
        notification.GetParams(Json.CancelParams).ShouldBe(new CancelParams(7));

        var failure = await reader.ReadAsync(TestContext.Current.CancellationToken);
        failure.ShouldNotBeNull();
        failure.IsResponse.ShouldBeTrue();
        failure.Error.ShouldNotBeNull();
        failure.Error.Code.ShouldBe("element_not_found");

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public void Serialization_IsCamelCaseAndOmitsNulls()
    {
        var message = DiagnosticsMessage.Request(3, "hello", new HelloParams(1, "c"), Json.HelloParams);

        JsonSerializer.Serialize(message, Json.DiagnosticsMessage)
            .ShouldBe("""{"id":3,"method":"hello","params":{"protocol":1,"client":"c"}}""");
    }

    [Fact]
    public async Task Read_HandlesSplitReadsCrLfBlankLinesAndAFinalLineWithoutNewline()
    {
        var text = "\n{\"id\":1,\"method\":\"a\"}\r\n   \n{\"method\":\"b\"}";
        await using var reader = new DiagnosticsConnection(new OneByteStream(Encoding.UTF8.GetBytes(text)));

        (await reader.ReadAsync(TestContext.Current.CancellationToken))!.Method.ShouldBe("a");
        (await reader.ReadAsync(TestContext.Current.CancellationToken))!.Method.ShouldBe("b");
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Read_GrowsTheBufferForLongLines()
    {
        var payload = new string('x', 100_000);
        var text = $"{{\"method\":\"m\",\"params\":\"{payload}\"}}\n";
        await using var reader = new DiagnosticsConnection(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        var message = await reader.ReadAsync(TestContext.Current.CancellationToken);

        message!.Params!.Value.GetString().ShouldBe(payload);
    }

    [Fact]
    public async Task Read_RejectsInvalidJson()
    {
        await using var reader = new DiagnosticsConnection(new MemoryStream("{not json}\n"u8.ToArray()));

        var ex = await Should.ThrowAsync<DiagnosticsProtocolException>(() => reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());

        ex.Code.ShouldBe(DiagnosticsErrorCodes.InvalidRequest);
    }

    [Theory]
    [InlineData("{\"method\":\"0123456789012345678901234567890123456789\"}\n")]
    [InlineData("{\"method\":\"0123456789012345678901234567890123456789\"}")]
    [InlineData("{\"method\":\"0123456789012345678901234567890123456789")]
    public async Task Read_RejectsMessagesOverTheLimit(string text)
    {
        await using var reader = new DiagnosticsConnection(new MemoryStream(Encoding.UTF8.GetBytes(text)), maxMessageBytes: 32);

        var ex = await Should.ThrowAsync<DiagnosticsProtocolException>(() => reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());

        ex.Code.ShouldBe(DiagnosticsErrorCodes.MessageTooLarge);
    }

    [Theory]
    [InlineData(3, 1_000_000, 3)] // count limit
    [InlineData(1_000, 250, 2)] // byte limit: each line is 100 bytes
    public async Task UnreadMessagesOverTheQueueLimit_EndTheConnectionWithQueueOverflow(int maxMessages, long maxBytes, int expectedQueued)
    {
        var line = """{"method":"m","params":""}""";
        line = line.Insert(line.Length - 2, new string('p', 100 - line.Length));
        var text = string.Concat(Enumerable.Repeat(line + "\n", 10));
        await using var reader = new DiagnosticsConnection(
            new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false),
            maxQueuedMessages: maxMessages,
            maxQueuedBytes: maxBytes);

        // Nobody reads until the pump has stopped on its own.
        await reader.Completion.WaitAsync(TestContext.Current.CancellationToken);

        for (var i = 0; i < expectedQueued; i++)
        {
            (await reader.ReadAsync(TestContext.Current.CancellationToken))!.Method.ShouldBe("m");
        }

        var ex = await Should.ThrowAsync<DiagnosticsProtocolException>(() => reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        ex.Code.ShouldBe(DiagnosticsErrorCodes.QueueOverflow);
    }

    [Fact]
    public async Task Write_RejectsMessagesOverTheLimit()
    {
        var stream = new MemoryStream();
        await using var writer = new DiagnosticsConnection(new WriteOnlyStream(stream), maxMessageBytes: 16);

        var ex = await Should.ThrowAsync<DiagnosticsProtocolException>(
            () => writer.WriteAsync(DiagnosticsMessage.Failure(1, new DiagnosticsError("internal", "a long message"))).AsTask());

        ex.Code.ShouldBe(DiagnosticsErrorCodes.MessageTooLarge);
        stream.Length.ShouldBe(0);
    }

    [Fact]
    public async Task ConcurrentWrites_DoNotInterleave()
    {
        var stream = new MemoryStream();
        await using (var writer = new DiagnosticsConnection(new WriteOnlyStream(stream)))
        {
            await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(
                () => writer.WriteAsync(DiagnosticsMessage.Failure(i, new DiagnosticsError("internal", new string('y', 500)))).AsTask(),
                TestContext.Current.CancellationToken)));
        }

        stream.Position = 0;
        await using var reader = new DiagnosticsConnection(stream);
        var ids = new List<long>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken) is { } message)
        {
            ids.Add(message.Id!.Value);
        }

        ids.Order().ShouldBe(Enumerable.Range(0, 50).Select(i => (long)i));
    }

    [Fact]
    public void GetParams_WithTheWrongShape_ThrowsInvalidParams()
    {
        // Missing and unknown members are tolerated so the protocol stays additive; a wrong type is not.
        using var document = JsonDocument.Parse("""{"id":"seven"}""");
        var message = new DiagnosticsMessage { Method = "cancel", Params = document.RootElement.Clone() };

        var ex = Should.Throw<DiagnosticsProtocolException>(() => message.GetParams(Json.CancelParams));

        ex.Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
    }

    private sealed class OneByteStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    // Write side of a test: not readable, so the connection starts no read pump on the shared stream,
    // and not closing, so the bytes can be read back afterwards.
    private sealed class WriteOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
