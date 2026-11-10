// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Layout.Leaves;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class HtmlInlineTextElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (node.Kind != VNodeKind.Element
            || !InlineMarkupBuilder.IsInlineTag(node.TagName)
            || !InlineMarkupBuilder.TryBuildMarkup(node, out var markup))
        {
            return next(node);
        }

        return new Markup(markup);
    }
}
