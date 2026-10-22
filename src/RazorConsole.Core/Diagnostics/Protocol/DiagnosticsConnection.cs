// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Channels;

namespace RazorConsole.Core.Diagnostics.Protocol;

/// <summary>
/// Reads and writes <see cref="DiagnosticsMessage"/> values as newline-delimited JSON over a stream.
/// </summary>
/// <remarks>
/// The connection is full duplex: a background pump reads the stream for as long as it is open and queues messages
/// for <see cref="ReadAsync"/>, whether or not anyone is reading them. A write blocked on a full pipe or socket
/// buffer therefore never stops this side from draining its peer, so correctness never depends on buffer sizes.
/// The queue is bounded by message count and bytes, because the endpoint runs inside the user's app and never trusts
/// its peer. Overflow does not pause reading, which would bring the deadlock back: the connection sends the peer a
/// <see cref="DiagnosticsErrorCodes.QueueOverflow"/> error on a best-effort basis, closes the stream, and
/// <see cref="ReadAsync"/> throws that error once the queued messages are read. Writes from several threads are serialized; <see cref="ReadAsync"/> has one caller at a time.
/// </remarks>
internal sealed class DiagnosticsConnection : IAsyncDisposable
{
    private const int InitialBufferSize = 4096;
    private const byte NewLine = (byte)'\n';
    private static readonly byte[] NewLineBytes = [NewLine];
    private static readonly TimeSpan OverflowNoticeTimeout = TimeSpan.FromSeconds(1);

    private readonly Stream _stream;
    private readonly int _maxMessageBytes;
    private readonly int _maxQueuedMessages;
    private readonly long _maxQueuedBytes;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _disposed = new();
    private readonly Channel<QueuedMessage> _incoming = Channel.CreateUnbounded<QueuedMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private readonly Task _pump;
    private Exception? _readError;
    private int _queuedMessages;
    private long _queuedBytes;
    private int _lastLineBytes;
    private byte[] _buffer = new byte[InitialBufferSize];
    private int _start;
    private int _end;
    private bool _endOfStream;

    public DiagnosticsConnection(
        Stream stream,
        int maxMessageBytes = DiagnosticsProtocol.MaxMessageBytes,
        int maxQueuedMessages = DiagnosticsProtocol.MaxQueuedMessages,
        long maxQueuedBytes = DiagnosticsProtocol.MaxQueuedBytes)
    {
        _stream = stream;
        _maxMessageBytes = maxMessageBytes;
        _maxQueuedMessages = maxQueuedMessages;
        _maxQueuedBytes = maxQueuedBytes;

        if (stream.CanRead)
        {
            _pump = Task.Run(() => PumpAsync(_disposed.Token));
        }
        else
        {
            _incoming.Writer.Complete();
            _pump = Task.CompletedTask;
        }
    }

    /// <summary>
    /// Completes when this side stops reading: end of stream, a read or protocol error, queue overflow, or disposal.
    /// </summary>
    public Task Completion => _pump;

