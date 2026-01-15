// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class GridElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        if (!TranslationHelpers.TryConvertChildrenToRenderables(node.Children, context, out var children))
        {
            return next(node);
        }

        var columnCount = VdomSpectreTranslator.TryGetIntAttribute(node, "data-columns", 2);
        var isExpanded = !string.Equals(VdomSpectreTranslator.GetAttribute(node, "data-expand"), "false", StringComparison.OrdinalIgnoreCase);
        var grid = new Grid()
        {
            Expand = isExpanded
        };

        for (var i = 0; i < columnCount; i++)
        {
            grid.AddColumn();
        }

        if (children.Count > 0)
        {
            foreach (var row in Chunk(children, columnCount))
            {
                grid.AddRow(row);
            }
        }

        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"), out var width))
        {
            grid.Width = width;
        }

        return grid;
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && node.TryGetAttributeValue<string>("class", out var value)
           && string.Equals(value, "grid", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<IRenderable[]> Chunk(IReadOnlyList<IRenderable> items, int size)
    {
        var buffer = new List<IRenderable>(size);
        foreach (var item in items)
        {
            buffer.Add(item);
            if (buffer.Count == size)
            {
                yield return buffer.ToArray();
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            while (buffer.Count < size)
            {
                buffer.Add(new Markup(string.Empty));
            }

            yield return buffer.ToArray();
        }
    }
}

