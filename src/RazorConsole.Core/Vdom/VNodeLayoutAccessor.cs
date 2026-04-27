// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Vdom;

/// <summary>
/// Represents snapshot layout metadata for a virtual DOM node.
/// </summary>
/// <param name="VNodeId">The unique identifier of the node.</param>
/// <param name="Top">Top offset in rows, when available.</param>
/// <param name="Left">Left offset in columns, when available.</param>
/// <param name="Right">Right offset in columns, when available.</param>
/// <param name="Bottom">Bottom offset in rows, when available.</param>
/// <param name="Width">Resolved or declared width, when available.</param>
/// <param name="Height">Resolved or declared height, when available.</param>
/// <param name="ZIndex">Stacking order of the node.</param>
/// <param name="IsCentered">Whether the node is centered by layout policy.</param>
public readonly record struct VNodeLayoutInfo(
    string VNodeId,
    int? Top,
    int? Left,
    int? Right,
    int? Bottom,
    int? Width,
    int? Height,
    int ZIndex,
    bool IsCentered = false);

/// <summary>
/// Provides access to snapshot layout metadata of virtual DOM nodes.
/// </summary>
public interface IVNodeLayoutAccessor
{
    /// <summary>
    /// Attempts to resolve layout metadata by VNode ID.
    /// </summary>
    /// <param name="vnodeId">The VNode identifier.</param>
    /// <param name="layout">Resolved layout metadata when found.</param>
    /// <returns><see langword="true"/> when a layout entry exists for <paramref name="vnodeId"/>; otherwise <see langword="false"/>.</returns>
    bool TryGetLayoutByVNodeId(string vnodeId, out VNodeLayoutInfo layout);

    /// <summary>
    /// Gets layout metadata by VNode ID.
    /// </summary>
    /// <param name="vnodeId">The VNode identifier.</param>
    /// <returns>Layout metadata when found; otherwise <see langword="null"/>.</returns>
    VNodeLayoutInfo? GetLayoutByVNodeIdOrDefault(string vnodeId);

    /// <summary>
    /// Attempts to resolve layout metadata by hook key.
    /// </summary>
    /// <param name="hookKey">Hook key assigned through <see cref="IVNodeIdAccessor.HookAttributeName"/>.</param>
    /// <param name="layout">Resolved layout metadata when found.</param>
    /// <returns><see langword="true"/> when a layout entry exists for <paramref name="hookKey"/>; otherwise <see langword="false"/>.</returns>
    bool TryGetLayoutByHookKey(string hookKey, out VNodeLayoutInfo layout);

    /// <summary>
    /// Gets layout metadata by hook key.
    /// </summary>
    /// <param name="hookKey">Hook key assigned through <see cref="IVNodeIdAccessor.HookAttributeName"/>.</param>
    /// <returns>Layout metadata when found; otherwise <see langword="null"/>.</returns>
    VNodeLayoutInfo? GetLayoutByHookKeyOrDefault(string hookKey);

    /// <summary>
    /// Attempts to resolve layout metadata by focus key.
    /// </summary>
    /// <param name="focusKey">Focus key assigned through the <c>data-focus-key</c> attribute.</param>
    /// <param name="layout">Resolved layout metadata when found.</param>
    /// <returns><see langword="true"/> when a layout entry exists for <paramref name="focusKey"/>; otherwise <see langword="false"/>.</returns>
    bool TryGetLayoutByFocusKey(string focusKey, out VNodeLayoutInfo layout);

    /// <summary>
    /// Gets layout metadata by focus key.
    /// </summary>
    /// <param name="focusKey">Focus key assigned through the <c>data-focus-key</c> attribute.</param>
    /// <returns>Layout metadata when found; otherwise <see langword="null"/>.</returns>
    VNodeLayoutInfo? GetLayoutByFocusKeyOrDefault(string focusKey);

    /// <summary>
    /// Gets available ancestor layout metadata from the root node to the requested node.
    /// </summary>
    /// <param name="vnodeId">The VNode identifier.</param>
    /// <returns>Layout metadata ordered from root to target. Nodes without layout metadata are skipped.</returns>
    IReadOnlyList<VNodeLayoutInfo> GetLayoutAncestorsByVNodeId(string vnodeId);

    /// <summary>
    /// Gets available ancestor layout metadata from the root node to the node associated with the supplied hook key.
    /// </summary>
    /// <param name="hookKey">Hook key assigned through <see cref="IVNodeIdAccessor.HookAttributeName"/>.</param>
    /// <returns>Layout metadata ordered from root to target. Nodes without layout metadata are skipped.</returns>
    IReadOnlyList<VNodeLayoutInfo> GetLayoutAncestorsByHookKey(string hookKey);