    /// <summary>
    /// Returns the next message, or <see langword="null"/> when the peer closed the stream.
    /// A final line without a trailing newline still counts. Blank lines are skipped.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException">
    /// A line is not valid JSON or is larger than the limit. Messages received before it are returned first,
    /// and the error ends the connection.
    /// </exception>
    public async ValueTask<DiagnosticsMessage?> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (await _incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_incoming.Reader.TryRead(out var queued))
            {
                Interlocked.Decrement(ref _queuedMessages);
                Interlocked.Add(ref _queuedBytes, -queued.Bytes);
                return queued.Message;
            }
        }

        if (_readError is not null)
        {
            ExceptionDispatchInfo.Throw(_readError);
        }

        return null;
    }

    /// <summary>
    /// Writes one message followed by a newline and flushes it.
    /// </summary>
    public async ValueTask WriteAsync(DiagnosticsMessage message, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, DiagnosticsJsonContext.Default.DiagnosticsMessage);
        if (payload.Length > _maxMessageBytes)
        {
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.MessageTooLarge,
                $"A diagnostics message of {payload.Length} bytes exceeded {_maxMessageBytes} bytes.");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(NewLineBytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposed.CancelAsync().ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _disposed.Dispose();
        _writeLock.Dispose();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await ReadMessageAsync(cancellationToken).ConfigureAwait(false) is { } message)
            {
                var bytes = _lastLineBytes;
                if (Interlocked.Increment(ref _queuedMessages) > _maxQueuedMessages
                    || Interlocked.Add(ref _queuedBytes, bytes) > _maxQueuedBytes)
                {
                    var overflow = new DiagnosticsProtocolException(
                        DiagnosticsErrorCodes.QueueOverflow,
                        $"The peer sent more than {_maxQueuedMessages} messages or {_maxQueuedBytes} bytes that were not read yet.");
                    _readError = overflow;
                    await CloseAfterOverflowAsync(overflow, cancellationToken).ConfigureAwait(false);
                    return;
                }

                _incoming.Writer.TryWrite(new QueuedMessage(message, bytes));
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _readError = ex;
        }
        catch
        {
            // Disposed while reading: the stream is gone, which readers see as its end.
        }
        finally
        {
            _incoming.Writer.TryComplete();
        }
    }

    // Tells the peer why, without waiting long for a peer that does not read, then stops all traffic.
    private async Task CloseAfterOverflowAsync(DiagnosticsProtocolException overflow, CancellationToken cancellationToken)
    {
        if (_stream.CanWrite)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(OverflowNoticeTimeout);
            try
            {
                await WriteAsync(DiagnosticsMessage.Failure(null, overflow.ToError()), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Best effort: the peer may be gone or not reading.
            }
        }

        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    // Reads the next line from the stream. Only the pump calls this, so the buffer has a single owner.
    private async ValueTask<DiagnosticsMessage?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var newLine = Array.IndexOf(_buffer, NewLine, _start, _end - _start);
            if (newLine >= 0)
            {
                var message = ParseLine(newLine - _start);
                _start = newLine + 1;
                if (message is not null)
                {
                    return message;
                }

                continue;
            }

            if (_end - _start > _maxMessageBytes)
            {
                throw new DiagnosticsProtocolException(
                    DiagnosticsErrorCodes.MessageTooLarge,
                    $"A diagnostics message exceeded {_maxMessageBytes} bytes.");
            }

            if (_endOfStream)
            {
                if (_end == _start)
                {
                    return null;
                }

                var last = ParseLine(_end - _start);
                _start = _end;
                if (last is not null)
                {
                    return last;
                }

                continue;
            }

            MakeRoom();
            var read = await _stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                _endOfStream = true;
            }
            else
            {
                _end += read;
            }
        }
    }

    private DiagnosticsMessage? ParseLine(int length)
    {
        var line = _buffer.AsSpan(_start, length);
        if (line.Length > 0 && line[^1] == (byte)'\r')
        {
            line = line[..^1];
        }

        if (line.Length > _maxMessageBytes)
        {
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.MessageTooLarge,
                $"A diagnostics message exceeded {_maxMessageBytes} bytes.");
        }

        if (line.Trim(" \t"u8).IsEmpty)
        {
            return null;
        }

        _lastLineBytes = line.Length;
        try
        {
            return JsonSerializer.Deserialize(line, DiagnosticsJsonContext.Default.DiagnosticsMessage)
                ?? throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidRequest, "A diagnostics message was null.");
        }
        catch (JsonException ex)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidRequest, $"Invalid diagnostics message: {ex.Message}", ex);
        }
    }

    private readonly record struct QueuedMessage(DiagnosticsMessage Message, int Bytes);

    // Moves unread bytes to the front, then grows the buffer when it is still full.
    private void MakeRoom()
    {
        if (_start > 0)
        {
            var pending = _end - _start;
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, pending);
            _start = 0;
            _end = pending;
        }

        // ReadMessageAsync throws before the buffer must exceed one maximal line plus "\r\n".
        if (_end == _buffer.Length)
        {
            var next = new byte[(int)Math.Min((long)_buffer.Length * 2, (long)_maxMessageBytes + 2)];
            Buffer.BlockCopy(_buffer, 0, next, 0, _end);
            _buffer = next;
        }
    }
}
