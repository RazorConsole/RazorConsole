// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Abstractions.Rendering;

/// <summary>
/// Translates a <see cref="VNode"/> with the rest of the translation chain.
/// </summary>
/// <param name="node">The virtual DOM node to translate.</param>
/// <returns>The widget for <paramref name="node"/>.</returns>
public delegate Widget WidgetTranslationDelegate(VNode node);

/// <summary>
/// Customizes how virtual DOM nodes become layout <see cref="Widget"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Register a translator with
/// <see cref="RazorConsoleServiceCollectionExtensions.AddWidgetTranslator{TTranslator}(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// Translators run in registration order, before RazorConsole's built-in translation, for every node that becomes a widget
/// of its own: the root, and every child that a built-in container translates through <see cref="WidgetTranslationContext.Translate(VNode)"/>.
/// Some built-in elements read their content directly into a single widget, so those inner nodes never reach a translator:
/// the text and inline elements (<c>strong</c>, <c>a</c>, <c>code</c>, ...) inside a paragraph or other inline run, headings,
/// list items and nested lists, the <c>code</c> inside a <c>pre</c>, and the parts of built-in components such as chart series.
/// To change those, handle the enclosing element.
/// </para>
/// <para>
/// A translator either handles the node and returns a widget, or returns <c>next(node)</c> to pass it on.
/// Calling <c>next(node)</c> and wrapping the result decorates the default translation.
/// Translate child nodes with <see cref="WidgetTranslationContext.Translate(VNode)"/>, which runs the whole chain again.
/// Never call it for the node being handled: that restarts the chain on the same node and recurses until the stack overflows.
/// Use <c>next(node)</c> for the current node and <c>context.Translate(child)</c> only for its children.
/// A translator must not return <see langword="null"/>.
/// </para>
/// <para>
/// To show a Spectre.Console <see cref="Spectre.Console.Rendering.IRenderable"/>, return a <see cref="SpectreWidget"/>;
/// it is laid out as a leaf and painted into the terminal cells like any other widget.
/// </para>
/// </remarks>
public interface IWidgetTranslator
{
    /// <summary>
    /// Translates <paramref name="node"/>, or passes it to <paramref name="next"/>.
    /// </summary>
    /// <param name="context">The translation context, used to translate child nodes and track animations.</param>
    /// <param name="next">The rest of the chain, ending with the built-in translation.</param>
    /// <param name="node">The virtual DOM node to translate.</param>
    /// <returns>The widget for <paramref name="node"/>.</returns>
    Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node);
}
