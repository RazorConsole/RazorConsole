// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Renderables;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class AlignElementTranslator : ITranslationMiddleware
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

        var horizontal = VdomSpectreTranslator.ParseHorizontalAlignment(VdomSpectreTranslator.GetAttribute(node, "data-horizontal"));
        var vertical = VdomSpectreTranslator.ParseVerticalAlignment(VdomSpectreTranslator.GetAttribute(node, "data-vertical"));
        var width = VdomSpectreTranslator.ParseOptionalPositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"));
        var height = VdomSpectreTranslator.ParseOptionalPositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-height"));

        var align = new MeasuredAlign(children, horizontal, vertical)
        {
            Width = width,
            Height = height,
        };

        return align;
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && node.Attributes.TryGetValue("class", out var value)
           && string.Equals(value, "align", StringComparison.OrdinalIgnoreCase);
}

