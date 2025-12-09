// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

internal sealed class TextNodeTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (node.Kind != VNodeKind.Text)
        {
            return next(node);
        }

        var normalized = VdomSpectreTranslator.NormalizeTextNode(node.Text);

        if (!normalized.HasContent)
        {
            return new Markup(string.Empty);
        }

        return new Text($"{(normalized.LeadingWhitespace ? " " : "")}{normalized.Content}{(normalized.TrailingWhitespace ? " " : "")}");
    }
}

