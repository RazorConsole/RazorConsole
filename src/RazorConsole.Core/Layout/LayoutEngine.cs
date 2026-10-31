// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

public sealed class LayoutEngine
{
    public LayoutResult Layout(Widget root, BoxConstraints constraints, int renderVersion = 0)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var context = new LayoutContext(renderVersion)
        {
            DocumentSize = new LayoutSize(constraints.MaxWidth, constraints.MaxHeight),
        };
        var desiredSize = root.Measure(context, constraints);
        var finalSize = constraints.Constrain(desiredSize);
        var rootBounds = new LayoutRect(0, 0, finalSize.Width, finalSize.Height);

        // bottom/right of absolute elements are relative to the document, which is as tall as the flow content.
        context.DocumentSize = new LayoutSize(constraints.MaxWidth, finalSize.Height);
        root.Arrange(context, rootBounds);

        finalSize = ExpandToAbsoluteChildren(root, finalSize, constraints.MaxWidth);

        return new LayoutResult(root, root.CreateLayoutBox(), finalSize, renderVersion);
    }

    private static LayoutSize ExpandToAbsoluteChildren(Widget root, LayoutSize size, int maxWidth)
    {
        var width = size.Width;
        var height = size.Height;
        var pending = new Stack<Widget>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var widget = pending.Pop();
            if (widget.IsAbsolutePositioned)
            {
                width = Math.Max(width, Math.Min(widget.Bounds.Right, maxWidth));
                height = Math.Max(height, widget.Bounds.Bottom);
            }

            foreach (var child in widget.Children)
            {
                pending.Push(child);
            }
        }

        return new LayoutSize(width, height);
    }
}

public sealed record LayoutResult(Widget Root, LayoutBox RootBox, LayoutSize Size, int RenderVersion)
{
    public IReadOnlyList<LayoutBox> EnumerateLayoutBoxes()
    {
        var result = new List<LayoutBox>();
        AppendLayoutBoxes(RootBox, result);
        return result;
    }

    public IReadOnlyList<Vdom.VNodeLayoutInfo> EnumerateLayoutInfos()
        => EnumerateLayoutBoxes()
            .Select(box => box.ToLayoutInfo(isVisible: !box.Bounds.IsEmpty, RenderVersion))
            .ToArray();

    public IReadOnlyDictionary<string, string?> EnumerateLayoutParentIds()
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        AppendLayoutParentIds(RootBox, parentId: null, result);
        return result;
    }

    public IRenderable PaintToRenderable()
        => new WidgetCanvasRenderable(Root, Size);

    private static void AppendLayoutBoxes(LayoutBox box, List<LayoutBox> result)
    {
        result.Add(box);
        foreach (var child in box.Children)
        {
            AppendLayoutBoxes(child, result);
        }
    }

    private static void AppendLayoutParentIds(LayoutBox box, string? parentId, Dictionary<string, string?> result)
    {
        result[box.VNodeId] = parentId;
        foreach (var child in box.Children)
        {
            AppendLayoutParentIds(child, box.VNodeId, result);
        }
    }
}
