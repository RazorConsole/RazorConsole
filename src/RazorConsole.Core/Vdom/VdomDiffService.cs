// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Vdom;

public sealed class VdomDiffService
{
    public VdomDiffResult Diff(VNode? previous, VNode? current)
    {
        var mutations = new List<VdomMutation>();
        var path = new List<int>();

        DiffInternal(previous, current, path, mutations);

        return new VdomDiffResult(current, mutations);
    }

    private static void DiffInternal(
        VNode? previous,
        VNode? current,
        List<int> path,
        List<VdomMutation> mutations)
    {
        if (previous is null && current is null)
        {
            return;
        }

        if (previous is null)
        {
            mutations.Add(CreateMutation(VdomMutationKind.InsertNode, path, current));
            return;
        }

        if (current is null)
        {
            mutations.Add(CreateMutation(VdomMutationKind.RemoveNode, path, null));
            return;
        }

        if (ReferenceEquals(previous, current) || VdomComparer.AreEqual(previous, current))
        {
            return;
        }

        if (previous.Kind != current.Kind)
        {
            mutations.Add(CreateMutation(VdomMutationKind.ReplaceNode, path, current));
            return;
        }

        switch (previous.Kind)
        {
            case VNodeKind.Text:
                if (!string.Equals(previous.Text, current.Text, StringComparison.Ordinal))
                {
                    mutations.Add(CreateMutation(VdomMutationKind.UpdateText, path, current, current.Text, null));
                }

                return;

            case VNodeKind.Element:
                if (!string.Equals(previous.TagName, current.TagName, StringComparison.Ordinal) ||
                    !string.Equals(previous.Key, current.Key, StringComparison.Ordinal))
                {
                    mutations.Add(CreateMutation(VdomMutationKind.ReplaceNode, path, current));
                    return;
                }

                if (!AreAttributesEqual(previous.Attributes, current.Attributes))
                {
                    mutations.Add(CreateMutation(VdomMutationKind.UpdateAttributes, path, current, null, current.Attributes));
                }

                DiffChildren(previous.Children, current.Children, path, mutations);
                return;

            default:
                DiffChildren(previous.Children, current.Children, path, mutations);
                return;
        }
    }

    private static void DiffChildren(
        IReadOnlyList<VNode> previous,
        IReadOnlyList<VNode> current,
        List<int> path,
        List<VdomMutation> mutations)
    {
        var previousCount = previous.Count;
        var currentCount = current.Count;
        var max = Math.Max(previousCount, currentCount);

        for (var i = 0; i < max; i++)
        {
            path.Add(i);

            if (i >= previousCount)
            {
                mutations.Add(CreateMutation(VdomMutationKind.InsertNode, path, current[i]));
                path.RemoveAt(path.Count - 1);
                continue;
            }

            if (i >= currentCount)
            {
                mutations.Add(CreateMutation(VdomMutationKind.RemoveNode, path, null));
                path.RemoveAt(path.Count - 1);
                continue;
            }

            DiffInternal(previous[i], current[i], path, mutations);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static bool AreAttributesEqual(
        IReadOnlyDictionary<string, object?> left,
        IReadOnlyDictionary<string, object?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var candidate)
                || !EqualityComparer<object?>.Default.Equals(pair.Value, candidate))
            {
                return false;
            }
        }

        return true;
    }

    private static VdomMutation CreateMutation(
        VdomMutationKind kind,
        List<int> path,
        VNode? node,
        string? text = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        var pathCopy = path.Count == 0 ? Array.Empty<int>() : path.ToArray();
        var resolvedText = text ?? (node is { Kind: VNodeKind.Text } ? node.Text : null);
        var resolvedAttributes = attributes ?? (node is { Kind: VNodeKind.Element } ? node.Attributes : null);

        return new VdomMutation(kind, pathCopy, node, resolvedText, resolvedAttributes);
    }
}

public sealed record VdomDiffResult(VNode? Current, IReadOnlyList<VdomMutation> Mutations)
{
    public bool HasChanges => Mutations.Count != 0;

    public static VdomDiffResult NoChanges(VNode? current)
        => new(current, Array.Empty<VdomMutation>());
}

public sealed record VdomMutation(
    VdomMutationKind Kind,
    IReadOnlyList<int> Path,
    VNode? Node,
    string? Text,
    IReadOnlyDictionary<string, object?>? Attributes);

public enum VdomMutationKind
{
    ReplaceNode,
    InsertNode,
    RemoveNode,
    UpdateAttributes,
    UpdateText,
}
