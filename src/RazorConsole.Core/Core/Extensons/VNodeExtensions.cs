// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Extensions;

public static class VNodeExtensions
{

    public static bool HasAttribute(this VNode node, string key)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return node.Attributes.ContainsKey(key);
    }

    public static bool TryGetAttributeValue<TValue>(this VNode node, string key, out TValue? value)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        value = default;

        if (node.Attributes.TryGetValue(key, out var attributeValue) && attributeValue is TValue typedValue)
        {
            value = typedValue;
            return true;
        }

        return false;
    }

}
