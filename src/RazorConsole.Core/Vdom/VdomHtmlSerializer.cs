// Copyright (c) RazorConsole. All rights reserved.

using System.Text;

namespace RazorConsole.Core.Vdom;

internal static class VdomHtmlSerializer
{
    public static string Serialize(VNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        Append(node, builder);
        return builder.ToString();
    }

    private static void Append(VNode node, StringBuilder builder)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        switch (node.Kind)
        {
            case VNodeKind.Text:
                builder.Append(System.Net.WebUtility.HtmlEncode(node.Text));
                break;
            case VNodeKind.Element:
                var tagName = node.TagName ?? string.Empty;
                builder.Append('<').Append(tagName);
                foreach (var pair in node.Attributes)
                {
                    if (pair.Value is null)
                    {
                        continue;
                    }

                    builder.Append(' ').Append(pair.Key).Append('=')
                        .Append('"')
                        .Append(System.Net.WebUtility.HtmlEncode(pair.Value?.ToString() ?? ""))
                        .Append('"');
                }

                builder.Append('>');
                foreach (var child in node.Children)
                {
                    Append(child, builder);
                }

                builder.Append("</").Append(tagName).Append('>');
                break;
        }
    }
}
