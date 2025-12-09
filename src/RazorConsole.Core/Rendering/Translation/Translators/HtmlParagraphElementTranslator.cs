// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class HtmlParagraphElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        // Convert children to renderables
        if (!TranslationHelpers.TryConvertChildrenToRenderables(node.Children, context, out var children))
        {
            return next(node);
        }

        if (children.Count == 0)
        {
            return new Markup(string.Empty);
        }

        // If single child, just return it
        if (children.Count == 1)
        {
            return children[0];
        }

        // Multiple children - compose them as column
        return new Columns(children)
        {
            Expand = false,
            Padding = new Padding(0, 0, 0, 0),
        };
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName?.ToLowerInvariant(), "p");
}

