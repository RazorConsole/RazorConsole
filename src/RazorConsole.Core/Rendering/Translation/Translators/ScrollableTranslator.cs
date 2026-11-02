// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.Renderables;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Utilities;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public class ScrollableTranslator(ScrollableLayoutCoordinator scrollableLayoutCoordinator) : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!string.Equals(node.TagName, "scrollable", StringComparison.OrdinalIgnoreCase))
        {
            return next(node);
        }

        var scrollbars = node.Children
            .Where(n => VdomSpectreTranslator.TryGetBoolAttribute(n, "data-scrollbar", out var value) && value)
            .ToList();
        // If there are many scrollbars - then component cannot be translated
        if (scrollbars.Count > 1)
        {
            return next(node);
        }

        // If there is not any scrollbar - render just rows layout
        if (scrollbars.Count == 0)
        {
            if (!TranslationHelpers.TryConvertChildrenToRenderables(node.Children, context, out var simpleContent))
            {
                return next(node);
            }

            return new Rows(simpleContent);
        }

        var scrollbarNode = scrollbars.Single();

        var itemsCount = VdomSpectreTranslator.TryGetIntAttribute(node, "data-items-count", 0);

        if (!int.TryParse(VdomSpectreTranslator.GetAttribute(node, "data-offset"), out var offset))
        {
            return next(node);
        }

        if (!VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-page-size"),
                out var pageSize))
        {
            return next(node);
        }

        if (!VdomSpectreTranslator.TryGetBoolAttribute(node, "data-enable-embedded", out var enableEmbedded))
        {
            return next(node);
        }

        // Extracting styling parameters
        if (!char.TryParse(VdomSpectreTranslator.GetAttribute(scrollbarNode, "data-track-char"), out var trackChar))
        {
            return next(node);
        }

        if (!char.TryParse(VdomSpectreTranslator.GetAttribute(scrollbarNode, "data-thumb-char"), out var thumbChar))
        {
            return next(node);
        }

        if (!scrollbarNode.Attributes.TryGetValue("data-track-color", out var trackColorStr) ||
            string.IsNullOrEmpty(trackColorStr) ||
            !ColorAttribute.TryParse(trackColorStr, out var trackColor))
        {
            return next(node);
        }

        if (!scrollbarNode.Attributes.TryGetValue("data-thumb-color", out var thumbColorStr) ||
            string.IsNullOrEmpty(thumbColorStr) ||
            !ColorAttribute.TryParse(thumbColorStr, out var thumbColor))
        {
            return next(node);
        }

        if (!VdomSpectreTranslator.TryParsePositiveInt(
                VdomSpectreTranslator.GetAttribute(scrollbarNode, "data-min-thumb-height"), out var minThumbHeight))
        {
            return next(node);
        }

        // Converting children
        if (!TranslationHelpers.TryConvertChildrenToRenderables(node.Children, context, out var contentRenderable))
        {
            return next(node);
        }

        var scrollbarSettings = new ScrollbarSettings(trackChar, thumbChar, trackColor, thumbColor, minThumbHeight);
        return new ScrollableRenderable(
            contentRenderable, itemsCount, offset, pageSize, enableEmbedded,
            scrollableLayoutCoordinator,
            scrollbarSettings
        );
    }
}