    /// <summary>
    /// Gets available ancestor layout metadata from the root node to the node associated with the supplied focus key.
    /// </summary>
    /// <param name="focusKey">Focus key assigned through the <c>data-focus-key</c> attribute.</param>
    /// <returns>Layout metadata ordered from root to target. Nodes without layout metadata are skipped.</returns>
    IReadOnlyList<VNodeLayoutInfo> GetLayoutAncestorsByFocusKey(string focusKey);
}

public sealed class VNodeLayoutAccessor : IVNodeLayoutAccessor
{
    private readonly object _sync = new();
    private Dictionary<string, VNodeLayoutInfo> _layoutByVNodeId = new(StringComparer.Ordinal);
    private Dictionary<string, VNodeLayoutInfo> _layoutByHookKey = new(StringComparer.Ordinal);
    private Dictionary<string, VNodeLayoutInfo> _layoutByFocusKey = new(StringComparer.Ordinal);
    private Dictionary<string, string> _hookToVNodeId = new(StringComparer.Ordinal);
    private Dictionary<string, string> _focusKeyToVNodeId = new(StringComparer.Ordinal);
    private Dictionary<string, string?> _parentByVNodeId = new(StringComparer.Ordinal);
    private Dictionary<string, List<string>> _hooksByVNodeId = new(StringComparer.Ordinal);
    private Dictionary<string, List<string>> _focusKeysByVNodeId = new(StringComparer.Ordinal);

    public bool TryGetLayoutByVNodeId(string vnodeId, out VNodeLayoutInfo layout)
    {
        if (string.IsNullOrWhiteSpace(vnodeId))
        {
            layout = default;
            return false;
        }

        lock (_sync)
        {
            return _layoutByVNodeId.TryGetValue(vnodeId, out layout);
        }
    }

    public VNodeLayoutInfo? GetLayoutByVNodeIdOrDefault(string vnodeId)
        => TryGetLayoutByVNodeId(vnodeId, out var layout) ? layout : null;

    public bool TryGetLayoutByHookKey(string hookKey, out VNodeLayoutInfo layout)
    {
        if (string.IsNullOrWhiteSpace(hookKey))
        {
            layout = default;
            return false;
        }

        lock (_sync)
        {
            return _layoutByHookKey.TryGetValue(hookKey, out layout);
        }
    }

    public VNodeLayoutInfo? GetLayoutByHookKeyOrDefault(string hookKey)
        => TryGetLayoutByHookKey(hookKey, out var layout) ? layout : null;

    public bool TryGetLayoutByFocusKey(string focusKey, out VNodeLayoutInfo layout)
    {
        if (string.IsNullOrWhiteSpace(focusKey))
        {
            layout = default;
            return false;
        }

        lock (_sync)
        {
            return _layoutByFocusKey.TryGetValue(focusKey, out layout);
        }
    }

    public VNodeLayoutInfo? GetLayoutByFocusKeyOrDefault(string focusKey)
        => TryGetLayoutByFocusKey(focusKey, out var layout) ? layout : null;

    public IReadOnlyList<VNodeLayoutInfo> GetLayoutAncestorsByVNodeId(string vnodeId)
    {
        if (string.IsNullOrWhiteSpace(vnodeId))
        {
            return Array.Empty<VNodeLayoutInfo>();
        }

        lock (_sync)
        {
            return GetLayoutAncestorsByVNodeId_NoLock(vnodeId);
        }
    }

    public IReadOnlyList<VNodeLayoutInfo> GetLayoutAncestorsByHookKey(string hookKey)
    {
        if (string.IsNullOrWhiteSpace(hookKey))
        {
            return Array.Empty<VNodeLayoutInfo>();
        }

        lock (_sync)
        {
            return _hookToVNodeId.TryGetValue(hookKey, out var vnodeId)
                ? GetLayoutAncestorsByVNodeId_NoLock(vnodeId)
                : Array.Empty<VNodeLayoutInfo>();
        }
    }

    public IReadOnlyList<VNodeLayoutInfo> GetLayoutAncestorsByFocusKey(string focusKey)
    {
        if (string.IsNullOrWhiteSpace(focusKey))
        {
            return Array.Empty<VNodeLayoutInfo>();
        }

        lock (_sync)
        {
            return _focusKeyToVNodeId.TryGetValue(focusKey, out var vnodeId)
                ? GetLayoutAncestorsByVNodeId_NoLock(vnodeId)
                : Array.Empty<VNodeLayoutInfo>();
        }
    }

    internal void UpdateRuntimeLayout(VNodeLayoutInfo layout)
    {
        if (string.IsNullOrWhiteSpace(layout.VNodeId))
        {
            return;
        }

        lock (_sync)
        {
            _layoutByVNodeId[layout.VNodeId] = layout;

            if (_hooksByVNodeId.TryGetValue(layout.VNodeId, out var hooks))
            {
                foreach (var hook in hooks)
                {
                    _layoutByHookKey[hook] = layout;
                }
            }

            if (_focusKeysByVNodeId.TryGetValue(layout.VNodeId, out var focusKeys))
            {
                foreach (var focusKey in focusKeys)
                {
                    _layoutByFocusKey[focusKey] = layout;
                }
            }
        }
    }

