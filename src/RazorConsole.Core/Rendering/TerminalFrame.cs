// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// One painted frame: the full grid of terminal cells the frame loop presents.
/// </summary>
/// <remarks>
/// The frame loop paints a new frame for every frame it writes, so a frame is never changed after it was painted.
/// </remarks>
internal sealed class TerminalFrame
{
    public TerminalFrame(long sequence, FrameSource source, TerminalCanvas canvas)
    {
        Sequence = sequence;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    /// <summary>
    /// The frame request this frame serves. It grows with every frame, and skips the requests that were merged into
    /// a later frame.
    /// </summary>
    public long Sequence { get; }

    public int Width => Canvas.Width;

    public int Height => Canvas.Height;

    public TerminalCell this[int x, int y] => Canvas[x, y];

    /// <summary>
    /// The source this frame was painted from, with the render it belongs to. Repaints of the same source (animations,
    /// scrolling) share it.
    /// </summary>
    internal FrameSource Source { get; }

    /// <summary>The painted cells.</summary>
    internal TerminalCanvas Canvas { get; }
}
