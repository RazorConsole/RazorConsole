// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class HtmlBlockquoteElementTranslator : ITranslationMiddleware
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

        var content = VdomSpectreTranslator.ComposeChildContent(children);
        var panel = new Panel(content)
        {
            Border = BoxBorder.None,
            Padding = new Padding(2, 0, 0, 0)
        };

        return panel;
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName?.ToLowerInvariant(), "blockquote");
}

