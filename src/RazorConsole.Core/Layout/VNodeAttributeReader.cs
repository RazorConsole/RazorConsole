// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Layout;

/// <summary>
/// Reads and parses <see cref="VNode"/> attributes for widget translation.
/// </summary>
internal static class VNodeAttributeReader
{
    public static string? GetAttribute(VNode node, string name)
    {
        if (node.Kind != VNodeKind.Element)
        {
            return null;
        }

        return node.Attributes.TryGetValue(name, out var value) ? value : null;
    }

    public static bool TryGetBoolAttribute(VNode node, string name, out bool value)
    {
        var raw = GetAttribute(node, name);
        if (!string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    public static int TryGetIntAttribute(VNode node, string name, int fallback)
    {
        var raw = GetAttribute(node, name);
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    public static bool TryParsePositiveInt(string? raw, out int result)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            result = value;
            return true;
        }

        result = default;
        return false;
    }

    public static bool TryParsePositiveDouble(string? raw, out double result)
    {
        if (double.TryParse(raw, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            result = value;
            return true;
        }

        result = default;
        return false;
    }
}
