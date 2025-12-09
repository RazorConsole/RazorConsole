// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Contexts;

public sealed class TranslationContext
{

    private readonly TranslationDelegate _pipeline;

    public TranslationContext(
        IEnumerable<ITranslationMiddleware> middlewares)
    {
        ArgumentNullException.ThrowIfNull(middlewares);

        if (!middlewares.Any())
        {
            throw new InvalidOperationException("No translation middleware registered. At least one ITranslationMiddleware must be registered in the service collection.");
        }

        static IRenderable terminalFallback(VNode node)
            => throw new InvalidOperationException($"No translation middleware was able to translate the VNode: {node}");

        _pipeline = middlewares
            .Reverse()
            .Aggregate(
                (TranslationDelegate)terminalFallback,
                (next, current) =>
                    node =>
                        current.Translate(this, next, node));
    }

    public IRenderable Translate(VNode node)
        => _pipeline(node);

}
