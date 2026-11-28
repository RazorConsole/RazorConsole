// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// What a render snapshot hands to the frame loop: a laid-out view that can be painted into cells any number of times.
/// </summary>
/// <remarks>
/// The frame loop paints the current source for every frame it writes, so a repaint without a new render (animations,
/// scrolling) paints the same source again and picks up state that changed since, such as a spinner's frame.
/// </remarks>
internal abstract class FrameSource
{
    /// <summary>
    /// The sequence of the render snapshot this source was laid out for, or 0 when it does not come from a render (as
    /// in tests). A higher value is a later render.
    /// </summary>
    public long RenderSequence { get; init; }

    /// <summary>The tree this source was laid out from, or <see langword="null"/> when it does not come from a render.</summary>
    public VNode? Root { get; init; }

    /// <summary>The layout of every node of <see cref="Root"/>, or <see langword="null"/> when it does not come from a render.</summary>
    public IReadOnlyCollection<VNodeLayoutInfo>? Layouts { get; init; }

    /// <summary>Paints the view into a new canvas.</summary>
    public abstract TerminalCanvas Paint();
}

/// <summary>
/// A laid-out widget tree, painted at the size it was laid out for.
/// </summary>
internal sealed class WidgetFrameSource(Widget root, LayoutSize size) : FrameSource
{
    private readonly Widget _root = root ?? throw new ArgumentNullException(nameof(root));

    public LayoutSize Size { get; } = size;

    public override TerminalCanvas Paint()
    {
        var canvas = new TerminalCanvas(Size.Width, Size.Height);
        var context = new PaintContext(canvas) { DeferAbsolute = true };
        _root.Paint(context);
        context.PaintOverlays();
        return canvas;
    }
}
