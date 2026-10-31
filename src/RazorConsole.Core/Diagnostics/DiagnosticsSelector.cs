// Copyright (c) RazorConsole. All rights reserved.

using System.Text.RegularExpressions;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// The node selector grammar shared by <c>find</c>, <c>inspect</c> and <c>tree</c>:
/// <c>id:&lt;id&gt;</c>, <c>hook:&lt;name&gt;</c>, <c>key:&lt;key&gt;</c>, <c>focus:&lt;focusKey&gt;</c>,
/// <c>text:&lt;substring&gt;</c>, <c>text~:&lt;regex&gt;</c>, <c>tag:&lt;name&gt;[attr=value]...</c> and
/// <c>path:/0/2/1</c>. Text selectors match the nearest element above the text; <c>path:</c> also reaches text nodes.
/// </summary>
internal sealed class DiagnosticsSelector
{
    // Selectors come from the peer, so a pathological pattern must not stall the endpoint.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Maps a visited node to the node it selects, or null. Text selectors select the element above the text.
    private readonly Func<DiagnosticsTreeIndex, DiagnosticsTreeEntry, DiagnosticsTreeEntry?> _select;

    private DiagnosticsSelector(Func<DiagnosticsTreeIndex, DiagnosticsTreeEntry, DiagnosticsTreeEntry?> select)
    {
        _select = select;
    }

    /// <exception cref="DiagnosticsProtocolException">The selector is malformed (<see cref="DiagnosticsErrorCodes.InvalidParams"/>).</exception>
    public static DiagnosticsSelector Parse(string? selector)
    {
        var colon = selector?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (selector is null || colon <= 0)
        {
            throw Invalid(selector, "expected <kind>:<value>");
        }

        var kind = selector[..colon];
        var value = selector[(colon + 1)..];
        if (value.Length == 0)
        {
            throw Invalid(selector, "the value is empty");
        }

        return kind switch
        {
            "id" => Element((_, e) => e.Node.ID == value),
            "hook" => Element((_, e) => e.Node.Attributes.TryGetValue(IVNodeIdAccessor.HookAttributeName, out var hook) && hook == value),
            "key" => Element((_, e) => e.Node.Key == value),
            "focus" => Element((index, e) => index.FocusKeyOf(e.Node) == value),
            "text" => Text(text => text.Contains(value, StringComparison.Ordinal)),
            "text~" => Text(CreateRegex(selector, value).IsMatch),
            "tag" => ParseTag(selector, value),
            "path" => new DiagnosticsSelector((_, e) => e.Path == value ? e : null),
            _ => throw Invalid(selector, $"unknown kind '{kind}'"),
        };
    }

    /// <summary>
    /// Matching nodes in document order, each once.
    /// </summary>
    public List<DiagnosticsTreeEntry> Match(DiagnosticsTreeIndex index)
    {
        var matches = new List<DiagnosticsTreeEntry>();
        var seen = new HashSet<DiagnosticsTreeEntry>(ReferenceEqualityComparer.Instance);
        foreach (var entry in index.Nodes)
        {
            if (_select(index, entry) is { } target && seen.Add(target))
            {
                matches.Add(target);
            }
        }

        return matches;
    }

    private static DiagnosticsSelector Element(Func<DiagnosticsTreeIndex, DiagnosticsTreeEntry, bool> predicate)
        => new((index, entry) => entry.Node.Kind == VNodeKind.Element && predicate(index, entry) ? entry : null);

    // A text node selects the element above it; a Markup-style element holds its text in an attribute and selects itself.
    private static DiagnosticsSelector Text(Func<string, bool> matches)
        => new((_, entry) =>
        {
            if (entry.Node.Kind == VNodeKind.Text)
            {
                return matches(entry.Node.Text) ? DiagnosticsTreeIndex.NearestElement(entry.Parent) : null;
            }

            return DiagnosticsTreeIndex.RenderedText(entry.Node) is { } rendered && matches(rendered) ? entry : null;
        })
        {
            IsText = true,
        };

    /// <summary>
    /// Whether this is a <c>text:</c> or <c>text~:</c> selector.
    /// </summary>
    public bool IsText { get; private init; }

    private static Regex CreateRegex(string selector, string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw Invalid(selector, ex.Message);
        }
    }

    // tag:<name> followed by any number of [attr] or [attr=value] filters.
    private static DiagnosticsSelector ParseTag(string selector, string value)
    {
        var bracket = value.IndexOf('[', StringComparison.Ordinal);
        var tag = bracket < 0 ? value : value[..bracket];
        if (tag.Length == 0)
        {
            throw Invalid(selector, "the tag name is empty");
        }

        var filters = new List<(string Name, string? Value)>();
        var rest = bracket < 0 ? string.Empty : value[bracket..];
        while (rest.Length > 0)
        {
            var close = rest.IndexOf(']', StringComparison.Ordinal);
            if (rest[0] != '[' || close < 2)
            {
                throw Invalid(selector, "expected [attr] or [attr=value]");
            }

            var filter = rest[1..close];
            var equals = filter.IndexOf('=', StringComparison.Ordinal);
            filters.Add(equals < 0 ? (filter, null) : (filter[..equals], filter[(equals + 1)..]));
            rest = rest[(close + 1)..];
        }

        return Element((_, e) =>
            string.Equals(e.Node.TagName, tag, StringComparison.OrdinalIgnoreCase)
            && filters.TrueForAll(f => e.Node.Attributes.TryGetValue(f.Name, out var actual) && (f.Value is null || actual == f.Value)));
    }

    private static DiagnosticsProtocolException Invalid(string? selector, string reason)
        => new(DiagnosticsErrorCodes.InvalidParams, $"Invalid selector '{selector}': {reason}.");
}
