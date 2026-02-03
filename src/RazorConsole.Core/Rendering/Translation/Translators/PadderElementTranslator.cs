// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class PadderElementTranslator : ITranslationMiddleware
{
    private static readonly char[] PaddingSeparators = [',', ' '];

    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        if (!TranslationHelpers.TryConvertChildrenToRenderables(node.Children, context, out var children))
        {
            return next(node);
        }

        var content = VdomSpectreTranslator.ComposeChildContent(children);
        var padding = ParsePadding(VdomSpectreTranslator.GetAttribute(node, "data-padding"));
        var padder = new Padder(content, padding);

        return padder;
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && node.TryGetAttributeValue<string>("class", out var value)
           && string.Equals(value, "padder", StringComparison.OrdinalIgnoreCase);

    private static Padding ParsePadding(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Padding(0, 0, 0, 0);
        }

        var parts = raw.Split(PaddingSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = parts
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? Math.Max(number, 0) : 0)
            .Take(4)
            .ToArray();

        return values.Length switch
        {
            0 => new Padding(0, 0, 0, 0),
            1 => new Padding(values[0], values[0], values[0], values[0]),
            2 => new Padding(values[0], values[1], values[0], values[1]),
            3 => new Padding(values[0], values[1], values[2], values[1]),
            4 => new Padding(values[0], values[1], values[2], values[3]),
            _ => new Padding(0, 0, 0, 0),
        };
    }
}

