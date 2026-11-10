// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Vdom;
using Spectre.Console;

namespace RazorConsole.Core.Layout.Leaves;

/// <summary>
/// Builds Spectre markup for HTML inline formatting elements such as <c>strong</c>, <c>em</c>, <c>code</c> and <c>a</c>.
/// </summary>
internal static class InlineMarkupBuilder
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

    /// <summary>
    /// Returns whether <paramref name="tagName"/> is an inline formatting element this builder understands.
    /// </summary>
    public static bool IsInlineTag(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        return StyleEnvelopes.ContainsKey(tagName) || QuoteTags.Contains(tagName) || LinkTags.Contains(tagName);
    }

    /// <summary>
    /// Builds markup for a single inline element, keeping its text as written.
    /// </summary>
    public static bool TryBuildMarkup(VNode node, out string markup)
    {
        var writer = new MarkupWriter(collapseWhitespace: false);
        if (!TryAppendElement(node, writer))
        {
            markup = string.Empty;
            return false;
        }

        markup = writer.ToString();
        return true;
    }

    /// <summary>
    /// Builds markup for a run of text and inline elements, collapsing whitespace the way HTML flows text.
    /// </summary>
    /// <returns><see langword="false"/> when a node is neither text nor a supported inline element.</returns>
    public static bool TryBuildFlowMarkup(IReadOnlyList<VNode> nodes, out string markup)
    {
        var writer = new MarkupWriter(collapseWhitespace: true);
        if (!TryAppendChildren(nodes, writer, allowNestedFormatting: true))
        {
            markup = string.Empty;
            return false;
        }

        markup = writer.ToString();
        return true;
    }

    private static bool TryAppendElement(VNode node, MarkupWriter writer)
    {
        if (node.Kind != VNodeKind.Element)
        {
            return false;
        }

        var tagName = node.TagName ?? string.Empty;

        if (StyleEnvelopes.TryGetValue(tagName, out var envelope))
        {
            writer.Open(envelope.Prefix);
            if (!TryAppendChildren(node.Children, writer, envelope.AllowNestedFormatting))
            {
                return false;
            }

            writer.Close(envelope.Suffix);
            return true;
        }

        if (QuoteTags.Contains(tagName))
        {
            writer.Open("“");
            if (!TryAppendChildren(node.Children, writer, allowNestedFormatting: true))
            {
                return false;
            }

            writer.Close("”");
            return true;
        }

        if (LinkTags.Contains(tagName))
        {
            if (node.Attributes.TryGetValue("href", out var href) && !string.IsNullOrWhiteSpace(href))
            {
                writer.Open($"[link={Markup.Escape(href)}]");
                if (!TryAppendChildren(node.Children, writer, allowNestedFormatting: true))
                {
                    return false;
                }

                writer.Close("[/]");
                return true;
            }

            // Without an href the link renders as regular text.
            return TryAppendChildren(node.Children, writer, allowNestedFormatting: true);
        }

        return false;
    }

    private static bool TryAppendChildren(IReadOnlyList<VNode> children, MarkupWriter writer, bool allowNestedFormatting)
    {
        foreach (var child in children)
        {
            switch (child.Kind)
            {
                case VNodeKind.Text:
                    writer.Text(child.Text);
                    break;
                case VNodeKind.Element when allowNestedFormatting:
                    if (!TryAppendElement(child, writer))
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

    private sealed class MarkupWriter(bool collapseWhitespace)
    {
        private readonly StringBuilder _builder = new();
        private bool _hasText;
        private bool _pendingSpace;

        public void Text(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!collapseWhitespace)
            {
                _builder.Append(Markup.Escape(text));
                return;
            }

            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    _pendingSpace = _hasText;
                    continue;
                }

                FlushSpace();
                _builder.Append(ch switch
                {
                    '[' => "[[",
                    ']' => "]]",
                    _ => ch.ToString(),
                });
                _hasText = true;
            }
        }

        // A space before an opening tag belongs outside it, so the tag's style does not cover it.
        public void Open(string tag)
        {
            FlushSpace();
            _builder.Append(tag);
        }

        // A space before a closing tag is deferred until text follows, which drops it at the end of the run.
        public void Close(string tag) => _builder.Append(tag);

        public override string ToString() => _builder.ToString();

        private void FlushSpace()
        {
            if (_pendingSpace)
            {
                _builder.Append(' ');
                _pendingSpace = false;
            }
        }
    }
}
