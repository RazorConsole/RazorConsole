// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Concurrent;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// Tracks the largest scroll offset of <c>Scrollable</c> components.
/// </summary>
[Obsolete("Only the obsolete Scrollable component uses this. Set Overflow=Scroll on a container and use a ScrollController to scroll from code.")]
public class ScrollableLayoutCoordinator
{
    private readonly ConcurrentDictionary<string, int> _maxOffsets = new();

    public void ReportMaxOffset(string scrollId, int maxOffset)
    {
        if (!string.IsNullOrEmpty(scrollId))
        {
            _maxOffsets[scrollId] = maxOffset;
        }
    }

    public int GetMaxOffset(string? scrollId) => _maxOffsets.GetValueOrDefault(scrollId ?? string.Empty, int.MaxValue);

    public void Remove(string scrollId)
    {
        if (!string.IsNullOrEmpty(scrollId))
        {
            _maxOffsets.TryRemove(scrollId, out _);
        }
    }
}
