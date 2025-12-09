// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class HtmlListElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        var tagName = node.TagName?.ToLowerInvariant();
        var isOrdered = tagName == "ol";
        var startNumber = 1;

        // Get start attribute for ordered lists
        if (isOrdered)
        {
            var startAttr = VdomSpectreTranslator.GetAttribute(node, "start");
            if (!string.IsNullOrWhiteSpace(startAttr) && int.TryParse(startAttr, out var parsedStart))
            {
                startNumber = parsedStart;
            }
        }

        // Get list items (only process li elements)
        var listItems = node.Children.Where(c => c.Kind == VNodeKind.Element &&
                                                 string.Equals(c.TagName, "li", StringComparison.OrdinalIgnoreCase))
                                      .ToList();

        if (listItems.Count == 0)
        {
            return new Markup(string.Empty);
        }

        var itemRenderables = new System.Collections.Generic.List<IRenderable>();

        for (int i = 0; i < listItems.Count; i++)
        {
            var listItem = listItems[i];
            var prefix = isOrdered ? $"{startNumber + i}. " : "• ";

            // Convert list item children to renderables
            if (!TranslationHelpers.TryConvertChildrenToBlockInlineRenderable(listItem.Children, context, out var itemChildRenderable))
            {
                return next(node);
            }

            // Create the list item with prefix
            IRenderable itemContent;
            if (itemChildRenderable is null)
            {
                itemContent = new Markup(prefix);
            }
            else
            {
                itemContent = new Columns(new IRenderable[] { new Markup(prefix), itemChildRenderable })
                {
                    Expand = false,
                    Padding = new Padding(0, 0, 0, 0),
                };
            }

            itemRenderables.Add(itemContent);
        }

        return new Rows(itemRenderables)
        {
            Expand = false,
        };
    }

    private static bool CanHandle(VNode node)
    {
        if (node.Kind != VNodeKind.Element)
        {
            return false;
        }

        var tagName = node.TagName?.ToLowerInvariant();
        return tagName == "ul" || tagName == "ol";
    }
}

