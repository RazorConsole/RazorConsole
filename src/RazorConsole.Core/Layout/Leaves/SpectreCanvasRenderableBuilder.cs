// Copyright (c) RazorConsole. All rights reserved.


using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout.Leaves;

internal static class SpectreCanvasRenderableBuilder
{
    public static IRenderable? TryCreate(VNode node)
    {
        if (!CanHandle(node))
        {
            return null;
        }

        // A canvas needs a positive size; without one the node falls back to the default widget.
        if (!VNodeAttributeReader.TryParsePositiveInt(VNodeAttributeReader.GetAttribute(node, "data-width"), out var width)
            || !VNodeAttributeReader.TryParsePositiveInt(VNodeAttributeReader.GetAttribute(node, "data-height"), out var height))
        {
            return null;
        }

        var pixelsDataIdAttribute = VNodeAttributeReader.GetAttribute(node, "data-canvas-data-id");

        var canvas = new Canvas(width, height)
        {
            Scale = VNodeAttributeReader.TryGetBoolAttribute(node, "data-scale", out var scale) && scale,
        };

        if (VNodeAttributeReader.TryParsePositiveInt(VNodeAttributeReader.GetAttribute(node, "data-maxwidth"), out var maxWidth))
        {
            canvas.MaxWidth = maxWidth;
        }

        if (!string.IsNullOrWhiteSpace(pixelsDataIdAttribute) &&
            Guid.TryParse(pixelsDataIdAttribute, out var dataId))
        {
            var dataInDictionary = CanvasDataRegistry.TryGetData(dataId, out var pixels);

            if (!dataInDictionary)
            {
                return null;
            }

            foreach (var p in pixels!)
            {
                canvas.SetPixel(p.Item1, p.Item2, p.Item3);
            }
        }
        else
        {
            return null;
        }

        return canvas;
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase)
           && node.Attributes.TryGetValue("data-canvas", out var canvasAttr)
           && string.Equals(canvasAttr, "true", StringComparison.OrdinalIgnoreCase);
}

