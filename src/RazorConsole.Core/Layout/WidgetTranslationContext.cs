// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Layout.Leaves;
using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Rendering.Syntax;
using RazorConsole.Core.Utilities;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

/// <summary>
/// Translates a virtual DOM tree into the <see cref="Widget"/> tree that the layout engine measures and paints.
/// </summary>
public sealed class WidgetTranslationContext
{
    private readonly IWidgetTranslator[] _translators;
    private readonly SyntaxHighlightingService? _syntaxHighlightingService;
    private readonly ScrollStateRegistry? _scrollStateRegistry;
    private readonly HashSet<IAnimatedConsoleRenderable> _animatedRenderables = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetTranslationContext"/> class.
    /// </summary>
    /// <param name="translators">Custom translators, run in order before the built-in translation.</param>
    /// <param name="syntaxHighlightingService">Highlights code blocks.</param>
    public WidgetTranslationContext(
        IEnumerable<IWidgetTranslator>? translators = null,
        SyntaxHighlightingService? syntaxHighlightingService = null)
        : this(translators, syntaxHighlightingService, scrollStateRegistry: null)
    {
    }

    internal WidgetTranslationContext(
        IEnumerable<IWidgetTranslator>? translators,
        SyntaxHighlightingService? syntaxHighlightingService,
        ScrollStateRegistry? scrollStateRegistry)
    {
        _scrollStateRegistry = scrollStateRegistry;
        _translators = translators?.ToArray() ?? [];
        _syntaxHighlightingService = syntaxHighlightingService;
    }

    /// <summary>
    /// Gets the animated renderables found in the current translation.
    /// </summary>
    public IReadOnlyCollection<IAnimatedConsoleRenderable> AnimatedRenderables => _animatedRenderables;

    /// <summary>
    /// Forgets the animated renderables of the previous translation.
    /// </summary>
    public void ClearAnimatedRenderables()
        => _animatedRenderables.Clear();

