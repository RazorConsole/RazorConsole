// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.ObjectModel;
using System.Text;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Testing;

/// <summary>
/// An immutable capture of one frame rendered by a <see cref="HeadlessTerminal"/>: the cell grid, focus, and layouts.
/// </summary>
public sealed class HeadlessTerminalFrame : TerminalFrame
{
    internal HeadlessTerminalFrame(
        long frameNumber,
        DateTimeOffset observedAt,
        TerminalCanvas canvas,
        int terminalWidth,
        int terminalHeight,
        string? focusKey,
        IReadOnlyDictionary<string, VNodeLayoutInfo> layouts)
        : base(frameNumber, observedAt, CopyCells(canvas, terminalWidth, terminalHeight))
    {
        FocusKey = focusKey;
        Layouts = new ReadOnlyDictionary<string, VNodeLayoutInfo>(
            new Dictionary<string, VNodeLayoutInfo>(layouts, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets the focus key of the focused element when the frame was captured, if any.
    /// </summary>
    public string? FocusKey { get; }

    /// <summary>
    /// Gets the layouts of the elements that have a <c>data-vnode-hook</c> attribute, keyed by hook.
    /// </summary>
    public IReadOnlyDictionary<string, VNodeLayoutInfo> Layouts { get; }

    /// <summary>
    /// Gets the layout captured for the element with the given <c>data-vnode-hook</c>.
    /// </summary>
    /// <param name="hookKey">The hook key.</param>
    /// <exception cref="KeyNotFoundException">No layout was captured for <paramref name="hookKey"/>.</exception>
    public VNodeLayoutInfo GetLayout(string hookKey)
        => Layouts.TryGetValue(hookKey, out var layout)
            ? layout
            : throw new KeyNotFoundException($"No layout was captured for hook '{hookKey}'.");

    /// <summary>
    /// Tries to get the layout captured for the element with the given <c>data-vnode-hook</c>.
    /// </summary>
    /// <param name="hookKey">The hook key.</param>
    /// <param name="layout">The layout, when found.</param>
    public bool TryGetLayout(string hookKey, out VNodeLayoutInfo layout)
        => Layouts.TryGetValue(hookKey, out layout);

    private protected override void AppendHeaderDetails(StringBuilder builder)
        => builder.Append(", focus: ").Append(FocusKey ?? "<none>");

    private protected override void AppendFooterDetails(StringBuilder builder)
    {
        if (Layouts.Count == 0)
        {
            return;
        }

        builder.AppendLine("Layouts:");
        foreach (var pair in Layouts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var layout = pair.Value;
            builder.Append("  ").Append(pair.Key).Append(": ")
                .Append("left=").Append(layout.Left)
                .Append(", top=").Append(layout.Top)
                .Append(", width=").Append(layout.Width)
                .Append(", height=").Append(layout.Height)
                .Append(", z=").Append(layout.ZIndex)
                .AppendLine();
        }
    }

    private static TerminalCell[,] CopyCells(TerminalCanvas canvas, int width, int height)
    {
        var cells = new TerminalCell[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cells[y, x] = x < canvas.Width && y < canvas.Height
                    ? canvas[x, y]
                    : TerminalCell.Empty;
            }
        }

        return cells;
    }
}
