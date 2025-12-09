// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class SpacerElementTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        var lines = Math.Max(VdomSpectreTranslator.TryGetIntAttribute(node, "data-lines", 1), 0);
        if (lines == 0)
        {
            return new Markup(string.Empty);
        }

        var fill = VdomSpectreTranslator.GetAttribute(node, "data-fill");
        var builder = new StringBuilder();

        if (string.IsNullOrEmpty(fill))
        {
            for (var i = 0; i < lines; i++)
            {
                builder.AppendLine();
            }
        }
        else
        {
            var glyph = Markup.Escape(fill[0].ToString());
            for (var i = 0; i < lines; i++)
            {
                builder.Append(glyph);
                builder.AppendLine();
            }
        }

        return new Markup(builder.ToString());
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase)
           && node.Attributes.ContainsKey("data-spacer");
}

