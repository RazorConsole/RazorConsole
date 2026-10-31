// Copyright (c) RazorConsole. All rights reserved.

using System.Buffers;
using System.Text;

namespace RazorConsole.Cli.Host;

/// <summary>
/// The last <see cref="Capacity"/> bytes of a session's raw output as UTF-8, addressed by absolute byte offsets that
/// count from the session's start, so a client can poll with <c>since</c> and notice what it missed.
/// </summary>
/// <remarks>
/// <see cref="Append"/> is the terminal's raw output observer. It runs under the terminal's lock, so it only encodes
/// and copies under its own short lock, and never calls back into the terminal.
/// </remarks>
internal sealed class OutputRing
{
    public const int DefaultCapacity = 4 * 1024 * 1024;

    /// <summary>
    /// The most one read returns, so a result stays well below the protocol's message limit after JSON escaping.
    /// </summary>
    public const int MaxReadBytes = 1024 * 1024;

    private readonly object _gate = new();
    private readonly byte[] _buffer;
    private readonly Encoder _encoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetEncoder();
    private long _total;

    public OutputRing(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 4);
        _buffer = new byte[capacity];
    }

    public int Capacity => _buffer.Length;

    /// <summary>
    /// The offset after the last byte written.
    /// </summary>
    public long End
    {
        get
        {
            lock (_gate)
            {
                return _total;
            }
        }
    }

    public void Append(ReadOnlySpan<char> output)
    {
        if (output.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            // The encoder keeps a high surrogate that ends one chunk until the next chunk brings its pair.
            var maxBytes = Encoding.UTF8.GetMaxByteCount(output.Length);
            var rented = maxBytes > 1024 ? ArrayPool<byte>.Shared.Rent(maxBytes) : null;
            Span<byte> bytes = rented is null ? stackalloc byte[maxBytes] : rented;
            try
            {
                var written = _encoder.GetBytes(output, bytes, flush: false);
                Write(bytes[..written]);
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }
    }

    /// <summary>
    /// Reads from <paramref name="since"/> (or the oldest byte kept) up to <paramref name="maxBytes"/> bytes, on
    /// character boundaries.
    /// </summary>
    /// <param name="since">The offset to start at; earlier offsets that were dropped start at the oldest byte kept.</param>
    /// <param name="maxBytes">The most bytes to return, at most <see cref="MaxReadBytes"/>.</param>
    /// <returns>Where the data starts, where the next read continues, and the data.</returns>
    public (long Offset, long Next, string Data) Read(long? since, int maxBytes = MaxReadBytes)
    {
        maxBytes = Math.Clamp(maxBytes, 4, MaxReadBytes);
        lock (_gate)
        {
            var oldest = Math.Max(0, _total - _buffer.Length);
            var start = Math.Clamp(since ?? oldest, oldest, _total);
            var end = Math.Min(start + maxBytes, _total);

            // A dropped prefix or a since inside a character cuts it; skip its continuation bytes.
            while (start < end && IsContinuation(At(start)))
            {
                start++;
            }

            end = TrimPartialCharacter(start, end);
            var length = (int)(end - start);
            Span<byte> bytes = length <= 1024 ? stackalloc byte[length] : new byte[length];
            Copy(start, bytes);
            return (start, end, Encoding.UTF8.GetString(bytes));
        }
    }

    private static bool IsContinuation(byte value) => (value & 0xC0) == 0x80;

    private static int SequenceLength(byte lead) => lead switch
    {
        < 0x80 => 1,
        >= 0xF0 => 4,
        >= 0xE0 => 3,
        >= 0xC0 => 2,
        _ => 1,
    };

    // Moves end back to before a character that is not complete yet, unless that would return nothing at all.
    private long TrimPartialCharacter(long start, long end)
    {
        for (var position = end - 1; position >= start && position >= end - 3; position--)
        {
            var value = At(position);
            if (IsContinuation(value))
            {
                continue;
            }

            return position + SequenceLength(value) > end ? position : end;
        }

        return end;
    }

    private byte At(long offset) => _buffer[(int)(offset % _buffer.Length)];

    private void Copy(long start, Span<byte> destination)
    {
        var index = (int)(start % _buffer.Length);
        var first = Math.Min(destination.Length, _buffer.Length - index);
        _buffer.AsSpan(index, first).CopyTo(destination);
        _buffer.AsSpan(0, destination.Length - first).CopyTo(destination[first..]);
    }

    private void Write(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= _buffer.Length)
        {
            // Only the tail survives; keep the offsets right.
            _total += bytes.Length - _buffer.Length;
            bytes = bytes[^_buffer.Length..];
        }

        var index = (int)(_total % _buffer.Length);
        var first = Math.Min(bytes.Length, _buffer.Length - index);
        bytes[..first].CopyTo(_buffer.AsSpan(index));
        bytes[first..].CopyTo(_buffer);
        _total += bytes.Length;
    }
}
