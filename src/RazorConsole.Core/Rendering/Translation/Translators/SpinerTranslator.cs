// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class SpinerTranslator : ITranslationMiddleware
{
    public IRenderable Translate(Contexts.TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        var spinnerType = VdomSpectreTranslator.GetAttribute(node, "data-spinner-type");
        var spinner = ComponentMarkupUtilities.ResolveSpinner(spinnerType);
        var message = VdomSpectreTranslator.GetAttribute(node, "data-message") ?? string.Empty;
        var style = VdomSpectreTranslator.GetAttribute(node, "data-style");
        var autoDismiss = VdomSpectreTranslator.TryGetBoolAttribute(node, "data-auto-dismiss", out var parsed) && parsed;

        var animated = new AnimatedSpinnerRenderable(spinner, message, style, autoDismiss);
        context.AnimatedRenderables.Add(animated);
        return animated;
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
        && string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase)
        && node.Attributes.ContainsKey("data-spinner");

}