    internal void UpdateSnapshot(
        VNode? root,
        IReadOnlyCollection<VNodeLayoutInfo>? layouts,
        IReadOnlyDictionary<string, string?>? layoutParents = null)
    {
        if (root is null)
        {
            lock (_sync)
            {
                _layoutByVNodeId = new Dictionary<string, VNodeLayoutInfo>(StringComparer.Ordinal);
                _layoutByHookKey = new Dictionary<string, VNodeLayoutInfo>(StringComparer.Ordinal);
                _layoutByFocusKey = new Dictionary<string, VNodeLayoutInfo>(StringComparer.Ordinal);
                _hookToVNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
                _focusKeyToVNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
                _parentByVNodeId = new Dictionary<string, string?>(StringComparer.Ordinal);
                _hooksByVNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                _focusKeysByVNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            return;
        }

        var nextByVNodeId = new Dictionary<string, VNodeLayoutInfo>(StringComparer.Ordinal);
        if (layouts is not null)
        {
            foreach (var layout in layouts)
            {
                if (string.IsNullOrWhiteSpace(layout.VNodeId))
                {
                    continue;
                }

                nextByVNodeId[layout.VNodeId] = layout;
            }
        }

        var nextHookToVNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextHooksByVNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var nextByHookKey = new Dictionary<string, VNodeLayoutInfo>(StringComparer.Ordinal);
        var nextFocusKeysByVNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var nextByFocusKey = new Dictionary<string, VNodeLayoutInfo>(StringComparer.Ordinal);
        var nextFocusKeyToVNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextParentByVNodeId = new Dictionary<string, string?>(StringComparer.Ordinal);
        var stack = new Stack<(VNode Node, string? ParentId)>();
        stack.Push((root, null));

        while (stack.Count > 0)
        {
            var (current, parentId) = stack.Pop();
            nextParentByVNodeId[current.ID] = parentId;

            if (current.Attributes.TryGetValue(IVNodeIdAccessor.HookAttributeName, out var hookValue)
                && !string.IsNullOrWhiteSpace(hookValue))
            {
                nextHookToVNodeId[hookValue] = current.ID;

                if (!nextHooksByVNodeId.TryGetValue(current.ID, out var hooks))
                {
                    hooks = new List<string>();
                    nextHooksByVNodeId[current.ID] = hooks;
                }

                hooks.Add(hookValue);

                if (nextByVNodeId.TryGetValue(current.ID, out var layout))
                {
                    nextByHookKey[hookValue] = layout;
                }
            }

            if (current.Attributes.TryGetValue("data-focus-key", out var focusKey)
                && !string.IsNullOrWhiteSpace(focusKey))
            {
                nextFocusKeyToVNodeId[focusKey] = current.ID;

                if (!nextFocusKeysByVNodeId.TryGetValue(current.ID, out var focusKeys))
                {
                    focusKeys = new List<string>();
                    nextFocusKeysByVNodeId[current.ID] = focusKeys;
                }

                focusKeys.Add(focusKey);

                if (nextByVNodeId.TryGetValue(current.ID, out var layout))
                {
                    nextByFocusKey[focusKey] = layout;
                }
            }

            for (var i = current.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((current.Children[i], current.ID));
            }
        }

        if (layoutParents is not null)
        {
            foreach (var (vnodeId, parentId) in layoutParents)
            {
                if (!string.IsNullOrWhiteSpace(vnodeId))
                {
                    nextParentByVNodeId[vnodeId] = parentId;
                }
            }
        }

        lock (_sync)
        {
            _layoutByVNodeId = nextByVNodeId;
            _layoutByHookKey = nextByHookKey;
            _layoutByFocusKey = nextByFocusKey;
            _hookToVNodeId = nextHookToVNodeId;
            _focusKeyToVNodeId = nextFocusKeyToVNodeId;
            _parentByVNodeId = nextParentByVNodeId;
            _hooksByVNodeId = nextHooksByVNodeId;
            _focusKeysByVNodeId = nextFocusKeysByVNodeId;
        }
    }

    private IReadOnlyList<VNodeLayoutInfo> GetLayoutAncestorsByVNodeId_NoLock(string vnodeId)
    {
        var ids = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? current = vnodeId;

        while (!string.IsNullOrWhiteSpace(current) && visited.Add(current))
        {
            ids.Add(current);
            current = _parentByVNodeId.TryGetValue(current, out var parentId) ? parentId : null;
        }

        ids.Reverse();

        var layouts = new List<VNodeLayoutInfo>();
        foreach (var id in ids)
        {
            if (_layoutByVNodeId.TryGetValue(id, out var layout))
            {
                layouts.Add(layout);
            }
        }

        return layouts;
    }
}
