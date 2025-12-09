// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Contexts;

public sealed class TranslationContext
{
    private readonly Next _pipeline;

    public TranslationContext(
        IEnumerable<ITranslationMiddleware> middlewares)
    {
        ArgumentNullException.ThrowIfNull(middlewares);

        if (!middlewares.Any())
        {
            throw new InvalidOperationException("No translation middleware registered. At least one ITranslationMiddleware must be registered in the service collection.");
        }

        static IRenderable terminalFallback(TranslationContext _, VNode node)
            => throw new InvalidOperationException($"No translation middleware was able to translate the VNode: {node}");

        _pipeline = middlewares
            .Reverse()
            .Aggregate(
                (Next)terminalFallback,
                (next, current) =>
                    (context, node) =>
                        current.Translate(context, next, node));
    }

    public IRenderable Translate(VNode node)
        => _pipeline(this, node);

}
