// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Components;
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class TextButtonTranslator : ITranslationMiddleware
{
    internal static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Component && node.Component is TextButton;

    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        var component = (TextButton)node.Component!;
        return new Text(component.Content, GetStyle(component));
    }

    internal static Widget TranslateWidget(VNode node, int zIndex)
    {
        var component = (TextButton)node.Component!;
        return new TextWidget(node.ID, component.Content, GetStyle(component),
            node.Key, node.Attributes, zIndex);
    }

    private static Style GetStyle(TextButton component)
    {
        var background = component.IsFocused ? component.FocusedColor
            : component.IsHovered ? component.HoverColor : component.BackgroundColor;
        var foreground = component.IsHovered && !component.IsFocused
            ? component.HoverForegroundColor : Color.Default;
        return new Style(foreground, background);
    }
}
