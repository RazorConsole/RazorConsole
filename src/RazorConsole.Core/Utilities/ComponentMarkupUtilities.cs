// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Spectre.Console;

namespace RazorConsole.Core.Rendering.ComponentMarkup;

internal static class ComponentMarkupUtilities
{
    public static string CreateStyledMarkup(string? style, string content, bool requiresEscape)
    {
        if (requiresEscape)
        {
            content = Markup.Escape(content);
        }

        if (string.IsNullOrWhiteSpace(style))
        {
            return content;
        }

        return string.Concat("[", style, "]", content, "[/]");
    }

    public static Spinner ResolveSpinner(string? spinnerType)
    {
        if (string.IsNullOrWhiteSpace(spinnerType))
        {
            return Spinner.Known.Dots;
        }

        return GetSpinnerFromType(typeof(Spinner.Known), spinnerType) ?? Spinner.Known.Dots;
    }

    private static Spinner? GetSpinnerFromType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        Type type,
        string name)
    {
        var property = type.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        return property?.GetValue(null) as Spinner;
    }
}
