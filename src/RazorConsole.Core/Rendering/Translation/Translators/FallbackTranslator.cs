// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class FallbackTranslator : ITranslationMiddleware
{
    private const int ChildPreviewCount = 5;

    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        return BuildPanel(node);
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element;

    private static Panel BuildPanel(VNode node)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"[bold]Kind:[/] {Markup.Escape(node.Kind.ToString())}");
        builder.AppendLine($"[bold]Tag:[/] {Markup.Escape(node.TagName ?? "<unknown>")}");
        builder.AppendLine($"[bold]Key:[/] {Markup.Escape(node.Key ?? "<none>")}");
        builder.AppendLine();

        AppendAttributes(node, builder);
        builder.AppendLine();
        AppendEvents(node, builder);
        builder.AppendLine();
        AppendChildren(node, builder);

        var panel = new Panel(new Markup(builder.ToString()))
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0),
        };

        var headerTag = node.TagName ?? "<unknown>";
        panel.Header = new PanelHeader($"[red]Untranslated VDOM[/] • {Markup.Escape(headerTag)}");
        panel.BorderStyle(Style.Parse("red"));
        return panel;
    }

    private static void AppendAttributes(VNode node, StringBuilder builder)
    {
        builder.AppendLine($"[bold]Attributes ({node.Attributes.Count}):[/]");

        if (node.Attributes.Count == 0)
        {
            builder.AppendLine("  [grey](none)[/]");
            return;
        }

        foreach (var attribute in node
            .Attributes
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = Markup.Escape(attribute.Key);
            var value = Markup.Escape(attribute.Value as string ?? "null");
            builder.AppendLine($"  • [cyan]{key}[/] = [yellow]{value}[/]");
        }
    }

    private static void AppendEvents(VNode node, StringBuilder builder)
    {
        var events = node.Events;
        builder.AppendLine($"[bold]Events ({events.Count}):[/]");

        if (events.Count == 0)
        {
            builder.AppendLine("  [grey](none)[/]");
            return;
        }

        foreach (var @event in events.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("  • [green]");
            builder.Append(Markup.Escape(@event.Name));
            builder.Append("[/] — handler ");
            builder.Append(@event.HandlerId);
            builder.Append(", preventDefault=");
            builder.Append(@event.Options.PreventDefault ? "true" : "false");
            builder.Append(", stopPropagation=");
            builder.AppendLine(@event.Options.StopPropagation ? "true" : "false");
        }
    }

    private static void AppendChildren(VNode node, StringBuilder builder)
    {
        builder.AppendLine($"[bold]Children ({node.Children.Count}):[/]");

        if (node.Children.Count == 0)
        {
            builder.AppendLine("  [grey](none)[/]");
            return;
        }

        var index = 0;
        foreach (var child in node.Children.Take(ChildPreviewCount))
        {
            builder.Append("  • ");
            builder.AppendLine(DescribeChild(child, ++index));
        }

        if (node.Children.Count > ChildPreviewCount)
        {
            builder.Append("  [grey]… ");
            builder.Append(node.Children.Count - ChildPreviewCount);
            builder.AppendLine(" more child node(s)[/]");
        }
    }

    private static string DescribeChild(VNode child, int position)
    {
        var summary = child.Kind switch
        {
            VNodeKind.Element => $"[bold]Element[/] tag=[yellow]{Markup.Escape(child.TagName ?? "<unknown>")}[/] attrs={child.Attributes.Count} children={child.Children.Count}",
            VNodeKind.Text => $"[bold]Text[/] \"{Markup.Escape(TrimText(child.Text))}\"",
            VNodeKind.Component => "[bold]Component[/]",
            VNodeKind.Region => "[bold]Region[/]",
            _ => "[bold]Unknown[/]",
        };

        return $"#{position} {summary}";
    }

    private static string TrimText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.ReplaceLineEndings(" ").Trim();
        if (normalized.Length > 80)
        {
            return string.Concat(normalized.AsSpan(0, 77), "…");
        }

        return normalized;
    }
}
