// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Utilities;
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
        var fontKey = VdomSpectreTranslator.GetAttribute(node, "data-font");
        FigletText figlet;
        if (!string.IsNullOrWhiteSpace(fontKey) && FigletFontRegistry.TryGetOrLoad(fontKey, out var font))
        {
            figlet = new FigletText(font, content) { Justification = justify, Color = style.Foreground };
        }
        else
        {
            figlet = new FigletText(content) { Justification = justify, Color = style.Foreground };
        }

        return figlet;
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && node.Attributes.TryGetValue("class", out var value)
           && string.Equals(value, "figlet", StringComparison.OrdinalIgnoreCase)
           && node.Children is { Count: 0 };
}
