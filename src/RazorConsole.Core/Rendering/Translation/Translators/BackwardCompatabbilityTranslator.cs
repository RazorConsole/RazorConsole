// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class BackwardCompatabbilityTranslator(VdomSpectreTranslator translator) : ITranslationMiddleware
{
    public IRenderable Translate(Contexts.TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!translator.TryTranslate(node, out var renderable, out var _))
        {
            return next(node);
        }
        return renderable!;
    }
}
