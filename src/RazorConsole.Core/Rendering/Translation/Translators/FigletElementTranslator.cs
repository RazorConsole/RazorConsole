// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class FigletElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        var content = VdomSpectreTranslator.GetAttribute(node, "data-content");

        if (string.IsNullOrWhiteSpace(content))
        {
            return next(node);
        }

        var styleAttribute = VdomSpectreTranslator.GetAttribute(node, "data-style");
        var style = new Style(Color.Default);
        if (!string.IsNullOrWhiteSpace(styleAttribute))
        {
            style = Style.Parse(styleAttribute);
        }

        var justifyAttribute = VdomSpectreTranslator.GetAttribute(node, "data-justify");
        var justify = (justifyAttribute?.ToLowerInvariant()) switch
        {
            "left" => Justify.Left,
            "right" => Justify.Right,
            "center" => Justify.Center,
            _ => Justify.Left,
        };
        var figlet = new FigletText(content)
        {
            Justification = justify,
            Color = style.Foreground
        };

        return figlet;
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && node.TryGetAttributeValue<string>("class", out var value)
           && string.Equals(value, "figlet", StringComparison.OrdinalIgnoreCase)
           && node.Children is { Count: 0 };
}

