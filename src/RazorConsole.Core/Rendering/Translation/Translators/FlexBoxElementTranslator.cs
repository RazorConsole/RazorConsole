// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class FlexBoxElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        if (!TranslationHelpers.TryConvertChildrenToRenderables(node.Children, context, out var children))
        {
            return next(node);
        }

        var direction = ParseEnum(VdomSpectreTranslator.GetAttribute(node, "data-direction"), FlexDirection.Row);
        var justify = ParseEnum(VdomSpectreTranslator.GetAttribute(node, "data-justify"), FlexJustify.Start);
        var align = ParseEnum(VdomSpectreTranslator.GetAttribute(node, "data-align"), FlexAlign.Start);
        var wrap = ParseEnum(VdomSpectreTranslator.GetAttribute(node, "data-wrap"), FlexWrap.NoWrap);
        var gap = VdomSpectreTranslator.TryGetIntAttribute(node, "data-gap", 0);

        int? width = null;
        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"), out var w))
        {
            width = w;
        }

        int? height = null;
        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-height"), out var h))
        {
            height = h;
        }

        return new FlexBoxRenderable(
            children,
            direction,
            justify,
            align,
            wrap,
            gap,
            width,
            height);
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && node.Attributes.TryGetValue("class", out var value)
           && string.Equals(value, "flexbox", StringComparison.OrdinalIgnoreCase);

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct
    {
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        // Handle multi-word enum names like "SpaceBetween" from "spacebetween"
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) ? result : fallback;
    }
}
