// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Vdom;

internal static class VdomComparer
{
    public static bool AreEqual(VNode? left, VNode? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            VNodeKind.Text => string.Equals(left.Text, right.Text, StringComparison.Ordinal),
            VNodeKind.Element => AreElementsEqual(left, right),
            _ => AreChildrenEqual(left, right),
        };
    }

    private static bool AreElementsEqual(VNode left, VNode right)
    {
        if (!string.Equals(left.TagName, right.TagName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.Key, right.Key, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.Attributes.Count != right.Attributes.Count)
        {
            return false;
        }

        foreach (var pair in left.Attributes)
        {
            if (!right.Attributes.TryGetValue(pair.Key, out var rightValue))
            {
                return false;
            }

            if (!EqualityComparer<object?>.Default.Equals(pair.Value, rightValue))
            {
                return false;
            }
        }

        if (left.Children.Count != right.Children.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Children.Count; i++)
        {
            if (!AreEqual(left.Children[i], right.Children[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreChildrenEqual(VNode left, VNode right)
    {
        if (left.Children.Count != right.Children.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Children.Count; i++)
        {
            if (!AreEqual(left.Children[i], right.Children[i]))
            {
                return false;
            }
        }

        return true;
    }
}
