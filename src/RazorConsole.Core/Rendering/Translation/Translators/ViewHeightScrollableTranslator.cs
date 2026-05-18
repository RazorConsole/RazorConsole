// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.Renderables;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public class ViewHeightScrollableTranslator(ScrollableLayoutCoordinator scrollableLayoutCoordinator)
    : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!string.Equals(node.TagName, "view-height-scrollable", StringComparison.OrdinalIgnoreCase))
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

        if (!int.TryParse(VdomSpectreTranslator.GetAttribute(node, "data-offset"), out var offset))
        {
            return next(node);
        }

        if (!int.TryParse(VdomSpectreTranslator.GetAttribute(node, "data-lines-to-render"), out var linesToRender)
            || linesToRender < 0)
        {
            return next(node);
        }

        linesToRender = linesToRender == 0
            ? Math.Max(1, Console.WindowHeight)
            : linesToRender;

        if (!VdomSpectreTranslator.TryGetBoolAttribute(node, "data-enable-embedded", out var enableEmbedded))
        {
            return next(node);
        }

        if (!TranslationHelpers.TryConvertChildrenToRenderables(node.Children, context, out var contentRenderable))
        {
            return next(node);
        }

        var scrollId = VdomSpectreTranslator.GetAttribute(node, "data-scroll-id");
        if (string.IsNullOrEmpty(scrollId))
        {
            return next(node);
        }
        if (scrollbars.Count == 0)
        {
            return new ScrollableRenderable(
                contentRenderable, 0, offset, linesToRender, enableEmbedded,
                scrollableLayoutCoordinator,
                scrollbarSettings: null,
                cropLines: true,
                scrollId: scrollId
            );
        }

        var scrollbarNode = scrollbars.Single();

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
            !Color.TryFromHex(trackColorStr, out var trackColor))
        {
            return next(node);
        }

        if (!scrollbarNode.Attributes.TryGetValue("data-thumb-color", out var thumbColorStr) ||
            string.IsNullOrEmpty(thumbColorStr) ||
            !Color.TryFromHex(thumbColorStr, out var thumbColor))
        {
            return next(node);
        }

        if (!VdomSpectreTranslator.TryParsePositiveInt(
                VdomSpectreTranslator.GetAttribute(scrollbarNode, "data-min-thumb-height"), out var minThumbHeight))
        {
            return next(node);
        }

        var scrollbarSettings = new ScrollbarSettings(trackChar, thumbChar, trackColor, thumbColor, minThumbHeight);
        return new ScrollableRenderable(
            contentRenderable, 0, offset, linesToRender, enableEmbedded,
            scrollableLayoutCoordinator,
            scrollbarSettings,
            cropLines: true,
            scrollId: scrollId
        );
    }
}
