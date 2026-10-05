// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Rendering.Syntax;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Utilities;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

public sealed class WidgetTranslationContext
{
    private readonly IReadOnlyList<ITranslationMiddleware> _spectreFallbackMiddlewares;
    private readonly ScrollableLayoutCoordinator? _scrollableLayoutCoordinator;
    private readonly SyntaxHighlightingService? _syntaxHighlightingService;
    private readonly HashSet<IAnimatedConsoleRenderable> _animatedRenderables = [];

    public WidgetTranslationContext(
        IEnumerable<ITranslationMiddleware>? spectreFallbackMiddlewares = null,
        ScrollableLayoutCoordinator? scrollableLayoutCoordinator = null,
        SyntaxHighlightingService? syntaxHighlightingService = null)
    {
        _spectreFallbackMiddlewares = spectreFallbackMiddlewares?.ToArray() ?? [];
        _scrollableLayoutCoordinator = scrollableLayoutCoordinator;
        _syntaxHighlightingService = syntaxHighlightingService;
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

        if (IsHeadingTag(node.TagName))
        {
            return CreateHeadingWidget(node, zIndex);
        }

        if (string.Equals(node.TagName, "pre", StringComparison.OrdinalIgnoreCase))
        {
            return CreateCodeBlockWidget(node, zIndex);
        }

        if (string.Equals(node.TagName, "blockquote", StringComparison.OrdinalIgnoreCase))
        {
            var quoteChild = ComposeChildren(node, TranslateChildren(node));
            return new BoxWidget(
                node.ID,
                quoteChild,
                paddingLeft: 2,
                key: node.Key,
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        if (string.Equals(node.TagName, "hr", StringComparison.OrdinalIgnoreCase))
        {
            return new RuleWidget(node.ID, new Style(Color.Grey), node.Key, node.Attributes, zIndex);
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

        if (string.Equals(node.TagName, "view-height-scrollable", StringComparison.OrdinalIgnoreCase))
        {
            var child = ComposeChildren(node, children);
            var scrollbarNode = node.Children.FirstOrDefault(IsScrollbarNode);
            var linesToRender = TryGetIntAttribute(node, "data-lines-to-render", 1);

            var offset = TryGetIntAttribute(node, "data-offset", 0);
            var pageSize = Math.Max(1, linesToRender);
            var enableEmbedded = IsTruthy(GetAttribute(node, "data-enable-embedded"));
            var trackChar = scrollbarNode is null ? '│' : ParseChar(GetAttribute(scrollbarNode, "data-track-char"), '│');
            var thumbChar = scrollbarNode is null ? '█' : ParseChar(GetAttribute(scrollbarNode, "data-thumb-char"), '█');
            var trackStyle = scrollbarNode is null ? null : TryParseHexStyle(GetAttribute(scrollbarNode, "data-track-color"));
            var thumbStyle = scrollbarNode is null ? null : TryParseHexStyle(GetAttribute(scrollbarNode, "data-thumb-color"));
            var minThumbHeight = scrollbarNode is null ? 1 : Math.Max(1, TryGetIntAttribute(scrollbarNode, "data-min-thumb-height", 1));
            var showScrollbar = scrollbarNode is not null;
            var autoPageSize = linesToRender == 0;
            var scrollId = GetAttribute(node, "data-scroll-id");

            // When the scrollable's single child is a bordered box (e.g. a Panel with a Title),
            // peel the box out so it becomes the OUTER, fixed frame and the scrollable wraps just
            // its inner content. Otherwise ScrollableWidget would offset/clip the whole box as one
            // unit, dragging the border and title along with the scroll offset and clipping them
            // out of view. A borderless box (plain padding/margin wrapper) has no visible frame to
            // keep fixed, so it is left exactly as-is.
            if (child is BoxWidget frame && frame.HasBorder)
            {
                var innerScrollable = new ScrollableWidget(
                    node.ID,
                    frame.Child,
                    itemsCount: 0,
                    offset: offset,
                    pageSize: pageSize,
                    enableEmbedded: enableEmbedded,
                    trackChar: trackChar,
                    thumbChar: thumbChar,
                    trackStyle: trackStyle,
                    thumbStyle: thumbStyle,
                    minThumbHeight: minThumbHeight,
                    showScrollbar: showScrollbar,
                    cropLines: true,
                    autoPageSize: autoPageSize,
                    layoutCoordinator: _scrollableLayoutCoordinator,
                    scrollId: scrollId,
                    attributes: node.Attributes,
                    zIndex: zIndex);

                return frame.WithChild(innerScrollable);
            }

            return new ScrollableWidget(
                node.ID,
                child,
                itemsCount: 0,
                offset: offset,
                pageSize: pageSize,
                enableEmbedded: enableEmbedded,
                trackChar: trackChar,
                thumbChar: thumbChar,
                trackStyle: trackStyle,
                thumbStyle: thumbStyle,
                minThumbHeight: minThumbHeight,
                showScrollbar: showScrollbar,
                cropLines: true,
                autoPageSize: autoPageSize,
                layoutCoordinator: _scrollableLayoutCoordinator,
                scrollId: scrollId,
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        if (string.Equals(className, "rows", StringComparison.OrdinalIgnoreCase))
        {
            return new FlexWidget(
                node.ID,
                children,
                direction: FlexDirection.Column,
                expand: IsTruthy(GetAttribute(node, "data-expand")),
                fillWidth: IsTruthy(GetAttribute(node, "data-fill-width")),
                fillHeight: IsTruthy(GetAttribute(node, "data-fill-height")),
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        if (string.Equals(className, "columns", StringComparison.OrdinalIgnoreCase))
        {
            return new FlexWidget(
                node.ID,
                children,
                direction: FlexDirection.Row,
                align: FlexAlign.Stretch,
                gap: 1,
                expand: IsTruthy(GetAttribute(node, "data-expand")),
                fillWidth: IsTruthy(GetAttribute(node, "data-fill-width")),
                fillHeight: IsTruthy(GetAttribute(node, "data-fill-height")),
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        if (IsFlexNode(node, className))
        {
            return new FlexWidget(
                node.ID,
                children,
                direction: ParseEnum(GetAttribute(node, "data-direction"), FlexDirection.Row),
                justify: ParseEnum(GetAttribute(node, "data-justify"), FlexJustify.Start),
                align: ParseEnum(GetAttribute(node, "data-align"), FlexAlign.Start),
                wrap: ParseEnum(GetAttribute(node, "data-wrap"), FlexWrap.NoWrap),
                gap: Math.Max(0, TryGetIntAttribute(node, "data-gap", 0)),
                expand: IsTruthy(GetAttribute(node, "data-expand")),
                fillWidth: IsTruthy(GetAttribute(node, "data-fill-width")),
                fillHeight: IsTruthy(GetAttribute(node, "data-fill-height")),
                width: TryParsePositiveInt(GetAttribute(node, "data-width")),
                height: TryParsePositiveInt(GetAttribute(node, "data-height")),
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        if (string.Equals(className, "padder", StringComparison.OrdinalIgnoreCase))
        {
            var child = ComposeChildren(node, children);
            var padding = ParsePadding(GetAttribute(node, "data-padding"));
            var margin = ParsePadding(GetAttribute(node, "data-margin"));
            return new BoxWidget(
                node.ID,
                child,
                paddingLeft: padding.Left,
                paddingTop: padding.Top,
                paddingRight: padding.Right,
                paddingBottom: padding.Bottom,
                marginLeft: margin.Left,
                marginTop: margin.Top,
                marginRight: margin.Right,
                marginBottom: margin.Bottom,
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
            var margin = ParsePadding(GetAttribute(node, "data-margin"));
            return new BoxWidget(
                node.ID,
                child,
                title: GetAttribute(node, "data-header"),
                border: ParsePanelBorder(GetAttribute(node, "data-border")),
                borderTop: ParseOptionalBoxBorder(GetAttribute(node, "data-border-top")),
                borderRight: ParseOptionalBoxBorder(GetAttribute(node, "data-border-right")),
                borderBottom: ParseOptionalBoxBorder(GetAttribute(node, "data-border-bottom")),
                borderLeft: ParseOptionalBoxBorder(GetAttribute(node, "data-border-left")),
                paddingLeft: padding.Left,
                paddingTop: padding.Top,
                paddingRight: padding.Right,
                paddingBottom: padding.Bottom,
                marginLeft: margin.Left,
                marginTop: margin.Top,
                marginRight: margin.Right,
                marginBottom: margin.Bottom,
                width: TryParsePositiveInt(GetAttribute(node, "data-width")),
                height: TryParsePositiveInt(GetAttribute(node, "data-height")),
                expand: IsTruthy(GetAttribute(node, "data-expand")),
                fillWidth: IsTruthy(GetAttribute(node, "data-fill-width")),
                fillHeight: IsTruthy(GetAttribute(node, "data-fill-height")),
                borderStyle: TryParseStyle(GetAttribute(node, "data-border-color")),
                attributes: node.Attributes,
                zIndex: zIndex);
        }

        if (IsBoxNode(node, className))
        {
            var child = ComposeChildren(node, children);
            var padding = ParsePadding(GetAttribute(node, "data-padding"));
            var margin = ParsePadding(GetAttribute(node, "data-margin"));
            return new BoxWidget(
                node.ID,
                child,
                paddingLeft: padding.Left,
                paddingTop: padding.Top,
                paddingRight: padding.Right,
                paddingBottom: padding.Bottom,
                marginLeft: margin.Left,
                marginTop: margin.Top,
                marginRight: margin.Right,
                marginBottom: margin.Bottom,
                width: TryParsePositiveInt(GetAttribute(node, "data-width")),
                height: TryParsePositiveInt(GetAttribute(node, "data-height")),
                expand: IsTruthy(GetAttribute(node, "data-expand")),
                fillWidth: IsTruthy(GetAttribute(node, "data-fill-width")),
                fillHeight: IsTruthy(GetAttribute(node, "data-fill-height")),
                title: GetAttribute(node, "data-header"),
                border: ParseBoxBorder(GetAttribute(node, "data-border"), fallback: BoxBorderStyle.None),
                borderTop: ParseOptionalBoxBorder(GetAttribute(node, "data-border-top")),
                borderRight: ParseOptionalBoxBorder(GetAttribute(node, "data-border-right")),
                borderBottom: ParseOptionalBoxBorder(GetAttribute(node, "data-border-bottom")),
                borderLeft: ParseOptionalBoxBorder(GetAttribute(node, "data-border-left")),
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

    private static bool IsFlexNode(VNode node, string? className)
        => string.Equals(className, "flex", StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetAttribute(node, "data-layout"), "flex", StringComparison.OrdinalIgnoreCase);

    private static bool IsBoxNode(VNode node, string? className)
        => string.Equals(className, "box", StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetAttribute(node, "data-layout"), "box", StringComparison.OrdinalIgnoreCase);

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

    private Widget CreateHtmlListWidget(VNode node, int zIndex)
    {
        var isOrdered = string.Equals(node.TagName, "ol", StringComparison.OrdinalIgnoreCase);
        var start = TryGetIntAttribute(node, "start", 1);
        var items = node.Children
            .Where(child => child.Kind == VNodeKind.Element && string.Equals(child.TagName, "li", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var rows = items
            .Select((item, index) => CreateHtmlListItemWidget(item, isOrdered, start + index, zIndex))
            .Cast<Widget>()
            .ToArray();

        return new StackWidget(node.ID, rows, attributes: node.Attributes, zIndex: zIndex);
    }

    private HtmlListItemWidget CreateHtmlListItemWidget(VNode item, bool isOrdered, int ordinal, int zIndex)
    {
        var marker = new TextWidget(item.ID + "-marker", isOrdered ? $"{ordinal}. " : "• ");

        // Translate the item's children through the normal translation pipeline (instead of
        // flattening to plain text) so styled/nested content (e.g. Markup, nested lists,
        // multi-line text) renders correctly rather than being silently dropped or losing
        // styling.
        var contentChildren = TranslateChildren(item);
        var content = ComposeChildren(item, contentChildren);

        return new HtmlListItemWidget(
            item.ID,
            marker,
            content,
            key: item.Key,
            attributes: item.Attributes,
            zIndex: zIndex);
    }

    private Widget CreateHeadingWidget(VNode node, int zIndex)
    {
        var tagName = node.TagName?.ToLowerInvariant();
        var innerText = GetPlainText(node).Trim();
        if (string.IsNullOrWhiteSpace(innerText))
        {
            return new TextWidget(node.ID, string.Empty, key: node.Key, attributes: node.Attributes, zIndex: zIndex);
        }

        var prefix = GetHeadingPrefix(tagName);
        var style = GetHeadingStyle(tagName);
        return new TextWidget(node.ID, prefix + innerText, style, node.Key, node.Attributes, zIndex);
    }

    private static bool IsHeadingTag(string? tagName)
        => tagName?.ToLowerInvariant() switch
        {
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => true,
            _ => false,
        };

    private static string GetHeadingPrefix(string? tagName)
        => tagName switch
        {
            "h1" => "# ",
            "h2" => "## ",
            "h3" => "### ",
            "h4" => "#### ",
            "h5" => "##### ",
            "h6" => "###### ",
            _ => string.Empty,
        };

    private static Style GetHeadingStyle(string? tagName)
        => tagName switch
        {
            "h1" => new Style(Color.Yellow, decoration: Decoration.Bold),
            "h2" => new Style(Color.Cyan1, decoration: Decoration.Bold),
            "h3" => new Style(Color.Green, decoration: Decoration.Bold),
            "h4" => new Style(Color.Blue, decoration: Decoration.Bold),
            "h5" => new Style(Color.Magenta1, decoration: Decoration.Bold),
            "h6" => new Style(Color.Grey, decoration: Decoration.Bold),
            _ => new Style(Color.White, decoration: Decoration.Bold),
        };

    private Widget CreateCodeBlockWidget(VNode node, int zIndex)
    {
        var codeNode = node.Children.FirstOrDefault(c =>
            c.Kind == VNodeKind.Element && string.Equals(c.TagName, "code", StringComparison.OrdinalIgnoreCase));

        if (codeNode is null)
        {
            return new TextWidget(node.ID, GetPlainText(node), key: node.Key, attributes: node.Attributes, zIndex: zIndex);
        }

        var code = GetPlainText(codeNode);
        if (string.IsNullOrWhiteSpace(code))
        {
            return new TextWidget(node.ID, string.Empty, key: node.Key, attributes: node.Attributes, zIndex: zIndex);
        }

        string? language = null;
        if (codeNode.Attributes.TryGetValue("class", out var classAttr) && !string.IsNullOrEmpty(classAttr))
        {
            var classes = classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var langClass = classes.FirstOrDefault(c => c.StartsWith("language-", StringComparison.OrdinalIgnoreCase));
            if (langClass is not null)
            {
                language = langClass["language-".Length..];
            }
        }

        if (_syntaxHighlightingService is null)
        {
            return new TextWidget(node.ID, code, key: node.Key, attributes: node.Attributes, zIndex: zIndex);
        }

        try
        {
            var request = new SyntaxHighlightRequest(code, language, null, true, SyntaxOptions.Default);
            var model = _syntaxHighlightingService.Highlight(request);
            var body = new SyntaxRenderable(model);
            var renderable = new Rows(new IRenderable[] { new Markup(" "), body, new Markup(" ") });
            return new SpectreWidget(node.ID, renderable, node.Key, node.Attributes, zIndex);
        }
        catch (Exception)
        {
            return new TextWidget(node.ID, code, key: node.Key, attributes: node.Attributes, zIndex: zIndex);
        }
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

    private static int TryGetIntAttribute(VNode node, string name, int fallback)
        => int.TryParse(GetAttribute(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int? TryParsePositiveInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }

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

    private static BoxBorderStyle ParsePanelBorder(string? value)
        => ParseBoxBorder(value, fallback: BoxBorderStyle.Square);

    private static BoxBorderStyle ParseBoxBorder(string? value, BoxBorderStyle fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.ToLowerInvariant() switch
            {
                "rounded" => BoxBorderStyle.Rounded,
                "double" => BoxBorderStyle.Double,
                "heavy" => BoxBorderStyle.Heavy,
                "ascii" => BoxBorderStyle.Ascii,
                "none" or "false" => BoxBorderStyle.None,
                _ => BoxBorderStyle.Square,
            };

    private static BoxBorderStyle? ParseOptionalBoxBorder(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : ParseBoxBorder(value, fallback: BoxBorderStyle.Square);

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
