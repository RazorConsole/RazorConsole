// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

public sealed class TextWidget : Widget
{
    public TextWidget(
        string vnodeId,
        string? text,
        Style? style = null,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, zIndex: zIndex)
    {
        Text = text ?? string.Empty;
        Style = style;
    }

    public string Text { get; }

    public Style? Style { get; }

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        if (Text.Length == 0 || constraints.MaxWidth == 0 || constraints.MaxHeight == 0)
        {
            return constraints.Constrain(LayoutSize.Empty);
        }

        var lines = GetWrappedLines(constraints.MaxWidth)
            .Take(constraints.MaxHeight)
            .ToArray();
        var width = lines.Select(GetCellCount).DefaultIfEmpty(0).Max();
        var height = lines.Length;

        return constraints.Constrain(new LayoutSize(Math.Min(width, constraints.MaxWidth), height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
    }

    protected override void PaintCore(PaintContext context)
    {
        if (Bounds.IsEmpty)
        {
            return;
        }

        var lines = GetWrappedLines(Bounds.Width);
        var maxLines = Math.Min(Bounds.Height, lines.Length);
        for (var i = 0; i < maxLines; i++)
        {
            context.Canvas.Write(Bounds.X, Bounds.Y + i, lines[i], Bounds.Width, Style);
        }
    }

    private string[] GetLines()
        => Text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    internal string[] GetWrappedLines(int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return [];
        }

        var wrapped = new List<string>();
        foreach (var line in GetLines())
        {
            var remaining = line;
            if (remaining.Length == 0)
            {
                wrapped.Add(string.Empty);
                continue;
            }

            while (GetCellCount(remaining) > maxWidth)
            {
                var breakIndex = FindWrapIndex(remaining, maxWidth);
                wrapped.Add(remaining[..breakIndex].TrimEnd());
                remaining = remaining[breakIndex..].TrimStart();
                if (remaining.Length == 0)
                {
                    break;
                }
            }

            if (remaining.Length > 0)
            {
                wrapped.Add(remaining);
            }
        }

        return wrapped.ToArray();
    }

    private static int FindWrapIndex(string text, int maxWidth)
    {
        var width = 0;
        var stringIndex = 0;
        var lastWhitespaceIndex = -1;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeWidth = GetCellCount(runeText);
            if (width + runeWidth > maxWidth)
            {
                if (string.IsNullOrWhiteSpace(runeText) && stringIndex > 0)
                {
                    return stringIndex;
                }

                return lastWhitespaceIndex > 0 ? lastWhitespaceIndex : Math.Max(1, stringIndex);
            }

            width += runeWidth;
            stringIndex += rune.Utf16SequenceLength;
            if (string.IsNullOrWhiteSpace(runeText))
            {
                lastWhitespaceIndex = stringIndex;
            }
        }

        return text.Length;
    }

    private static int GetCellCount(string text)
        => Math.Max(0, Segment.CellCount([new Segment(text)]));
}
