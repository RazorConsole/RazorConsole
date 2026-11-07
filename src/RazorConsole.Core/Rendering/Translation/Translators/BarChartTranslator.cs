// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class BarChartTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
        => IsBarChart(node) && ChartRenderableBuilder.CreateBarChart(node) is { } barChart
            ? barChart
            : next(node);

    private static bool IsBarChart(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "barchart", StringComparison.OrdinalIgnoreCase);
}