    /// <summary>
    /// Registers an animated renderable so the display refreshes it on its interval.
    /// Custom translators call this for animated content they place in a <see cref="SpectreWidget"/>.
    /// </summary>
    /// <param name="renderable">The animated renderable.</param>
    public void TrackAnimation(IAnimatedConsoleRenderable renderable)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        _animatedRenderables.Add(renderable);
    }

    /// <summary>
    /// Translates <paramref name="node"/> through the custom translators and then the built-in translation.
    /// </summary>
    /// <param name="node">The virtual DOM node to translate.</param>
    /// <returns>The widget for <paramref name="node"/>.</returns>
    public Widget Translate(VNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _translators.Length == 0 ? TranslateBuiltIn(node) : TranslateWith(0, node);
    }

    private Widget TranslateWith(int index, VNode node)
    {
        if (index >= _translators.Length)
        {
            return TranslateBuiltIn(node);
        }

        var translator = _translators[index];
        return translator.Translate(this, nextNode => TranslateWith(index + 1, nextNode), node)
            ?? throw new InvalidOperationException(
                $"Widget translator '{translator.GetType().FullName}' returned null for {DescribeNode(node)}. "
                + "Return a widget, or next(node) to pass the node on.");
    }

    private static string DescribeNode(VNode node)
        => node.Kind == VNodeKind.Element ? $"<{node.TagName}>" : $"a {node.Kind} node";

    private Widget TranslateBuiltIn(VNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
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

        if (string.Equals(node.TagName, "modal", StringComparison.OrdinalIgnoreCase))
        {
            // Modals are centered absolute overlays.
            var modalAttributes = new Dictionary<string, string?>(node.Attributes, StringComparer.Ordinal)
            {
                ["position"] = "absolute",
                ["data-centered"] = "true",
            };
            return new StackWidget(
                node.ID,
                TranslateChildren(node),
                attributes: modalAttributes,
                zIndex: TryGetIntAttribute(node, "zindex", 9999))
            {
                Opaque = true,
            };
        }

        if (IsTruthy(GetAttribute(node, "data-text")))
        {
            var style = GetAttribute(node, "data-style");
            return new TextWidget(
                node.ID,
                GetTextContent(node),
                TryParseStyle(style),
                node.Key,
                node.Attributes,
                zIndex,
                ComponentMarkupUtilities.ParseLink(style));
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

        if (TryCreateLeafWidget(node, className, zIndex) is { } leafWidget)
        {
            return leafWidget;
        }

        if (string.Equals(className, "grid", StringComparison.OrdinalIgnoreCase))
        {
            return CreateGridWidget(node, zIndex);
        }

        if (TryCreateInlineFlowWidget(node, className, zIndex) is { } inlineWidget)
        {
            return inlineWidget;
        }

        var children = TranslateChildren(node);

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
                zIndex: zIndex)
            {
                Scrollbar = ParseScrollbar(node),
            };
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
                zIndex: zIndex)
            {
                Scrollbar = ParseScrollbar(node),
            };
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
                zIndex: zIndex)
            {
                Scrollbar = ParseScrollbar(node),
            };
        }

        return TranslateContainer(node);
    }

    // data-opaque hides what is painted beneath the container, e.g. for a dropdown overlay.
    private Widget TranslateContainer(VNode node)
    {
        var children = TranslateChildren(node);
        if (children.Count > 0 && GetOverflowMode(node) != OverflowMode.Visible)
        {
            // Plain HTML elements scroll too: the element is the frame and its children are the content.
            var content = ComposeChildren(node, children);
            if (ParseScrollbar(node) is { } scrollbar)
            {
                return new BoxWidget(node.ID, content, attributes: node.Attributes, zIndex: TryGetIntAttribute(node, "z-index", 0))
                {
                    Scrollbar = scrollbar,
                };
            }

            children = [content];
        }

        return new StackWidget(node.ID, children, attributes: node.Attributes, zIndex: TryGetIntAttribute(node, "z-index", 0))
        {
            Opaque = IsTruthy(GetAttribute(node, "data-opaque")),
        };
    }

    // Scrollbar options of a scroll container; null when the container does not scroll.
    private static ScrollbarOptions? ParseScrollbar(VNode node)
    {
        if (GetOverflowMode(node) != OverflowMode.Scroll)
        {
            return null;
        }

        return new ScrollbarOptions(
            ParseEnum(GetAttribute(node, "data-scrollbar-placement"), ScrollbarPlacement.Inset),
            ParseEnum(GetAttribute(node, "data-scrollbar-visibility"), ScrollbarVisibility.Auto),
            ParseChar(GetAttribute(node, "data-scrollbar-track-char"), '┃'),
            ParseChar(GetAttribute(node, "data-scrollbar-thumb-char"), '┃'),
            TryParseHexStyle(GetAttribute(node, "data-scrollbar-track-color")),
            TryParseHexStyle(GetAttribute(node, "data-scrollbar-thumb-color")),
            Math.Max(1, TryGetIntAttribute(node, "data-scrollbar-min-thumb-height", 1)));
    }

    private static OverflowMode GetOverflowMode(VNode node)
        => ParseEnum(GetAttribute(node, "data-overflow") ?? GetAttribute(node, "overflow"), OverflowMode.Visible);

    // Components that wrap a Spectre.Console renderable become opaque leaves that paint into the cell grid.
    // A malformed node (a missing size, an unknown color, a chart too small for its axes) makes the
    // builders throw; it then falls back to the default widget instead of failing the whole render.
    private Widget? TryCreateLeafWidget(VNode node, string? className, int zIndex)
    {
        IRenderable? renderable;
        try
        {
            renderable = CreateLeafRenderable(node, className);
        }
        catch (Exception)
        {
            return null;
        }

        return renderable is null
            ? null
            : new SpectreWidget(node.ID, renderable, node.Key, node.Attributes, zIndex);
    }

    private IRenderable? CreateLeafRenderable(VNode node, string? className)
    {
        if (string.Equals(className, "syntax-highlighter", StringComparison.OrdinalIgnoreCase))
        {
            return CreateSyntaxHighlighterRenderable(node);
        }

        if (IsTruthy(GetAttribute(node, "data-canvas")))
        {
            return SpectreCanvasRenderableBuilder.TryCreate(node);
        }

        if (node.Attributes.ContainsKey("data-spinner"))
        {
            return CreateSpinnerRenderable(node);
        }

        return node.TagName?.ToLowerInvariant() switch
        {
            "stepchart" => StepChartRenderableBuilder.TryCreate(node),
            "barchart" => BarChartRenderableBuilder.TryCreate(node),
            "breakdownchart" => BreakdownChartRenderableBuilder.TryCreate(node),
            _ => null,
        };
    }

    private static IRenderable? CreateSyntaxHighlighterRenderable(VNode node)
    {
        var payload = GetAttribute(node, "data-payload");
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        try
        {
            return new SyntaxRenderable(SyntaxHighlightingService.DecodePayload(payload));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private AnimatedSpinnerRenderable CreateSpinnerRenderable(VNode node)
    {
        var spinner = ComponentMarkupUtilities.ResolveSpinner(GetAttribute(node, "data-spinner-type"));
        var message = GetAttribute(node, "data-message") ?? string.Empty;
        var style = GetAttribute(node, "data-style");
        var autoDismiss = VNodeAttributeReader.TryGetBoolAttribute(node, "data-auto-dismiss", out var parsed) && parsed;

        var animated = new AnimatedSpinnerRenderable(spinner, message, style, autoDismiss);
        _animatedRenderables.Add(animated);
        return animated;
    }

    // A grid is a borderless table that fills rows left to right, like Spectre's Grid:
    // columns are separated by two spaces and a short last row is padded with empty cells.
    private TableWidget CreateGridWidget(VNode node, int zIndex)
    {
        var columnCount = Math.Max(1, TryGetIntAttribute(node, "data-columns", 2));
        var children = TranslateChildren(node);
        var rows = new List<TableWidgetRow>();

        for (var start = 0; start < children.Count; start += columnCount)
        {
            var cells = new TableWidgetCell[columnCount];
            for (var column = 0; column < columnCount; column++)
            {
                var index = start + column;
                var cellId = $"{node.ID}-cell-{index}";
                var child = index < children.Count ? children[index] : new TextWidget(cellId + "-empty", string.Empty);
                cells[column] = new TableWidgetCell(
                    cellId,
                    child,
                    paddingLeft: 0,
                    paddingRight: column == columnCount - 1 ? 0 : 2);
            }

            rows.Add(new TableWidgetRow(cells));
        }

        return new TableWidget(
            node.ID,
            [],
            rows,
            TableWidgetBorderStyle.None,
            expand: !string.Equals(GetAttribute(node, "data-expand"), "false", StringComparison.OrdinalIgnoreCase),
            showHeaders: false,
            width: TryParsePositiveInt(GetAttribute(node, "data-width")),
            key: node.Key,
            attributes: node.Attributes,
            zIndex: zIndex);
    }

    // Paragraphs, inline formatting elements, and plain wrappers around inline elements flow as one
    // wrapped run of styled text, with whitespace collapsed like HTML.
    private Widget? TryCreateInlineFlowWidget(VNode node, string? className, int zIndex)
    {
        if (!string.IsNullOrWhiteSpace(className)
            || string.Equals(GetAttribute(node, "position"), "absolute", StringComparison.OrdinalIgnoreCase)
            || node.Attributes.ContainsKey(IVNodeIdAccessor.HookAttributeName)
            || node.Attributes.ContainsKey("data-focus-key"))
        {
            return null;
        }

        var isParagraph = string.Equals(node.TagName, "p", StringComparison.OrdinalIgnoreCase);
        var isInlineElement = InlineMarkupBuilder.IsInlineTag(node.TagName);
        var isInlineWrapper = IsPlainInlineWrapper(node) && node.Children.Any(IsInlineElement);
        if (!isParagraph && !isInlineElement && !isInlineWrapper)
        {
            return null;
        }

        // The run becomes one piece of markup, so only this node keeps its identity. Nested inline
        // elements are flattened: their classes, event handlers and focus keys are dropped, as they were
        // in the legacy pipeline. Give such an element its own block (or a class) to keep it interactive.
        IReadOnlyList<VNode> run = isInlineElement ? [node] : node.Children;
        if (InlineMarkupBuilder.TryBuildFlowMarkup(run, out var markup) && TryCreateMarkup(markup) is { } inlineMarkup)
        {
            return new SpectreWidget(node.ID, inlineMarkup, node.Key, node.Attributes, zIndex);
        }

        // A paragraph holding components keeps its children side by side, wrapping when they do not fit.
        return isParagraph
            ? new FlexWidget(node.ID, TranslateChildren(node), FlexDirection.Row, wrap: FlexWrap.Wrap, key: node.Key, attributes: node.Attributes, zIndex: zIndex)
            : null;
    }

    private static bool IsInlineElement(VNode node)
        => node.Kind == VNodeKind.Element && InlineMarkupBuilder.IsInlineTag(node.TagName);

    // The builder escapes text, so parsing should not fail; if it does, the node takes the default path.
    private static Markup? TryCreateMarkup(string markup)
    {
        try
        {
            return new Markup(markup);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsFlexNode(VNode node, string? className)
        => string.Equals(className, "flex", StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetAttribute(node, "data-layout"), "flex", StringComparison.OrdinalIgnoreCase);

    private static bool IsBoxNode(VNode node, string? className)
        => string.Equals(className, "box", StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetAttribute(node, "data-layout"), "box", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlainInlineWrapper(VNode node)
        => (string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.TagName, "span", StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrWhiteSpace(GetAttribute(node, "data-layout"))
            && !node.Attributes.ContainsKey(ConsoleRenderer.ComponentWrapperAttribute)
            && !IsTruthy(GetAttribute(node, "data-text"));

    private IReadOnlyList<Widget> TranslateChildren(VNode node)
        => node.Children
            .Where(child => child.Kind != VNodeKind.Text || !string.IsNullOrWhiteSpace(child.Text))
            .Select(Translate)
            .ToArray();

    private Widget ComposeChildren(VNode owner, IReadOnlyList<Widget> children)
        => WrapOverflow(
            owner,
            children.Count switch
            {
                0 => new TextWidget(owner.ID + "-content", string.Empty),
                1 => children[0],
                _ => new StackWidget(owner.ID + "-content", children),
            });

    // overflow is a property of any container: the border and title stay on the owner, only its content scrolls.
    private Widget WrapOverflow(VNode owner, Widget content)
    {
        var mode = GetOverflowMode(owner);
        if (mode == OverflowMode.Visible)
        {
            return content;
        }

        var viewportId = owner.ID + "-viewport";
        if (mode == OverflowMode.Hidden)
        {
            return new ScrollViewportWidget(viewportId, content);
        }

        var top = Math.Max(0, TryGetIntAttribute(owner, "data-scroll-top", 0));
        var scrollId = GetAttribute(owner, "data-scroll-id");
        if (string.IsNullOrWhiteSpace(scrollId))
        {
            // Without an explicit ScrollId the state follows the VNode id, which is not stable when siblings are inserted or reordered.
            scrollId = owner.ID;
        }

        return new ScrollViewportWidget(viewportId, content, top, _scrollStateRegistry, _scrollStateRegistry is null ? null : scrollId);
    }

    private Widget CreateHtmlListWidget(VNode node, int zIndex)
    {
        var isOrdered = string.Equals(node.TagName, "ol", StringComparison.OrdinalIgnoreCase);
        var start = TryGetIntAttribute(node, "start", 1);
        var items = node.Children
            .Where(child => child.Kind == VNodeKind.Element && string.Equals(child.TagName, "li", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var rows = items
            .Select((item, index) => CreateHtmlListItemWidget(item, isOrdered ? $"{start + index}. " : "• ", zIndex))
            .ToArray();

        return new StackWidget(node.ID, rows, attributes: node.Attributes, zIndex: zIndex);
    }

    private Widget CreateHtmlListItemWidget(VNode item, string prefix, int zIndex)
    {
        var nestedLists = item.Children
            .Where(child => child.Kind == VNodeKind.Element
                && (string.Equals(child.TagName, "ul", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(child.TagName, "ol", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var leafChildren = item.Children.Where(child => !nestedLists.Any(nested => ReferenceEquals(nested, child)));
        var leafText = string.Concat(leafChildren.Select(GetPlainText)).Trim();
        var leafWidget = new TextWidget(item.ID, prefix + leafText, key: item.Key, attributes: item.Attributes, zIndex: zIndex);

        if (nestedLists.Length == 0)
        {
            return leafWidget;
        }

        var itemRows = new List<Widget> { leafWidget };
        foreach (var nested in nestedLists)
        {
            var nestedWidget = CreateHtmlListWidget(nested, zIndex);
            itemRows.Add(new BoxWidget(nested.ID + "-indent", nestedWidget, paddingLeft: 2, zIndex: zIndex));
        }

        return new StackWidget(item.ID + "-stack", itemRows, zIndex: zIndex);
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
            return GetTextContent(node);
        }

        return string.Concat(node.Children.Select(GetPlainText));
    }

    // data-content is Spectre markup (Markup escapes it); TextWidget draws text literally.
    // Markup.Remove returns "" for whitespace-only input, which would drop spacer text.
    private static string GetTextContent(VNode node)
    {
        var content = GetAttribute(node, "data-content") ?? string.Empty;
        return string.IsNullOrWhiteSpace(content) ? content : Spectre.Console.Markup.Remove(content);
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

        return new TableWidgetRow(cells, ParseColor(GetAttribute(row, "data-selected-bg")), ParseColor(GetAttribute(row, "data-selected-fg")));
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

    private static Color? ParseColor(string? value)
        => ColorAttribute.TryParse(value, out var color) ? color : null;

    private static Style? TryParseHexStyle(string? value)
        => !string.IsNullOrWhiteSpace(value) && ColorAttribute.TryParse(value, out var color)
            ? new Style(color)
            : (Style?)null;
}
