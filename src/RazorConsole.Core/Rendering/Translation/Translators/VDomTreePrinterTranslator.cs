// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

internal sealed class VDomTreePrinterTranslator : ITranslationMiddleware
{
    private const string ENABLE_COMPONENT_ROOT_FLAG = "RC_PRINT_VDOM_TREE";
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly List<Text> _frames = new List<Text>();

    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (Environment.GetEnvironmentVariable(ENABLE_COMPONENT_ROOT_FLAG)?.ToLowerInvariant() != "true")
        {
            return next(node);
        }

        _semaphore.Wait();

        var builder = new StringBuilder();
        builder.Append(node.Key);

        AppendNode(node, builder, string.Empty, true);
        builder.AppendLine();
        var dump = builder.ToString();

        var textRenderable = new Text(dump);

        _frames.Add(textRenderable);

        var row = new Rows(_frames.Select((i, frame) =>
        {
            return new Panel(i)
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader($"Frame {frame + 1}", Justify.Center),
                Padding = new Padding(1, 1, 1, 1),
                Expand = true
            };
        }));

        var result = new Panel(row)
        {
            Border = BoxBorder.None,
            Expand = true
        };

        _semaphore.Release();

        return result;
    }

    private static void AppendNode(VNode node, StringBuilder builder, string indent, bool isLast)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (indent.Length > 0)
        {
            builder.Append(indent);
            builder.Append(isLast ? "└── " : "├── ");
        }
        else
        {
            builder.Append("• ");
        }

        builder.Append(DescribeNode(node));
        builder.AppendLine();

        if (node.Children.Count == 0)
        {
            return;
        }

        var nextIndent = indent + (indent.Length > 0 ? (isLast ? "    " : "│   ") : "   ");
        for (var i = 0; i < node.Children.Count; i++)
        {
            AppendNode(node.Children[i], builder, nextIndent, i == node.Children.Count - 1);
        }
    }

    private static string DescribeNode(VNode node)
    {
        var summary = new StringBuilder();
        summary.Append(node.Kind);

        switch (node.Kind)
        {
            case VNodeKind.Element:
                summary.Append(' ');
                summary.Append(node.TagName ?? "<unknown>");
                summary.Append(" key=");
                summary.Append(node.Key ?? "<null>");
                summary.Append(" text=");
                summary.Append(node.Text == null ? "<null?" : $"'{node.Text}'");
                summary.Append(" v-id=");
                summary.Append(node.ID[..4]);
                AppendAttributes(summary, node.Attributes);
                AppendEvents(summary, node.Events);
                break;
            case VNodeKind.Text:
                var text = node.Text ?? string.Empty;
                text = text.Replace("\r", "\\r", StringComparison.Ordinal)
                           .Replace("\n", "\\n", StringComparison.Ordinal);
                const int maxLength = 60;
                if (text.Length > maxLength)
                {
                    text = text[..maxLength] + "…";
                }

                summary.Append(" \"");
                summary.Append(text);
                summary.Append('"');
                break;
            case VNodeKind.Component:
                if (!string.IsNullOrWhiteSpace(node.Key))
                {
                    summary.Append(" key=");
                    summary.Append(node.Key);
                }

                AppendAttributes(summary, node.Attributes);
                AppendEvents(summary, node.Events);
                // TODO
                // include children
                break;
            case VNodeKind.Region:
                break;
        }

        return summary.ToString();
    }

    private static void AppendAttributes(StringBuilder builder, IReadOnlyDictionary<string, object?> attributes)
    {
        if (attributes.Count == 0)
        {
            return;
        }

        builder.Append(" attrs[");
        var first = true;
        foreach (var attribute in attributes
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(attribute.Key);
            builder.Append('=');
            builder.Append(attribute.Value ?? "<null>");
        }

        builder.Append(']');
    }

    private static void AppendEvents(StringBuilder builder, IReadOnlyCollection<VNodeEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        builder.Append(" events[");
        var first = true;
        foreach (var @event in events.OrderBy(static evt => evt.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(@event.Name);
        }

        builder.Append(']');
    }
}

