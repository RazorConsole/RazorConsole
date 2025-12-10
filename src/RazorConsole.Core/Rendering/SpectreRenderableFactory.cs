// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering;

public static class SpectreRenderableFactory
{
    internal static bool TryCreateRenderable(
        VNode? vdomRoot,
        TranslationContext translationContext,
        out IRenderable? renderable,
        out IReadOnlyCollection<IAnimatedConsoleRenderable> animatedRenderables)
    {
        if (vdomRoot is null)
        {
            throw new InvalidOperationException("A virtual DOM root is required to create Spectre renderables.");
        }

        if (translationContext is null)
        {
            throw new ArgumentNullException(nameof(translationContext));
        }

        try
        {
            translationContext.AnimatedRenderables.Clear();
            renderable = translationContext.Translate(vdomRoot);
            animatedRenderables = translationContext.AnimatedRenderables;
            return renderable is not null;
        }
        catch
        {
            renderable = null;
            animatedRenderables = Array.Empty<IAnimatedConsoleRenderable>();
            return false;
        }
    }
}
