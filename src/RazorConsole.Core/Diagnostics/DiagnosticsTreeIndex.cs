// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// One frame's tree flattened for lookups: every node with its child-index path, parent and depth, plus layout
/// rectangles and focus keys by element id. Built per request from <see cref="DiagnosticsState.LatestFrame"/>,
/// so the renderer never pays for it.
/// </summary>
internal sealed class DiagnosticsTreeIndex
{
    private const int MaxMatchTextLength = 200;

    private readonly Dictionary<string, DiagnosticsRect> _rects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _focusKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<VNode, DiagnosticsTreeEntry> _entries = new(ReferenceEqualityComparer.Instance);

    public DiagnosticsTreeIndex(DiagnosticsFrame? frame, IReadOnlyList<FocusManager.FocusTargetInfo> focusTargets)
    {
        Frame = frame?.Sequence ?? 0;
        Root = frame?.Root;

        foreach (var layout in frame?.Layouts ?? [])
        {
            // A node can own several boxes; the first one enumerated is its outermost.
            if (layout is { Left: { } left, Top: { } top, Width: { } width, Height: { } height })
            {
                _rects.TryAdd(layout.VNodeId, new DiagnosticsRect(left, top, width, height, layout.ZIndex));
            }
        }

        foreach (var target in focusTargets)
        {
            _focusKeys.TryAdd(target.VNodeId, target.Key);
            if (target.IsFocused)
            {
                FocusedId = target.VNodeId;
            }
        }

        if (Root is not null)
        {
            Add(Root, "/", parent: null, depth: 0);
        }
    }

    public long Frame { get; }

    public VNode? Root { get; }

    public string? FocusedId { get; }

    /// <summary>
    /// Every node in document order.
    /// </summary>
    public List<DiagnosticsTreeEntry> Nodes { get; } = [];

    public DiagnosticsTreeEntry this[VNode node] => _entries[node];

    public DiagnosticsRect? RectOf(VNode node)
        => node.Kind == VNodeKind.Element && _rects.TryGetValue(node.ID, out var rect) ? rect : null;

    public string? FocusKeyOf(VNode node)
        => node.Kind == VNodeKind.Element && _focusKeys.TryGetValue(node.ID, out var key) ? key : null;

    /// <summary>
    /// The nearest element at or above <paramref name="entry"/>.
    /// </summary>
    public static DiagnosticsTreeEntry? NearestElement(DiagnosticsTreeEntry? entry)
    {
        while (entry is not null && entry.Node.Kind != VNodeKind.Element)
        {
            entry = entry.Parent;
        }

        return entry;
    }

    /// <summary>
    /// The element ancestors of <paramref name="entry"/>, nearest first.
    /// </summary>
    public IEnumerable<DiagnosticsTreeEntry> ElementAncestors(DiagnosticsTreeEntry entry)
    {
        for (var parent = NearestElement(entry.Parent); parent is not null; parent = NearestElement(parent.Parent))
        {
            yield return parent;
        }
    }

    public DiagnosticsMatch ToMatch(DiagnosticsTreeEntry entry)
    {
        var node = entry.Node;
        return new DiagnosticsMatch(
            node.Kind == VNodeKind.Element ? node.ID : null,
            entry.Path,
            KindName(node.Kind),
            node.TagName,
            TextOf(node),
            RectOf(node),
            FocusKeyOf(node));
    }

    public static string KindName(VNodeKind kind) => kind switch
    {
        VNodeKind.Element => "element",
        VNodeKind.Text => "text",
        VNodeKind.Component => "component",
        _ => "region",
    };

    /// <summary>
    /// The node's own text, or its descendants' text with whitespace collapsed, shortened to a summary length.
    /// </summary>
    public static string? TextOf(VNode node)
    {
        var builder = new StringBuilder();
        AppendText(node, builder);
        var text = builder.ToString().Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= MaxMatchTextLength ? text : string.Concat(text.AsSpan(0, MaxMatchTextLength - 1), "…");
    }

    /// <summary>
    /// The text a <c>Markup</c>-style element renders from its <c>data-content</c> attribute (an element with
    /// <c>data-text</c> set), with the markup removed as on screen. <see langword="null"/> for any other node.
    /// </summary>
    public static string? RenderedText(VNode node)
    {
        if (node.Kind != VNodeKind.Element
            || !node.Attributes.TryGetValue("data-text", out var flag)
            || !string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
            || !node.Attributes.TryGetValue("data-content", out var content)
            || content is null)
        {
            return null;
        }

        try
        {
            return string.IsNullOrWhiteSpace(content) ? content : Spectre.Console.Markup.Remove(content);
        }
        catch (InvalidOperationException)
        {
            // Malformed markup is shown as written.
            return content;
        }
    }

    private static void AppendText(VNode node, StringBuilder builder)
    {
        if (builder.Length > MaxMatchTextLength)
        {
            return;
        }

        if (RenderedText(node) is { } rendered)
        {
            AppendCollapsed(rendered, builder);
            return;
        }

        if (node.Kind == VNodeKind.Text)
        {
            AppendCollapsed(node.Text, builder);
            return;
        }

        foreach (var child in node.Children)
        {
            // Sibling elements are separate words, as on screen; text nodes inside one element run together.
            if (child.Kind == VNodeKind.Element && builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }

            AppendText(child, builder);
        }
    }

    private static void AppendCollapsed(string text, StringBuilder builder)
    {
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }
    }

    private void Add(VNode node, string path, DiagnosticsTreeEntry? parent, int depth)
    {
        var entry = new DiagnosticsTreeEntry(node, path, parent, depth);
        Nodes.Add(entry);
        _entries[node] = entry;

        var prefix = path == "/" ? "/" : path + "/";
        for (var i = 0; i < node.Children.Count; i++)
        {
            Add(node.Children[i], prefix + i.ToString(CultureInfo.InvariantCulture), entry, depth + 1);
        }
    }
}

/// <param name="Node">The node.</param>
/// <param name="Path">Child indexes from the root, such as <c>/0/2</c>; the root itself is <c>/</c>.</param>
/// <param name="Parent">The parent entry, or <see langword="null"/> for the root.</param>
/// <param name="Depth">Distance from the root.</param>
internal sealed record DiagnosticsTreeEntry(VNode Node, string Path, DiagnosticsTreeEntry? Parent, int Depth);
