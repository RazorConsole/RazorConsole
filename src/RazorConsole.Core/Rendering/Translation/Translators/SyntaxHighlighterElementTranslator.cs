// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.Syntax;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class SyntaxHighlighterElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        var payload = VdomSpectreTranslator.GetAttribute(node, "data-payload");
        if (string.IsNullOrEmpty(payload))
        {
            return next(node);
        }

        try
        {
            var model = SyntaxHighlightingService.DecodePayload(payload);
            return new SyntaxRenderable(model);
        }
        catch (Exception)
        {
            return next(node);
        }
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && node.TryGetAttributeValue<string>("class", out var @class)
           && string.Equals(@class, "syntax-highlighter", StringComparison.OrdinalIgnoreCase);
}

