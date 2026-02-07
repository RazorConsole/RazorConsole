// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public class AbsolutePositionMiddleware : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        var position = VdomSpectreTranslator.GetAttribute(node, "position");

        if (!string.Equals(position, "absolute", StringComparison.OrdinalIgnoreCase))
        {
            return next(node);
        }

        var top = int.TryParse(VdomSpectreTranslator.GetAttribute(node, "top"), out int topVal)
            ? topVal
            : (int?)null;
        var left = int.TryParse(VdomSpectreTranslator.GetAttribute(node, "left"), out int leftVal)
            ? leftVal
            : (int?)null;
        var bottom = int.TryParse(VdomSpectreTranslator.GetAttribute(node, "bottom"), out int bottomVal)
            ? bottomVal
            : (int?)null;
        var right = int.TryParse(VdomSpectreTranslator.GetAttribute(node, "right"), out int rightVal)
            ? rightVal
            : (int?)null;
        var zIndex = VdomSpectreTranslator.TryGetIntAttribute(node, "z-index", 0);

        int? finalTop = top.HasValue ? top.Value + context.CumulativeTop : null;
        int? finalLeft = left.HasValue ? left.Value + context.CumulativeLeft : null;

        int previousTop = context.CumulativeTop;
        int previousLeft = context.CumulativeLeft;

        context.CumulativeTop = finalTop ?? previousTop;
        context.CumulativeLeft = finalLeft ?? previousLeft;

        var renderable = next(node);

        context.CumulativeTop = previousTop;
        context.CumulativeLeft = previousLeft;

        context.CollectedOverlays.Add(new OverlayItem(
            renderable,
            finalTop,
            finalLeft,
            right,
            bottom,
            zIndex
        ));

        return new Markup(string.Empty);
    }
}
