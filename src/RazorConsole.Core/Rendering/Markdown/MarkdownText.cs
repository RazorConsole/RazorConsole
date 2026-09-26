// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using RazorConsole.Core.Input;
using Spectre.Console;
using Table = Markdig.Extensions.Tables.Table;
using TableRow = Markdig.Extensions.Tables.TableRow;

namespace RazorConsole.Core.Rendering.Markdown;

/// <summary>Selectable plain text and UTF-16 style ranges rendered from Markdown.</summary>
public sealed record MarkdownText(string Text, IReadOnlyList<MarkdownText.StyleRange> Styles)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
    public sealed record StyleRange(int Start, int Length, Style Style);

    public static MarkdownText Parse(string markdown, string continuationPrefix = "", int width = 80)
    {
        var text = new StringBuilder();
        var styles = new List<StyleRange>();
        var availableWidth = Math.Max(1, width - TextSelectionState.CellWidth(continuationPrefix));
        void Append(string value, Style style)
        {
            styles.Add(new(text.Length, value.Length, style));
            text.Append(value);
        }
        void Inline(Inline inline, Style style)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    Append(literal.Content.ToString(), style);
                    break;
                case CodeInline code:
                    Append(code.Content, new Style(new Color(53, 132, 29), decoration: style.Decoration));
                    break;
                case LineBreakInline:
                    Append("\n" + continuationPrefix, style);
                    break;
                case HtmlInline html:
                    Append(html.Tag, style);
                    break;
                case ContainerInline container:
                    var nested = inline switch
                    {
                        EmphasisInline emphasis => new Style(style.Foreground, decoration: style.Decoration | (emphasis.DelimiterCount >= 2 ? Decoration.Bold : Decoration.Italic)),
                        LinkInline => new Style(new Color(17, 96, 220), decoration: Decoration.Underline),
                        _ => style,
                    };
                    foreach (var child in container)
                    {
                        Inline(child, nested);
                    }
                    // Keep the destination selectable and activatable by TextArea.
                    if (inline is LinkInline link && !string.IsNullOrEmpty(link.Url) && !text.ToString().EndsWith(link.Url, StringComparison.Ordinal))
                    {
                        Append($" ({link.Url})", nested);
                    }
                    break;
                default:
                    if (inline.Span.Start >= 0 && inline.Span.End < markdown.Length)
                    {
                        Append(markdown[inline.Span.Start..(inline.Span.End + 1)], style);
                    }
                    break;
            }
        }
        MarkdownText Cell(TableCell cell)
        {
            var start = text.Length;
            var styleStart = styles.Count;
            foreach (var leaf in cell.OfType<LeafBlock>())
            {
                if (leaf.Inline is not null)
                {
                    Inline(leaf.Inline, Style.Plain);
                }
            }
            var result = new MarkdownText(text.ToString(start, text.Length - start), styles.Skip(styleStart).Select(s => s with { Start = s.Start - start }).ToArray());
            text.Length = start;
            styles.RemoveRange(styleStart, styles.Count - styleStart);
            return result;
        }
        void Table(Table table)
        {
            var rows = table.OfType<TableRow>().ToArray();
            var cells = rows.Select(row => row.OfType<TableCell>().Select(Cell).ToArray()).ToArray();
            var columns = cells.Max(row => row.Length);
            var widths = Enumerable.Range(0, columns).Select(column => Math.Max(3, cells.Max(row => column < row.Length ? TextSelectionState.CellWidth(row[column].Text) : 0))).ToArray();
            var gap = 2;
            if (availableWidth < columns * 3 + gap * (columns - 1))
            {
                // Stack cells rather than clip columns beyond a narrow viewport.
                for (var r = 1; r < cells.Length; r++)
                {
                    for (var c = 0; c < cells[r].Length; c++)
                    {
                        if (text.Length > 0 && text[^1] == '\n')
                        {
                            text.Append(continuationPrefix);
                        }
                        Append((c < cells[0].Length ? cells[0][c].Text : $"Column {c + 1}") + ": ", new Style(new Color(166, 105, 21)));
                        var start = text.Length;
                        text.Append(cells[r][c].Text);
                        styles.AddRange(cells[r][c].Styles.Select(s => s with { Start = start + s.Start }));
                        text.Append('\n');
                    }
                    if (r < cells.Length - 1)
                    {
                        text.Append('\n');
                    }
                }
                return;
            }
            while (widths.Sum() + gap * (columns - 1) > availableWidth)
            {
                var largest = Array.IndexOf(widths, widths.Max());
                widths[largest]--;
            }
            for (var r = 0; r < cells.Length; r++)
            {
                var wrapped = Enumerable.Range(0, columns).Select(c =>
                {
                    var buffer = new TextSelectionState();
                    buffer.SetText(c < cells[r].Length ? cells[r][c].Text : "");
                    return buffer.Wrap(widths[c], wordWrap: true);
                }).ToArray();
                for (var line = 0; line < wrapped.Max(lines => lines.Count); line++)
                {
                    if (text.Length > 0 && text[^1] == '\n')
                    {
                        text.Append(continuationPrefix);
                    }
                    for (var c = 0; c < columns; c++)
                    {
                        var cell = c < cells[r].Length ? cells[r][c] : new MarkdownText("", []);
                        var row = line < wrapped[c].Count ? wrapped[c][line] : new TextSelectionState.TextRow(0, 0);
                        var value = cell.Text[row.Start..row.End];
                        var padding = Math.Max(0, widths[c] - TextSelectionState.CellWidth(value));
                        var alignment = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null;
                        var left = alignment == TableColumnAlign.Right ? padding : alignment == TableColumnAlign.Center ? padding / 2 : 0;
                        text.Append(' ', left);
                        var start = text.Length;
                        text.Append(value);
                        if (rows[r].IsHeader)
                        {
                            styles.Add(new(start, value.Length, new Style(new Color(166, 105, 21))));
                        }
                        else
                        {
                            foreach (var style in cell.Styles)
                            {
                                var begin = Math.Max(style.Start, row.Start);
                                var end = Math.Min(style.Start + style.Length, row.End);
                                if (end > begin)
                                {
                                    styles.Add(new(start + begin - row.Start, end - begin, style.Style));
                                }
                            }
                        }
                        text.Append(' ', padding - left + (c < columns - 1 ? gap : 0));
                    }
                    text.Append('\n');
                }
                if (r < cells.Length - 1)
                {
                    text.Append(continuationPrefix);
                    Append(string.Join(new string(' ', gap), widths.Select(w => new string(rows[r].IsHeader ? '━' : '─', w))), new Style(new Color(210, 210, 210)));
                    text.Append('\n');
                }
            }
        }
        void Block(Block block, int depth)
        {
            if (text.Length > 0 && text[^1] == '\n')
            {
                text.Append(continuationPrefix);
            }
            switch (block)
            {
                case Table table:
                    Table(table);
                    break;
                case CodeBlock code:
                    Append(code.Lines.ToString().Replace("\n", "\n" + continuationPrefix, StringComparison.Ordinal), new Style(new Color(53, 132, 29)));
                    text.Append('\n');
                    break;
                case LeafBlock leaf when leaf.Inline is not null:
                    var headingStyle = new Style(decoration: Decoration.Bold | Decoration.Italic);
                    if (block is HeadingBlock heading)
                    {
                        Append(new string('#', heading.Level) + " ", headingStyle);
                    }
                    Inline(leaf.Inline, block is HeadingBlock ? headingStyle : Style.Plain);
                    text.Append('\n');
                    break;
                case ContainerBlock container:
                    var number = container is ListBlock ordered && int.TryParse(ordered.OrderedStart, out var first) ? first : 1;
                    foreach (var child in container)
                    {
                        if (container is ListBlock list)
                        {
                            if (text.Length > 0 && text[^1] == '\n')
                            {
                                text.Append(continuationPrefix);
                            }
                            Append(new string(' ', depth * 2) + (list.IsOrdered ? $"{number++}. " : "• "), Style.Plain);
                        }
                        Block(child, depth + (container is ListBlock ? 1 : 0));
                    }
                    break;
                default:
                    if (block.Span.Start >= 0 && block.Span.End < markdown.Length)
                    {
                        Append(markdown[block.Span.Start..(block.Span.End + 1)], Style.Plain);
                    }
                    break;
            }
        }
        foreach (var block in Markdig.Markdown.Parse(markdown, Pipeline))
        {
            if (text.Length > 0)
            {
                text.Append("\n" + continuationPrefix);
            }
            Block(block, 0);
        }
        return new(text.ToString().TrimEnd('\n'), styles);
    }
}
