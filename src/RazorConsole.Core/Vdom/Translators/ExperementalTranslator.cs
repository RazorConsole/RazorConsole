// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Vdom.Translators;

public sealed class ExperementalTranslator(Rendering.Translation.Contexts.TranslationContext ctx) : IVdomElementTranslator
{
    public int Priority => -10;

    public bool TryTranslate(VNode node, TranslationContext context, out IRenderable? renderable)
    {
        renderable = null;
        try
        {
            renderable = ctx.Translate(node);
            return true;
        }
        catch
        {
        }

        return false;
    }
}
