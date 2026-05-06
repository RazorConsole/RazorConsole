// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Utilities;
using RazorConsole.Core.Vdom;
using Spectre.Console;

namespace RazorConsole.Core.Layout;

public sealed class WidgetTranslationContext
{
    private readonly IReadOnlyList<ITranslationMiddleware> _spectreFallbackMiddlewares;
    private readonly HashSet<IAnimatedConsoleRenderable> _animatedRenderables = [];

    public WidgetTranslationContext(IEnumerable<ITranslationMiddleware>? spectreFallbackMiddlewares = null)
    {
        _spectreFallbackMiddlewares = spectreFallbackMiddlewares?.ToArray() ?? [];
    }

    public IReadOnlyCollection<IAnimatedConsoleRenderable> AnimatedRenderables => _animatedRenderables;

    public void ClearAnimatedRenderables()
        => _animatedRenderables.Clear();

    public Widget Translate(VNode node)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        return node.Kind switch
        {
            VNodeKind.Text => new TextWidget(node.ID, string.IsNullOrWhiteSpace(node.Text) ? string.Empty : node.Text),
            VNodeKind.Element => TranslateElement(node),
            _ => TranslateContainer(node),
        };
    }

    private Widget TranslateElement(VNode node)
    {
        var zIndex = TryGetIntAttribute(node, "z-index", 0);
        var className = GetAttribute(node, "class");

        if (string.Equals(node.TagName, "table", StringComparison.OrdinalIgnoreCase))
        {
            return CreateTableWidget(node, zIndex);
        }

        if (IsTruthy(GetAttribute(node, "data-text")))
        {
            return new TextWidget(
                node.ID,
                GetAttribute(node, "data-content"),
                TryParseStyle(GetAttribute(node, "data-style")),
                node.Key,
                node.Attributes,
                zIndex);
        }

        if (IsTruthy(GetAttribute(node, "data-text-input")))
        {
            return CreateTextInputWidget(node, zIndex);
        }

        if (string.Equals(className, "figlet", StringComparison.OrdinalIgnoreCase))
        {
            return new SpectreWidget(
                node.ID,
                CreateFigletRenderable(node),
                node.Key,
                node.Attributes,
                zIndex);
        }

        if (string.Equals(node.TagName, "ul", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.TagName, "ol", StringComparison.OrdinalIgnoreCase))
        {
            return CreateHtmlListWidget(node, zIndex);
        }

        if (TryCreateSpectreFallbackWidget(node, zIndex) is { } fallbackWidget)
        {
            return fallbackWidget;
        }

        var children = TranslateChildren(node);

        if (string.Equals(node.TagName, "scrollable", StringComparison.OrdinalIgnoreCase))
        {
            var child = ComposeChildren(node, children);
            var scrollbarNode = node.Children.FirstOrDefault(IsScrollbarNode);
            if (scrollbarNode is null)
            {
                return new StackWidget(node.ID, children, attributes: node.Attributes, zIndex: zIndex);
            }

            return new ScrollableWidget(
                node.ID,
                child,
                TryGetIntAttribute(node, "data-items-count", 0),
                TryGetIntAttribute(node, "data-offset", 0),
                Math.Max(1, TryGetIntAttribute(node, "data-page-size", 1)),
                IsTruthy(GetAttribute(node, "data-enable-embedded")),
                ParseChar(GetAttribute(scrollbarNode, "data-track-char"), '│'),
                ParseChar(GetAttribute(scrollbarNode, "data-thumb-char"), '█'),
                TryParseHexStyle(GetAttribute(scrollbarNode, "data-track-color")),
                TryParseHexStyle(GetAttribute(scrollbarNode, "data-thumb-color")),
                Math.Max(1, TryGetIntAttribute(scrollbarNode, "data-min-thumb-height", 1)),
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        if (string.Equals(className, "rows", StringComparison.OrdinalIgnoreCase))
        {
            return new StackWidget(node.ID, children, attributes: node.Attributes, zIndex: zIndex);
        }

        if (string.Equals(className, "columns", StringComparison.OrdinalIgnoreCase))
        {
            return new RowWidget(node.ID, children, gap: 1, attributes: node.Attributes, zIndex: zIndex);
        }

        if (string.Equals(className, "padder", StringComparison.OrdinalIgnoreCase))
        {
            var child = ComposeChildren(node, children);
            var padding = ParsePadding(GetAttribute(node, "data-padding"));
            return new PaddingWidget(
                node.ID,
                child,
                padding.Left,
                padding.Top,
                padding.Right,
                padding.Bottom,
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        if (string.Equals(className, "align", StringComparison.OrdinalIgnoreCase))
        {
            var child = ComposeChildren(node, children);
            return new AlignWidget(
                node.ID,
                child,
                ParseHorizontalAlignment(GetAttribute(node, "data-horizontal")),
                ParseVerticalAlignment(GetAttribute(node, "data-vertical")),
                TryParsePositiveInt(GetAttribute(node, "data-width")),
                TryParsePositiveInt(GetAttribute(node, "data-height")),
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        if (string.Equals(className, "panel", StringComparison.OrdinalIgnoreCase))
        {
            var child = ComposeChildren(node, children);
            var padding = ParsePadding(GetAttribute(node, "data-padding"), fallback: (1, 0, 1, 0));
            return new PanelWidget(
                node.ID,
                child,
                title: GetAttribute(node, "data-header"),
                border: ParsePanelBorder(GetAttribute(node, "data-border")),
                paddingLeft: padding.Left,
                paddingTop: padding.Top,
                paddingRight: padding.Right,
                paddingBottom: padding.Bottom,
                width: TryParsePositiveInt(GetAttribute(node, "data-width")),
                height: TryParsePositiveInt(GetAttribute(node, "data-height")),
                expand: IsTruthy(GetAttribute(node, "data-expand")),
                borderStyle: TryParseStyle(GetAttribute(node, "data-border-color")),
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        return TranslateContainer(node);
    }

    private Widget TranslateContainer(VNode node)
        => new StackWidget(node.ID, TranslateChildren(node), attributes: node.Attributes, zIndex: TryGetIntAttribute(node, "z-index", 0));

    private Widget? TryCreateSpectreFallbackWidget(VNode node, int zIndex)
    {
        if (_spectreFallbackMiddlewares.Count == 0 || !ShouldUseSpectreFallback(node))
        {
            return null;
        }

        try
        {
            var context = new TranslationContext(_spectreFallbackMiddlewares);
            var renderable = context.Translate(node);
            if (context.CollectedOverlays.Count > 0)
            {
                renderable = new OverlayRenderable(renderable, context.CollectedOverlays);
            }

            foreach (var animated in context.AnimatedRenderables)
            {
                _animatedRenderables.Add(animated);
            }

            return new SpectreWidget(
                node.ID,
                renderable,
                node.Key,
                node.Attributes,
                zIndex);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool ShouldUseSpectreFallback(VNode node)
    {
        if (node.Kind != VNodeKind.Element)
        {
            return false;
        }

        var className = GetAttribute(node, "class");
        if (string.Equals(className, "syntax-highlighter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(className, "flexbox", StringComparison.OrdinalIgnoreCase)
            || string.Equals(className, "grid", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsTruthy(GetAttribute(node, "data-canvas")) || node.Attributes.ContainsKey("data-spinner"))
        {
            return true;
        }

        if (IsPlainDivWrapper(node) && node.Children.Any(ContainsAbsolutePositionedElement))
        {
            return true;
        }

        if (IsHtmlElementRequiringInlineOrBlockSemantics(node))
        {
            return true;
        }

        return string.Equals(node.TagName, "stepchart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.TagName, "barchart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.TagName, "breakdownchart", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtmlElementRequiringInlineOrBlockSemantics(VNode node)
    {
        if (!string.IsNullOrWhiteSpace(GetAttribute(node, "class"))
            || string.Equals(GetAttribute(node, "position"), "absolute", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (node.Attributes.ContainsKey(IVNodeIdAccessor.HookAttributeName)
            || node.Attributes.ContainsKey("data-focus-key"))
        {
            return false;
        }

        return node.TagName?.ToLowerInvariant() switch
        {
            "p" or "strong" or "em" or "mark" or "code" or "abbr" or "sup" or "sub" or "a" or "q" or "cite" or "ul" or "ol" or "li" => true,
            _ => false,
        };
    }

    private static bool IsPlainDivWrapper(VNode node)
        => string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(GetAttribute(node, "class"))
            && string.IsNullOrWhiteSpace(GetAttribute(node, "position"))
            && !node.Attributes.ContainsKey(IVNodeIdAccessor.HookAttributeName)
            && !node.Attributes.ContainsKey("data-focus-key");

    private static bool ContainsAbsolutePositionedElement(VNode node)
    {
        if (node.Kind == VNodeKind.Element
            && string.Equals(GetAttribute(node, "position"), "absolute", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return node.Children.Any(ContainsAbsolutePositionedElement);
    }

    private IReadOnlyList<Widget> TranslateChildren(VNode node)
        => node.Children
            .Where(child => child.Kind != VNodeKind.Text || !string.IsNullOrWhiteSpace(child.Text))
            .Where(child => !IsScrollbarNode(child))
            .Select(Translate)
            .ToArray();

    private Widget ComposeChildren(VNode owner, IReadOnlyList<Widget> children)
        => children.Count switch
        {
            0 => new TextWidget(owner.ID + "-content", string.Empty),
            1 => children[0],
            _ => new StackWidget(owner.ID + "-content", children),
        };

    private Widget CreateTextInputWidget(VNode node, int zIndex)
    {
        var panel = FindFirstDescendant(node, IsPanelNode);
        var textNodes = EnumerateDescendants(node)
            .Where(candidate => IsTruthy(GetAttribute(candidate, "data-text")))
            .ToArray();
        var label = textNodes.ElementAtOrDefault(0);
        var display = textNodes.ElementAtOrDefault(1) ?? label;

        var children = new List<Widget>();
        if (label is not null && display is not null && !ReferenceEquals(label, display))
        {
            children.Add(new TextWidget(
                label.ID,
                GetAttribute(label, "data-content"),
                TryParseStyle(GetAttribute(label, "data-style")),
                label.Key,
                label.Attributes,
                zIndex));
        }

        if (display is not null)
        {
            var displayText = new TextWidget(
                display.ID,
                GetAttribute(display, "data-content"),
                TryParseStyle(GetAttribute(display, "data-style")),
                display.Key,
                display.Attributes,
                zIndex);
            var contentPadding = FindNearestAncestor(node, display, candidate => string.Equals(GetAttribute(candidate, "class"), "padder", StringComparison.OrdinalIgnoreCase)) is { } padder
                ? ParsePadding(GetAttribute(padder, "data-padding"))
                : (Left: 0, Top: 0, Right: 0, Bottom: 0);
            children.Add(new PaddingWidget(
                display.ID + "-padding",
                displayText,
                contentPadding.Left,
                contentPadding.Top,
                contentPadding.Right,
                contentPadding.Bottom,
                attributes: display.Attributes,
                zIndex: zIndex));
        }

        var content = children.Count switch
        {
            0 => new TextWidget(node.ID + "-content", string.Empty),
            1 => children[0],
            _ => new RowWidget(node.ID + "-content", children, attributes: node.Attributes, zIndex: zIndex),
        };

        if (panel is null)
        {
            return new StackWidget(node.ID, [content], attributes: node.Attributes, zIndex: zIndex);
        }

        var padding = ParsePadding(GetAttribute(panel, "data-padding"));
        return new PanelWidget(
            panel.ID,
            content,
            title: GetAttribute(panel, "data-header"),
            border: ParsePanelBorder(GetAttribute(panel, "data-border")),
            paddingLeft: padding.Left,
            paddingTop: padding.Top,
            paddingRight: padding.Right,
            paddingBottom: padding.Bottom,
            width: TryParsePositiveInt(GetAttribute(panel, "data-width")),
            height: TryParsePositiveInt(GetAttribute(panel, "data-height")),
            expand: IsTruthy(GetAttribute(panel, "data-expand")),
            borderStyle: TryParseStyle(GetAttribute(panel, "data-border-color")),
            attributes: node.Attributes,
            zIndex: zIndex);
    }

    private Widget CreateHtmlListWidget(VNode node, int zIndex)
    {
        var isOrdered = string.Equals(node.TagName, "ol", StringComparison.OrdinalIgnoreCase);
        var start = TryGetIntAttribute(node, "start", 1);
        var rows = node.Children
            .Where(child => child.Kind == VNodeKind.Element && string.Equals(child.TagName, "li", StringComparison.OrdinalIgnoreCase))
            .Select((child, index) => new TextWidget(
                child.ID,
                $"{(isOrdered ? $"{start + index}. " : "• ")}{GetPlainText(child)}",
                key: child.Key,
                attributes: child.Attributes,
                zIndex: zIndex))
            .Cast<Widget>()
            .ToArray();

        return new StackWidget(node.ID, rows, attributes: node.Attributes, zIndex: zIndex);
    }

    private static string GetPlainText(VNode node)
    {
        if (node.Kind == VNodeKind.Text)
        {
            return node.Text;
        }

        if (IsTruthy(GetAttribute(node, "data-text")))
        {
            return GetAttribute(node, "data-content") ?? string.Empty;
        }

        return string.Concat(node.Children.Select(GetPlainText));
    }

    private static string? GetAttribute(VNode node, string name)
        => node.Attributes.TryGetValue(name, out var value) ? value : null;

    private static bool IsPanelNode(VNode node)
        => node.Kind == VNodeKind.Element
            && string.Equals(GetAttribute(node, "class"), "panel", StringComparison.OrdinalIgnoreCase);

    private static VNode? FindFirstDescendant(VNode node, Func<VNode, bool> predicate)
        => EnumerateDescendants(node).FirstOrDefault(predicate);

    private static IEnumerable<VNode> EnumerateDescendants(VNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;

            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static VNode? FindNearestAncestor(VNode root, VNode target, Func<VNode, bool> predicate)
    {
        var path = new List<VNode>();
        return TryFindPath(root, target, path)
            ? path.AsEnumerable().Reverse().Skip(1).FirstOrDefault(predicate)
            : null;
    }

    private static bool TryFindPath(VNode current, VNode target, List<VNode> path)
    {
        path.Add(current);
        if (ReferenceEquals(current, target))
        {
            return true;
        }

        foreach (var child in current.Children)
        {
            if (TryFindPath(child, target, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private static int TryGetIntAttribute(VNode node, string name, int fallback)
        => int.TryParse(GetAttribute(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int? TryParsePositiveInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static (int Left, int Top, int Right, int Bottom) ParsePadding(
        string? value,
        (int Left, int Top, int Right, int Bottom)? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback ?? (0, 0, 0, 0);
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var all) && all >= 0)
        {
            return (all, all, all, all);
        }

        if (parts.Length == 4
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var top)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var right)
            && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bottom)
            && left >= 0
            && top >= 0
            && right >= 0
            && bottom >= 0)
        {
            return (left, top, right, bottom);
        }

        return fallback ?? (0, 0, 0, 0);
    }

    private static HorizontalAlignment ParseHorizontalAlignment(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? HorizontalAlignment.Left
            : value.ToLowerInvariant() switch
            {
                "center" or "centre" => HorizontalAlignment.Center,
                "right" or "end" => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left,
            };

    private static VerticalAlignment ParseVerticalAlignment(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? VerticalAlignment.Top
            : value.ToLowerInvariant() switch
            {
                "middle" or "center" or "centre" => VerticalAlignment.Middle,
                "bottom" or "end" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top,
            };

    private static PanelBorderStyle ParsePanelBorder(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? PanelBorderStyle.Square
            : value.ToLowerInvariant() switch
            {
                "rounded" => PanelBorderStyle.Rounded,
                "double" => PanelBorderStyle.Double,
                "heavy" => PanelBorderStyle.Heavy,
                "ascii" => PanelBorderStyle.Ascii,
                "none" => PanelBorderStyle.None,
                _ => PanelBorderStyle.Square,
            };

    private static Style? TryParseStyle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Style.Parse(value);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsTruthy(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, string.Empty, StringComparison.Ordinal);

    private static FigletText CreateFigletRenderable(VNode node)
    {
        var content = GetAttribute(node, "data-content") ?? string.Empty;
        var style = TryParseStyle(GetAttribute(node, "data-style")) ?? new Style(Color.Default);
        var justify = ParseJustify(GetAttribute(node, "data-justify"));
        var fontKey = GetAttribute(node, "data-font");

        if (!string.IsNullOrWhiteSpace(fontKey) && FigletFontRegistry.TryGetOrLoad(fontKey, out var font))
        {
            return new FigletText(font, content)
            {
                Justification = justify,
                Color = style.Foreground,
            };
        }

        return new FigletText(content)
        {
            Justification = justify,
            Color = style.Foreground,
        };
    }

    private static Justify ParseJustify(string? value)
        => value?.ToLowerInvariant() switch
        {
            "center" or "centre" => Justify.Center,
            "right" => Justify.Right,
            _ => Justify.Left,
        };

    private TableWidget CreateTableWidget(VNode node, int zIndex)
    {
        var headerRows = GetSectionRows(node, "thead").Select(row => CreateTableWidgetRow(row, isHeader: true)).ToArray();
        var bodyRows = GetSectionRows(node, "tbody")
            .Concat(GetLooseRows(node))
            .Concat(GetSectionRows(node, "tfoot"))
            .Select(row => CreateTableWidgetRow(row, isHeader: false))
            .ToArray();

        if (headerRows.Length == 0 && bodyRows.Length == 0)
        {
            return new TableWidget(node.ID, [], [], attributes: node.Attributes, zIndex: zIndex);
        }

        return new TableWidget(
            node.ID,
            headerRows,
            bodyRows,
            ParseTableWidgetBorder(GetAttribute(node, "data-border")),
            IsTruthy(GetAttribute(node, "data-expand")),
            !string.Equals(GetAttribute(node, "data-show-headers"), "false", StringComparison.OrdinalIgnoreCase),
            TryParsePositiveInt(GetAttribute(node, "data-width")),
            TryParseHexStyle(GetAttribute(node, "data-border-color")),
            node.Key,
            node.Attributes,
            zIndex);
    }

    private TableWidgetRow CreateTableWidgetRow(VNode row, bool isHeader)
    {
        var cells = row.Children
            .Where(child => child.Kind == VNodeKind.Element
                && (string.Equals(child.TagName, "td", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(child.TagName, "th", StringComparison.OrdinalIgnoreCase)))
            .Select(cell => CreateTableWidgetCell(cell, isHeader || string.Equals(cell.TagName, "th", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return new TableWidgetRow(cells);
    }

    private TableWidgetCell CreateTableWidgetCell(VNode cell, bool isHeader)
    {
        var child = ComposeChildren(cell, TranslateChildren(cell));
        var padding = ParsePadding(GetAttribute(cell, "data-padding"), fallback: (1, 0, 1, 0));
        return new TableWidgetCell(
            cell.ID,
            child,
            ParseTableCellAlignment(GetAttribute(cell, "data-align"), isHeader),
            padding.Left,
            padding.Top,
            padding.Right,
            padding.Bottom,
            TryParsePositiveInt(GetAttribute(cell, "data-width")));
    }

    private static TableWidgetBorderStyle ParseTableWidgetBorder(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? TableWidgetBorderStyle.Rounded
            : value.ToLowerInvariant() switch
            {
                "none" => TableWidgetBorderStyle.None,
                "ascii" => TableWidgetBorderStyle.Ascii,
                "simple" or "square" => TableWidgetBorderStyle.Square,
                _ => TableWidgetBorderStyle.Rounded,
            };

    private static HorizontalAlignment ParseTableCellAlignment(string? value, bool isHeader)
        => string.IsNullOrWhiteSpace(value)
            ? isHeader ? HorizontalAlignment.Center : HorizontalAlignment.Left
            : ParseHorizontalAlignment(value);

    private static IEnumerable<VNode> GetSectionRows(VNode table, string sectionName)
        => table.Children
            .Where(child => child.Kind == VNodeKind.Element && string.Equals(child.TagName, sectionName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(section => section.Children)
            .Where(child => child.Kind == VNodeKind.Element && string.Equals(child.TagName, "tr", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<VNode> GetLooseRows(VNode table)
        => table.Children
            .Where(child => child.Kind == VNodeKind.Element && string.Equals(child.TagName, "tr", StringComparison.OrdinalIgnoreCase));

    private static char ParseChar(string? value, char fallback)
        => char.TryParse(value, out var parsed) ? parsed : fallback;

    private static Style? TryParseHexStyle(string? value)
        => !string.IsNullOrWhiteSpace(value) && Color.TryFromHex(value, out var color)
            ? new Style(color)
            : null;

    private static bool IsScrollbarNode(VNode node)
        => node.Kind == VNodeKind.Element
            && node.Attributes.TryGetValue("data-scrollbar", out var value)
            && IsTruthy(value);
}
