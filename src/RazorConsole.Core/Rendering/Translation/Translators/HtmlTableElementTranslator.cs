// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class HtmlTableElementTranslator : ITranslationMiddleware
{
    private static readonly IReadOnlyDictionary<string, TableBorder> BorderLookup = new Dictionary<string, TableBorder>(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = TableBorder.None,
        ["simple"] = TableBorder.Simple,
        ["double"] = TableBorder.Double,
        ["rounded"] = TableBorder.Rounded,
        ["heavy"] = TableBorder.Heavy,
        ["minimal"] = TableBorder.Minimal,
        ["minimaldoublehead"] = TableBorder.MinimalDoubleHead,
        ["minimalheavyhead"] = TableBorder.MinimalHeavyHead,
        ["simpleheavy"] = TableBorder.SimpleHeavy,
        ["horizontal"] = TableBorder.Horizontal,
        ["square"] = TableBorder.Square,
        ["ascii"] = TableBorder.Ascii,
    };

    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!IsTableNode(node))
        {
            return next(node);
        }

        var headerRows = BuildRows(node, "thead", context, out var headerRowData);
        if (!headerRows)
        {
            return next(node);
        }

        var bodyRows = BuildRows(node, "tbody", context, out var bodyRowData);
        if (!bodyRows)
        {
            return next(node);
        }

        var footerRows = BuildRows(node, "tfoot", context, out var footerRowData);
        if (!footerRows)
        {
            return next(node);
        }

        var looseRowNodes = GetLooseRows(node).ToList();
        var looseRowData = new List<RowData>();
        foreach (var looseRow in looseRowNodes)
        {
            if (!TryBuildRow(looseRow, context, out var data))
            {
                return next(node);
            }

            looseRowData.Add(data);
        }

        var table = new Table();
        var dataExpand = VdomSpectreTranslator.GetAttribute(node, "data-expand") ?? "false";
        table.Expand = !string.Equals(dataExpand, "false", StringComparison.OrdinalIgnoreCase);

        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"), out var width))
        {
            table.Width = width;
        }

        var title = VdomSpectreTranslator.GetAttribute(node, "data-title");
        if (!string.IsNullOrWhiteSpace(title))
        {
            table.Title = new TableTitle(Markup.Escape(title));
        }

        var border = ResolveBorder(VdomSpectreTranslator.GetAttribute(node, "data-border"));
        table.Border = border;

        var borderColorAttr = VdomSpectreTranslator.GetAttribute(node, "data-border-color");
        if (!string.IsNullOrWhiteSpace(borderColorAttr) && Color.TryFromHex(borderColorAttr, out var borderColor))
        {
            table.BorderColor(borderColor);
        }

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-show-headers", out var showHeaders))
        {
            table.ShowHeaders = showHeaders;
        }

        var columnSource = headerRowData.FirstOrDefault()
            ?? bodyRowData.FirstOrDefault()
            ?? looseRowData.FirstOrDefault()
            ?? footerRowData.FirstOrDefault();

        var columnCount = columnSource?.Cells.Length ?? 0;

        if (columnCount == 0)
        {
            return table;
        }

        var headerDefinition = headerRowData.FirstOrDefault();
        for (var i = 0; i < columnCount; i++)
        {
            var headerCell = headerDefinition?.GetCell(i);
            var headerRenderable = headerCell?.Content ?? new Markup(string.Empty);
            var column = new TableColumn(headerRenderable);

            if (headerCell?.Alignment is { } alignment)
            {
                column.Alignment = alignment;
            }

            table.AddColumn(column);
        }

        var headerRowsToRender = table.ShowHeaders ? headerRowData.Skip(1) : headerRowData;
        foreach (var headerRow in headerRowsToRender)
        {
            AddRow(table, headerRow, columnCount);
        }

        foreach (var row in bodyRowData)
        {
            AddRow(table, row, columnCount);
        }

        foreach (var row in looseRowData)
        {
            AddRow(table, row, columnCount);
        }

        foreach (var row in footerRowData)
        {
            AddRow(table, row, columnCount);
        }

        return table;
    }

    private static bool IsTableNode(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "table", StringComparison.OrdinalIgnoreCase);

    private static bool BuildRows(VNode table, string sectionName, TranslationContext context, out List<RowData> rows)
    {
        rows = new List<RowData>();

        foreach (var rowNode in GetSectionRows(table, sectionName))
        {
            if (!TryBuildRow(rowNode, context, out var rowData))
            {
                rows = new List<RowData>();
                return false;
            }

            rows.Add(rowData);
        }

        return true;
    }

    private static IEnumerable<VNode> GetSectionRows(VNode table, string sectionName)
    {
        foreach (var child in table.Children)
        {
            if (child.Kind != VNodeKind.Element)
            {
                continue;
            }

            if (!string.Equals(child.TagName, sectionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var row in GetRows(child))
            {
                yield return row;
            }
        }
    }

    private static IEnumerable<VNode> GetLooseRows(VNode table)
    {
        foreach (var child in table.Children)
        {
            if (child.Kind != VNodeKind.Element)
            {
                continue;
            }

            if (string.Equals(child.TagName, "thead", StringComparison.OrdinalIgnoreCase)
                || string.Equals(child.TagName, "tbody", StringComparison.OrdinalIgnoreCase)
                || string.Equals(child.TagName, "tfoot", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(child.TagName, "tr", StringComparison.OrdinalIgnoreCase))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<VNode> GetRows(VNode section)
    {
        foreach (var child in section.Children)
        {
            if (child.Kind == VNodeKind.Element && string.Equals(child.TagName, "tr", StringComparison.OrdinalIgnoreCase))
            {
                yield return child;
            }
        }
    }

    private static bool TryBuildRow(VNode rowNode, TranslationContext context, out RowData rowData)
    {
        var cells = new List<CellData>();

        foreach (var child in rowNode.Children)
        {
            if (child.Kind != VNodeKind.Element)
            {
                continue;
            }

            if (!string.Equals(child.TagName, "td", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(child.TagName, "th", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TranslationHelpers.TryConvertChildrenToBlockInlineRenderable(child.Children, context, out var renderable) || renderable is null)
            {
                rowData = default!;
                return false;
            }

            var alignmentAttribute = VdomSpectreTranslator.GetAttribute(child, "data-align");
            var alignment = ParseAlignment(alignmentAttribute);

            cells.Add(new CellData(renderable, alignment));
        }

        rowData = new RowData(cells.ToArray());
        return true;
    }

    private static TableBorder ResolveBorder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TableBorder.Rounded;
        }

        return BorderLookup.TryGetValue(value, out var border) ? border : TableBorder.Rounded;
    }

    private static Justify? ParseAlignment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToLowerInvariant() switch
        {
            "left" => Justify.Left,
            "center" or "centre" => Justify.Center,
            "right" => Justify.Right,
            _ => null,
        };
    }

    private static void AddRow(Table table, RowData rowData, int columnCount)
    {
        var cells = NormalizeCells(rowData.Cells, columnCount);
        table.AddRow(cells);
    }

    private static IRenderable[] NormalizeCells(CellData[] cells, int columnCount)
    {
        if (cells.Length == columnCount)
        {
            return cells.Select(cell => cell.Content).ToArray();
        }

        var buffer = new IRenderable[columnCount];
        var count = Math.Min(cells.Length, columnCount);

        for (var i = 0; i < count; i++)
        {
            buffer[i] = cells[i].Content;
        }

        for (var i = count; i < columnCount; i++)
        {
            buffer[i] = new Markup(string.Empty);
        }

        return buffer;
    }

    private sealed record CellData(IRenderable Content, Justify? Alignment);

    private sealed record RowData(CellData[] Cells)
    {
        public CellData? GetCell(int index)
        {
            if (index < 0 || index >= Cells.Length)
            {
                return null;
            }

            return Cells[index];
        }
    }
}

