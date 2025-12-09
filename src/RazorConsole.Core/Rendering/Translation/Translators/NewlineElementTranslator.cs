// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class NewlineElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        var count = Math.Max(VdomSpectreTranslator.TryGetIntAttribute(node, "data-count", 1), 0);
        if (count == 0)
        {
            return new Markup(string.Empty);
        }

        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.AppendLine();
        }

        return new Markup(builder.ToString());
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase)
           && node.Attributes.ContainsKey("data-newline");
}

