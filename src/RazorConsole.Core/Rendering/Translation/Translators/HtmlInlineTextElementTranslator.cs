// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class HtmlInlineTextElementTranslator : ITranslationMiddleware
{
    private static readonly IReadOnlyDictionary<string, MarkupEnvelope> StyleEnvelopes =
        new Dictionary<string, MarkupEnvelope>(StringComparer.OrdinalIgnoreCase)
        {
            ["strong"] = new MarkupEnvelope("[bold]", "[/]"),
            ["b"] = new MarkupEnvelope("[bold]", "[/]"),
            ["em"] = new MarkupEnvelope("[italic]", "[/]"),
            ["i"] = new MarkupEnvelope("[italic]", "[/]"),
            ["mark"] = new MarkupEnvelope("[black on yellow]", "[/]"),
            ["del"] = new MarkupEnvelope("[strikethrough]", "[/]"),
            ["ins"] = new MarkupEnvelope("[underline]", "[/]"),
            ["code"] = new MarkupEnvelope("[indianred1 on #1f1f1f]", "[/]", AllowNestedFormatting: false),
            ["abbr"] = new MarkupEnvelope("[underline]", "[/]"),
            ["cite"] = new MarkupEnvelope("[italic]", "[/]"),
            ["small"] = new MarkupEnvelope("[dim]", "[/]"),
            ["sub"] = new MarkupEnvelope("[dim]", "[/]"),
            ["sup"] = new MarkupEnvelope("[dim]", "[/]"),
        };

    private static readonly HashSet<string> QuoteTags = new(StringComparer.OrdinalIgnoreCase) { "q" };

    private static readonly HashSet<string> LinkTags = new(StringComparer.OrdinalIgnoreCase) { "a" };

    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        if (!TryBuildMarkup(node, out var markup))
        {
            return next(node);
        }

        return new Markup(markup);
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && IsSupportedInlineElement(node.TagName);

    private static bool IsSupportedInlineElement(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        return StyleEnvelopes.ContainsKey(tagName) || QuoteTags.Contains(tagName) || LinkTags.Contains(tagName);
    }

    internal static bool TryBuildMarkup(VNode node, out string markup)
    {
        var builder = new StringBuilder();

        if (!TryAppendElement(node, builder))
        {
            markup = string.Empty;
            return false;
        }

        markup = builder.ToString();
        return true;
    }

    private static bool TryAppendElement(VNode node, StringBuilder builder)
    {
        if (node.Kind != VNodeKind.Element)
        {
            return false;
        }

        var tagName = node.TagName ?? string.Empty;

        if (StyleEnvelopes.TryGetValue(tagName, out var envelope))
        {
            builder.Append(envelope.Prefix);
            if (!TryAppendChildren(node.Children, builder, envelope.AllowNestedFormatting))
            {
                return false;
            }

            builder.Append(envelope.Suffix);
            return true;
        }

        if (QuoteTags.Contains(tagName))
        {
            builder.Append("\u201C");
            if (!TryAppendChildren(node.Children, builder, allowNestedFormatting: true))
            {
                return false;
            }

            builder.Append("\u201D");
            return true;
        }

        if (LinkTags.Contains(tagName))
        {
            if (node.TryGetAttributeValue<string>("href", out var href) && !string.IsNullOrWhiteSpace(href))
            {
                builder.Append($"[link={Markup.Escape(href)}]");
                if (!TryAppendChildren(node.Children, builder, allowNestedFormatting: true))
                {
                    return false;
                }

                builder.Append("[/]");
                return true;
            }

            // If no href attribute, render as regular text
            if (!TryAppendChildren(node.Children, builder, allowNestedFormatting: true))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool TryAppendChildren(IReadOnlyList<VNode> children, StringBuilder builder, bool allowNestedFormatting)
    {
        foreach (var child in children)
        {
            switch (child.Kind)
            {
                case VNodeKind.Text when !string.IsNullOrEmpty(child.Text):
                    builder.Append(Markup.Escape(child.Text));
                    break;
                case VNodeKind.Element when allowNestedFormatting:
                    if (!TryAppendElement(child, builder))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private readonly record struct MarkupEnvelope(string Prefix, string Suffix, bool AllowNestedFormatting = true);
}
