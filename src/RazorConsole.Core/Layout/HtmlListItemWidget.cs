// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

/// <summary>
/// Lays out a single HTML list item (an <c>&lt;li&gt;</c>) as a marker (bullet or ordinal
/// prefix) followed by its fully-translated content. Unlike flattening the item to plain
/// text, this allows list items to contain arbitrary nested/styled widgets (e.g. a styled
/// <c>Markup</c> span, nested lists, multi-line text) without losing styling or content.
/// </summary>
public sealed class HtmlListItemWidget : Widget
{
    public HtmlListItemWidget(
        string vnodeId,
        Widget marker,
        Widget content,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, new[] { marker, content }, zIndex)
    {
        Marker = marker ?? throw new ArgumentNullException(nameof(marker));
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public Widget Marker { get; }

    public Widget Content { get; }

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        if (constraints.MaxWidth == 0 || constraints.MaxHeight == 0)
        {
            return constraints.Constrain(LayoutSize.Empty);
        }

        var markerConstraints = new BoxConstraints(0, constraints.MaxWidth, 0, constraints.MaxHeight);
        var markerSize = Marker.Measure(context, markerConstraints);

        // Constrain the content to the width remaining after the marker so that wrapping
        // (and therefore the measured line/height count) reflects where the content will
        // actually be painted. This keeps Scrollable's virtual-window slicing accurate for
        // multi-line and styled list items.
        var contentMaxWidth = Math.Max(0, constraints.MaxWidth - markerSize.Width);
        var contentConstraints = new BoxConstraints(0, contentMaxWidth, 0, constraints.MaxHeight);
        var contentSize = Content.Measure(context, contentConstraints);

        var width = markerSize.Width + contentSize.Width;
        var height = Math.Max(markerSize.Height, contentSize.Height);
        return constraints.Constrain(new LayoutSize(width, height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var markerWidth = Math.Min(Marker.DesiredSize.Width, bounds.Width);
        var markerHeight = Math.Min(Marker.DesiredSize.Height, bounds.Height);
        Marker.Arrange(context, new LayoutRect(bounds.X, bounds.Y, markerWidth, markerHeight));

        var contentX = bounds.X + markerWidth;
        var contentWidth = Math.Max(0, bounds.Width - markerWidth);
        Content.Arrange(context, new LayoutRect(contentX, bounds.Y, contentWidth, bounds.Height));
    }

    protected override void PaintCore(PaintContext context)
    {
        Marker.Paint(context);
        Content.Paint(context);
    }
}
