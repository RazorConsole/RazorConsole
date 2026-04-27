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

        var context = new LayoutContext(renderVersion);
        var desiredSize = root.Measure(context, constraints);
        var finalSize = constraints.Constrain(desiredSize);
        var rootBounds = new LayoutRect(0, 0, finalSize.Width, finalSize.Height);
        root.Arrange(context, rootBounds);

        return new LayoutResult(root, root.CreateLayoutBox(), finalSize, renderVersion);
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
