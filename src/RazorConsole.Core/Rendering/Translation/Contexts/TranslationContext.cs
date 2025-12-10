// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Contexts;

/// <summary>
/// Provides a context for translating virtual DOM nodes into <see cref="IRenderable"/> using a middleware pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="TranslationContext"/> orchestrates the translation of <see cref="VNode"/> instances into
/// <see cref="IRenderable"/> instances using a chain-of-responsibility pattern. It builds a pipeline from
/// registered <see cref="ITranslationMiddleware"/> components and executes them in order until one successfully
/// handles the node.
/// </para>
/// </remarks>
public sealed class TranslationContext
{

    private readonly TranslationDelegate _pipeline;

    /// <summary>
    /// Gets a collection of animated renderables that require periodic updates during live display.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Middleware components can add animated renderables to this collection during translation.
    /// These renderables are collected and used by the rendering system to manage animations
    /// that need to be refreshed periodically (e.g., spinners, progress indicators).
    /// </para>
    /// <para>
    /// The collection is cleared before each translation pass and populated as nodes are translated.
    /// After translation completes, the collection contains all animated renderables discovered during
    /// the translation process.
    /// </para>
    /// </remarks>
    public HashSet<IAnimatedConsoleRenderable> AnimatedRenderables { get; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationContext"/> class with the specified middleware components.
    /// </summary>
    /// <param name="middlewares">The collection of translation middleware components to use in the pipeline.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="middlewares"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="middlewares"/> is empty. At least one middleware must be registered.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The order of middleware execution is determined by the order in which they are registered in
    /// the dependency injection container. Middleware registered first will be called first.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Translates a virtual DOM node into a <see cref="IRenderable"/> using the middleware pipeline.
    /// </summary>
    /// <param name="node">The virtual DOM node to translate.</param>
    /// <returns>
    /// An <see cref="IRenderable"/> that represents the translated node. The specific type depends on
    /// which middleware handled the translation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no middleware in the pipeline can handle the specified node.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method initiates the translation process by invoking the middleware pipeline with the
    /// specified node. The pipeline will attempt to find a middleware that can handle the node,
    /// executing middlewares in registration order until one returns a result.
    /// </para>
    /// <para>
    /// The <see cref="AnimatedRenderables"/> collection is not automatically cleared by this method.
    /// It should be cleared before starting a new translation pass if you need to collect only the
    /// animated renderables from the current translation.
    /// </para>
    /// </remarks>
    public IRenderable Translate(VNode node)
        => _pipeline(node);

}
