// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

internal sealed class ComponentRegionTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (node.Kind != VNodeKind.Component && node.Kind != VNodeKind.Region)
        {
            return next(node);
        }

        if (TranslationHelpers.TryConvertChildrenToBlockInlineRenderable(node.Children, context, out var children) && children is not null)
        {
            return children;
        }

        return next(node);
    }
}

