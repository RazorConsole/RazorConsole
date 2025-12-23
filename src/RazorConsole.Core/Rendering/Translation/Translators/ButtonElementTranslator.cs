// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.ComponentMarkup;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class ButtonElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        if (!TranslationHelpers.TryConvertChildrenToBlockInlineRenderable(node.Children, context, out var children) || children is null)
        {
            return next(node);
        }

        var descriptor = ButtonRenderableDescriptorFactory.Create(name => VdomSpectreTranslator.GetAttribute(node, name));
        return ButtonRenderableBuilder.Build(descriptor, children);
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && (string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase)
               || string.Equals(node.TagName, "button", StringComparison.OrdinalIgnoreCase))
           && node.TryGetAttributeValue<string>("data-button", out var value)
           && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}

