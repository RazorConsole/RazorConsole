// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Renderables;

using RazorConsole.Core.Vdom;

using Spectre.Console;
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

        node.TryGetAttributeValue<HorizontalAlignment>("data-horizontal", out var horizontal);
        node.TryGetAttributeValue<VerticalAlignment>("data-vertical", out var vertical);
        node.TryGetAttributeValue<int>("data-width", out var width);
        node.TryGetAttributeValue<int>("data-height", out var height);

        var align = new MeasuredAlign(children, horizontal, vertical)
        {
            Width = width,
            Height = height,
        };

        return align;
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && node.TryGetAttributeValue<string>("class", out var value)
           && string.Equals(value, "align", StringComparison.OrdinalIgnoreCase);
}

