// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

/// <summary>
/// Pure scrollbar math shared by every widget that draws a vertical scrollbar.
/// </summary>
public static class ScrollbarGeometry
{
    /// <summary>
    /// Computes the thumb rectangle inside a vertical track.
    /// </summary>
    /// <param name="trackLength">Number of rows in the track.</param>
    /// <param name="viewportLength">Number of visible content rows.</param>
    /// <param name="contentLength">Total number of content rows.</param>
    /// <param name="offset">Rows scrolled past the top. Values outside <c>[0, contentLength - viewportLength]</c> are clamped.</param>
    /// <param name="minThumbLength">Smallest thumb length, capped at the track length.</param>
    /// <returns>The thumb start row and length within the track; <c>(0, 0)</c> when there is nothing to draw.</returns>
    public static (int Top, int Length) CalculateThumb(
        int trackLength,
        int viewportLength,
        int contentLength,
        int offset,
        int minThumbLength = 1)
    {
        if (trackLength <= 0 || contentLength <= 0 || viewportLength <= 0)
        {
            return (0, 0);
        }

        var length = Math.Clamp(
            (int)Math.Ceiling(trackLength * (Math.Min(viewportLength, contentLength) / (double)contentLength)),
            Math.Min(Math.Max(1, minThumbLength), trackLength),
            trackLength);
        var maxOffset = Math.Max(0, contentLength - viewportLength);
        var maxTop = Math.Max(0, trackLength - length);
        if (maxOffset == 0)
        {
            return (0, length);
        }

        var clamped = Math.Clamp(offset, 0, maxOffset);
        var top = (int)Math.Round(maxTop * (clamped / (double)maxOffset));
        return (Math.Clamp(top, 0, maxTop), length);
    }

    /// <summary>
    /// Maps a row inside the track back to the content offset it represents (used for track clicks and thumb drags).
    /// </summary>
    public static int OffsetForThumbTop(int thumbTop, int trackLength, int thumbLength, int viewportLength, int contentLength)
    {
        var maxOffset = Math.Max(0, contentLength - viewportLength);
        var maxTop = Math.Max(0, trackLength - thumbLength);
        if (maxOffset == 0 || maxTop == 0)
        {
            return 0;
        }

        var clamped = Math.Clamp(thumbTop, 0, maxTop);
        return (int)Math.Round(maxOffset * (clamped / (double)maxTop));
    }
}
