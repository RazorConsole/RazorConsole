// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Abstractions.Rendering;

/// <summary>
/// Represents a delegate that translates a <see cref="VNode"/> into a <see cref="IRenderable"/>.
/// </summary>
/// <remarks>
/// <para>
/// When a middleware cannot handle a particular <see cref="VNode"/>, it should invoke this delegate
/// with the node to pass it to the next middleware in the chain. If no middleware in the chain
/// can handle the node, the terminal fallback will throw an <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
/// <param name="node">The virtual DOM node to translate.</param>
/// <returns>A <see cref="IRenderable"/> that represents the translated node.</returns>
public delegate IRenderable TranslationDelegate(VNode node);

/// <summary>
/// Defines a middleware component in the translation pipeline that converts <see cref="VNode"/> instances
/// into <see cref="IRenderable"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// The translation pipeline uses a chain-of-responsibility pattern where multiple middleware components
/// are registered and executed in order. Each middleware can either:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>Handle the node and return an <see cref="IRenderable"/> directly, stopping the chain.</description>
/// </item>
/// <item>
/// <description>Delegate to the next middleware by calling <see cref="TranslationDelegate"/>.</description>
/// </item>
/// </list>
/// </remarks>
public interface ITranslationMiddleware
{

    /// <summary>
    /// Translates a virtual DOM node into a <see cref="IRenderable"/>
    /// </summary>
    /// <param name="context">The translation context providing access to the translation pipeline and shared state.</param>
    /// <param name="next">The delegate representing the next middleware in the chain. Call this to pass the node to the next middleware if this middleware cannot handle it.</param>
    /// <param name="node">The virtual DOM node to translate.</param>
    /// <returns>
    /// <see cref="IRenderable"/> that represents the translated node.
    /// If this middleware cannot handle the node, it should return <paramref name="next"/>(<paramref name="node"/>).
    /// </returns>
    /// <remarks>
    /// <para>
    /// Implementations should check if they can handle the given <paramref name="node"/> based on its
    /// <see cref="VNode.Kind"/>, <see cref="VNode.TagName"/>, attributes, or other properties.
    /// </para>
    /// <para>
    /// If the middleware can handle the node, it should:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>Translate child nodes recursively using <see cref="TranslationContext.Translate(VNode)"/> if needed.</description>
    /// </item>
    /// <item>
    /// <description>Create and return an appropriate <see cref="IRenderable"/> instance.</description>
    /// </item>
    /// </list>
    /// <para>
    /// If the middleware cannot handle the node, it must call <paramref name="next"/>
    /// to pass it to the next middleware in the chain. Failure to do so will prevent other middlewares from
    /// processing the node.
    /// </para>
    /// </remarks>
    IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node);

}
