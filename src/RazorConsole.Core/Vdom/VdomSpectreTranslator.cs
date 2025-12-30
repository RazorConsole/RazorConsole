// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text;

using RazorConsole.Core.Extensions;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Vdom;

/// <summary>
/// Static helper methods for translating VNodes to Spectre.Console renderables.
/// </summary>
public static class VdomSpectreTranslator
{

    /// <summary>
    /// Gets an attribute value from a VNode.
    /// </summary>
    /// <param name="node">The VNode to get the attribute from.</param>
    /// <param name="name">The name of the attribute.</param>
    /// <returns>The attribute value if found; otherwise, null.</returns>
    public static string? GetAttribute(VNode node, string name)
    {
        if (node.Kind != VNodeKind.Element)
        {
            return null;
        }

        return node.TryGetAttributeValue<string>(name, out var value) ? value : null;
    }

    /// <summary>
    /// Normalizes a text node by trimming whitespace and collapsing multiple spaces.
    /// </summary>
    /// <param name="raw">The raw text to normalize.</param>
    /// <returns>A normalized text structure.</returns>
    public static NormalizedText NormalizeTextNode(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return new NormalizedText(string.Empty, false, false, false);
        }

        var span = raw.AsSpan();
        var start = -1;
        var end = -1;

        for (var i = 0; i < span.Length; i++)
        {
            if (!char.IsWhiteSpace(span[i]))
            {
                start = i;
                break;
            }
        }

        if (start == -1)
        {
            return new NormalizedText(string.Empty, false, span.Length > 0, span.Length > 0);
        }

