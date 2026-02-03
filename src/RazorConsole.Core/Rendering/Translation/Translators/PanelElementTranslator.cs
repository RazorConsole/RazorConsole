// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.ComponentMarkup;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class PanelElementTranslator : ITranslationMiddleware
{
    private static readonly IReadOnlyDictionary<string, BoxBorder> BorderLookup = new Dictionary<string, BoxBorder>(StringComparer.OrdinalIgnoreCase)
        {
            { "square", BoxBorder.Square },
            { "rounded", BoxBorder.Rounded },
            { "double", BoxBorder.Double },
            { "heavy", BoxBorder.Heavy },
            { "ascii", BoxBorder.Ascii },
            { "none", BoxBorder.None },
        };

    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!IsPanelNode(node))
        {
            return next(node);
        }

        if (!TranslationHelpers.TryConvertChildrenToRenderables(node.Children, context, out var children))
        {
            return next(node);
        }

        var content = VdomSpectreTranslator.ComposeChildContent(children);
        var panel = new Panel(content);

        if (ShouldExpand(node))
        {
            panel = panel.Expand();
        }

        panel.Border = ResolveBorder(VdomSpectreTranslator.GetAttribute(node, "data-border"));

        if (VdomSpectreTranslator.TryParsePadding(VdomSpectreTranslator.GetAttribute(node, "data-padding"), out var padding))
        {
            panel.Padding = padding;
        }

        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-height"), out var height))
        {
            panel.Height = height;
        }

        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"), out var width))
        {
            panel.Width = width;
        }

        ApplyHeader(node, panel);
        ApplyBorderColor(node, panel);

        return panel;
    }

    private static bool IsPanelNode(VNode node)
        => node.Kind == VNodeKind.Element
           && node.TryGetAttributeValue<string>("class", out var value)
           && string.Equals(value, "panel", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldExpand(VNode node)
    {
        var value = VdomSpectreTranslator.GetAttribute(node, "data-expand");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static BoxBorder ResolveBorder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BoxBorder.Square;
        }

        return BorderLookup.TryGetValue(value, out var border) ? border : BoxBorder.Square;
    }

    private static void ApplyHeader(VNode node, Panel panel)
    {
        var header = VdomSpectreTranslator.GetAttribute(node, "data-header");
        if (string.IsNullOrWhiteSpace(header))
        {
            return;
        }

        var headerColor = VdomSpectreTranslator.GetAttribute(node, "data-header-color");
        var markup = string.IsNullOrWhiteSpace(headerColor)
            ? Markup.Escape(header)
            : ComponentMarkupUtilities.CreateStyledMarkup(headerColor, header, requiresEscape: true);

        panel.Header = new PanelHeader(markup);
    }

    private static void ApplyBorderColor(VNode node, Panel panel)
    {
        var borderColorValue = VdomSpectreTranslator.GetAttribute(node, "data-border-color");
        if (string.IsNullOrWhiteSpace(borderColorValue))
        {
            return;
        }

        try
        {
            var style = Style.Parse(borderColorValue);
            panel.BorderStyle(style);
        }
        catch (Exception)
        {
            // Ignore invalid style specifications.
        }
    }
}

