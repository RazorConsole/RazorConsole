// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace RazorConsole.Core.Vdom;

/// <summary>
/// Provides access to VNode identifiers discovered during the latest render snapshot.
/// </summary>
public interface IVNodeIdAccessor
{
    /// <summary>
    /// The attribute name used on render tree nodes to register a lookup hook.
    /// </summary>
    public const string HookAttributeName = "data-vnode-hook";

    /// <summary>
    /// Attempts to resolve the VNode ID associated with the supplied hook key.
    /// </summary>
    /// <param name="hookKey">Hook key assigned to a render tree node through <see cref="HookAttributeName"/>.</param>
    /// <param name="vnodeId">Resolved VNode ID when found.</param>
    /// <returns><see langword="true"/> when a VNode ID is available for <paramref name="hookKey"/>; otherwise <see langword="false"/>.</returns>
    bool TryGetVNodeId(string hookKey, [NotNullWhen(true)] out string? vnodeId);

    /// <summary>
    /// Gets the VNode ID associated with the supplied hook key.
    /// </summary>
    /// <param name="hookKey">Hook key assigned to a render tree node through <see cref="HookAttributeName"/>.</param>
    /// <returns>The resolved VNode ID, or <see langword="null"/> when no matching node is present in the latest snapshot.</returns>
    string? GetVNodeIdOrDefault(string hookKey);
}

public sealed class VNodeIdAccessor : IVNodeIdAccessor
{
    private readonly object _sync = new();
    private Dictionary<string, string> _hookToNodeId = new(StringComparer.Ordinal);

    public bool TryGetVNodeId(string hookKey, [NotNullWhen(true)] out string? vnodeId)
    {
        if (string.IsNullOrWhiteSpace(hookKey))
        {
            vnodeId = null;
            return false;
        }

        lock (_sync)
        {
            return _hookToNodeId.TryGetValue(hookKey, out vnodeId);
        }
    }

    public string? GetVNodeIdOrDefault(string hookKey)
        => TryGetVNodeId(hookKey, out var vnodeId) ? vnodeId : null;

    internal void UpdateSnapshot(VNode? root)
    {
        if (root is null)
        {
            lock (_sync)
            {
                _hookToNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return;
        }

        var nextMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var stack = new Stack<VNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.Attributes.TryGetValue(IVNodeIdAccessor.HookAttributeName, out var hookValue)
                && !string.IsNullOrWhiteSpace(hookValue))
            {
                nextMap[hookValue] = current.ID;
            }

            for (var i = current.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(current.Children[i]);
            }
        }

        lock (_sync)
        {
            _hookToNodeId = nextMap;
        }
    }
}