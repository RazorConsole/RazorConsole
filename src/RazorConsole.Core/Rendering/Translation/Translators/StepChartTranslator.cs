// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class StepChartTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
        => node.Kind == VNodeKind.Element && node.TagName == "stepchart"
            ? ChartRenderableBuilder.CreateStepChart(node)
            : next(node);
}
