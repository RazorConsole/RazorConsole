// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;

using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

internal sealed class TextElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        string? text;
        if (node.TryGetAttributeValue<string>("data-content", out var inlineContent) && inlineContent is not null)
        {
            if (node.Children.Any())
            {
                // Prefer explicit content attribute when present and require no additional children.
                return next(node);
            }

            text = inlineContent;
        }
        else
        {
            text = VdomSpectreTranslator.CollectInnerText(node);
            if (string.IsNullOrWhiteSpace(text))
            {
                // Missing required text content.
                return next(node);
            }
        }

        var styleAttributes = VdomSpectreTranslator.GetAttribute(node, "data-style");
        if (string.IsNullOrEmpty(styleAttributes))
        {
            return new Markup(text);
        }
        else
        {
            var style = Style.Parse(styleAttributes ?? string.Empty);
            return new Markup(text, style);
        }
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "span", StringComparison.OrdinalIgnoreCase)
           && node.TryGetAttributeValue<string>("data-text", out var value)
           && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