        for (var i = span.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(span[i]))
            {
                end = i;
                break;
            }
        }

        var leadingWhitespace = start > 0;
        var trailingWhitespace = end < span.Length - 1;

        var builder = new StringBuilder(end - start + 1);
        var previousWasWhitespace = false;

        for (var i = start; i <= end; i++)
        {
            var ch = span[i];
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWasWhitespace = false;
        }

        var content = builder.ToString();
        var hasContent = content.Length > 0;

        return new NormalizedText(content, hasContent, leadingWhitespace, trailingWhitespace);
    }

    /// <summary>
    /// Represents normalized text with information about whitespace.
    /// </summary>
    public readonly record struct NormalizedText(string Content, bool HasContent, bool LeadingWhitespace, bool TrailingWhitespace);

    /// <summary>
    /// Composes multiple child renderables into a single renderable.
    /// </summary>
    /// <param name="children">The child renderables to compose.</param>
    /// <returns>A single renderable representing all children.</returns>
    public static IRenderable ComposeChildContent(IReadOnlyList<IRenderable> children)
    {
        if (children.Count == 0)
        {
            return new Markup(string.Empty);
        }

        if (children.Count == 1)
        {
            return children[0];
        }

        return new Columns(children)
        {
            Expand = false,
            Padding = new Padding(0, 0, 0, 0),
        };
    }

    /// <summary>
    /// Parses a horizontal alignment value from a string.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <returns>The parsed horizontal alignment.</returns>
    public static HorizontalAlignment ParseHorizontalAlignment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return HorizontalAlignment.Left;
        }

        return value.ToLowerInvariant() switch
        {
            "center" or "centre" => HorizontalAlignment.Center,
            "right" or "end" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left,
        };
    }

    /// <summary>
    /// Parses a vertical alignment value from a string.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <returns>The parsed vertical alignment.</returns>
    public static VerticalAlignment ParseVerticalAlignment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return VerticalAlignment.Top;
        }

        return value.ToLowerInvariant() switch
        {
            "middle" or "center" or "centre" => VerticalAlignment.Middle,
            "bottom" or "end" => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Top,
        };
    }

    /// <summary>
    /// Parses an optional positive integer from a string.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <returns>The parsed integer value if successful and positive; otherwise, null.</returns>
    public static int? ParseOptionalPositiveInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0)
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Tries to get a boolean attribute value from a VNode.
    /// </summary>
    /// <param name="node">The VNode to get the attribute from.</param>
    /// <param name="name">The name of the attribute.</param>
    /// <param name="value">The parsed boolean value if successful.</param>
    /// <returns>True if the attribute was found and parsed successfully; otherwise, false.</returns>
    public static bool TryGetBoolAttribute(VNode node, string name, out bool value)
    {
        var raw = GetAttribute(node, name);
        if (!string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Tries to get an integer attribute value from a VNode.
    /// </summary>
    /// <param name="node">The VNode to get the attribute from.</param>
    /// <param name="name">The name of the attribute.</param>
    /// <param name="fallback">The fallback value if the attribute is not found or invalid.</param>
    /// <returns>The parsed integer value, or the fallback value.</returns>
    public static int TryGetIntAttribute(VNode node, string name, int fallback)
    {
        var raw = GetAttribute(node, name);
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    /// <summary>
    /// Collects all inner text content from a VNode and its children.
    /// </summary>
    /// <param name="node">The VNode to collect text from.</param>
    /// <returns>The collected inner text, or null if no text is found.</returns>
    public static string? CollectInnerText(VNode node)
    {
        if (node is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        AppendInnerText(node, builder);
        var value = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void AppendInnerText(VNode node, StringBuilder builder)
    {
        if (node.Kind == VNodeKind.Text)
        {
            if (!string.IsNullOrEmpty(node.Text))
            {
                builder.Append(node.Text);
            }

            return;
        }

        foreach (var child in node.Children)
        {
            AppendInnerText(child, builder);
        }
    }

    /// <summary>
    /// Enumerates class names from a space-separated class attribute value.
    /// </summary>
    /// <param name="raw">The raw class attribute value.</param>
    /// <returns>An enumerable of individual class names.</returns>
    public static IEnumerable<string> EnumerateClassNames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var parts = raw.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            yield return part;
        }
    }

    /// <summary>
    /// Checks if a VNode has a specific CSS class.
    /// </summary>
    /// <param name="node">The VNode to check.</param>
    /// <param name="className">The class name to look for.</param>
    /// <returns>True if the node has the specified class; otherwise, false.</returns>
    public static bool HasClass(VNode node, string className)
    {
        if (!node.TryGetAttributeValue<string>("class", out var classes))
        {
            return false;
        }

        return EnumerateClassNames(classes).Any(token => string.Equals(token, className, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tries to parse a padding value from a string.
    /// </summary>
    /// <param name="raw">The string value to parse.</param>
    /// <param name="padding">The parsed padding value if successful.</param>
    /// <returns>True if parsing was successful; otherwise, false.</returns>
    public static bool TryParsePadding(string? raw, out Padding padding)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            padding = new Padding(0, 0, 0, 0);
            return false;
        }

        var parts = raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = parts
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? Math.Max(number, 0) : 0)
            .Take(4)
            .ToArray();

        padding = values.Length switch
        {
            0 => new Padding(0, 0, 0, 0),
            1 => new Padding(values[0], values[0], values[0], values[0]),
            2 => new Padding(values[0], values[1], values[0], values[1]),
            3 => new Padding(values[0], values[1], values[2], values[1]),
            4 => new Padding(values[0], values[1], values[2], values[3]),
            _ => new Padding(0, 0, 0, 0),
        };

        return true;
    }

    /// <summary>
    /// Tries to parse a positive integer from a string.
    /// </summary>
    /// <param name="raw">The string value to parse.</param>
    /// <param name="result">The parsed integer value if successful.</param>
    /// <returns>True if parsing was successful and the value is positive; otherwise, false.</returns>
    public static bool TryParsePositiveInt(string? raw, out int result)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            result = value;
            return true;
        }

        result = default;
        return false;
    }
    /// <summary>
    /// Tries to parse a positive double from a string.
    /// </summary>
    /// <param name="raw">The string value to parse.</param>
    /// <param name="result">The parsed double value if successful.</param>
    /// <returns>True if parsing was successful and the value is positive; otherwise, false.</returns>
    public static bool TryParsePositiveDouble(string? raw, out double result)
    {
        if (double.TryParse(raw, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            result = value;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Determines if a VNode should be rendered as a block element.
    /// </summary>
    /// <param name="node">The VNode to check.</param>
    /// <returns>True if the node should be rendered as a block element; otherwise, false.</returns>
    public static bool ShouldBeBlock(VNode node)
    {
        // Check for explicit data-display attribute
        if (node.TryGetAttributeValue<string>("data-display", out var display))
        {
            if (string.Equals(display, "block", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(display, "inline", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Default block elements based on tag name
        if (node.TagName is not null)
        {
            return node.TagName.ToLowerInvariant() switch
            {
                // Block-level HTML elements
                "div" => true,
                "p" => true,
                "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => true,
                "panel" => true,
                "table" => true,
                "ul" or "ol" => true,
                "pre" => true,
                "blockquote" => true,

                // Inline elements
                "span" => false,
                "strong" or "b" => false,
                "em" or "i" => false,
                "code" => false,
                "a" => false,
                "mark" => false,

                // Default to inline for unknown elements
                _ => false,
            };
        }

        // Text nodes are inline
        return false;
    }
}
