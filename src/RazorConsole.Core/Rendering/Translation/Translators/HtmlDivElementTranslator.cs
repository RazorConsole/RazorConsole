// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class HtmlDivElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        if (node.Children.Count == 0)
        {
            return new Markup(string.Empty);
        }

        if (TranslationHelpers.TryConvertChildrenToBlockInlineRenderable(node.Children, context, out var bir) && bir is not null)
        {
            return bir;
        }

        return next(node);
    }

    private static bool CanHandle(VNode node)
    {
        if (node.Kind != VNodeKind.Element || !string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Skip div elements with special classes that should be handled by other translators
        if (node.TryGetAttributeValue<string>("class", out var classValue))
        {
            var classes = classValue?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (classes.Any(c => string.Equals(c, "figlet", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(c, "panel", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(c, "rows", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(c, "columns", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(c, "grid", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(c, "padder", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(c, "align", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(c, "spinner", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        // Skip div elements with special data attributes
        if (node.Attributes.ContainsKey("data-spacer") ||
            node.Attributes.ContainsKey("data-newline") ||
            node.Attributes.ContainsKey("data-button") ||
            node.Attributes.ContainsKey("data-canvas"))
        {
            return false;
        }

        return true;
    }
}

