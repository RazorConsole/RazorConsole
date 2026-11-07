// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class BreakdownChartTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
        => IsBreakdownChart(node) && ChartRenderableBuilder.CreateBreakdownChart(node) is { } breakdownChart
            ? breakdownChart
            : next(node);

    private static bool IsBreakdownChart(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "breakdownchart", StringComparison.OrdinalIgnoreCase);
}
