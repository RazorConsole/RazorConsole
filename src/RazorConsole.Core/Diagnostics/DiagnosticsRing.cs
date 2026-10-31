// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Keeps the most recent items up to a fixed capacity, dropping the oldest. Thread safe.
/// </summary>
/// <remarks>
/// Every item gets a sequence number that starts at 1 and never repeats, so a reader can ask only for what is new
/// and can tell from a gap that items were dropped.
/// </remarks>
internal sealed class DiagnosticsRing<T>
{
    private readonly Lock _sync = new();
    private readonly DiagnosticsRingEntry<T>[] _items;
    private long _lastSequence;

    public DiagnosticsRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new DiagnosticsRingEntry<T>[capacity];
    }

    public int Capacity => _items.Length;

    /// <summary>
    /// Gets the sequence number of the newest item, or 0 when empty.
    /// </summary>
    public long LastSequence
    {
        get
        {
            lock (_sync)
            {
                return _lastSequence;
            }
        }
    }

    public long Add(T item)
    {
        lock (_sync)
        {
            var sequence = ++_lastSequence;
            _items[(sequence - 1) % _items.Length] = new DiagnosticsRingEntry<T>(sequence, item);
            return sequence;
        }
    }

    /// <summary>
    /// Returns the kept items with a sequence number above <paramref name="afterSequence"/>, oldest first.
    /// </summary>
    public DiagnosticsRingEntry<T>[] Read(long afterSequence = 0)
    {
        lock (_sync)
        {
            var first = Math.Max(afterSequence, _lastSequence - _items.Length) + 1;
            if (first > _lastSequence)
            {
                return [];
            }

            var result = new DiagnosticsRingEntry<T>[_lastSequence - first + 1];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = _items[(first + i - 1) % _items.Length];
            }

            return result;
        }
    }
}

internal readonly record struct DiagnosticsRingEntry<T>(long Sequence, T Value);
