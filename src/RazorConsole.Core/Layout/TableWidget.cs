// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;

namespace RazorConsole.Core.Layout;

public enum TableWidgetBorderStyle
{
    None,
    Rounded,
    Square,
    Ascii,
}

public sealed class TableWidget : Widget
{
    private readonly TableWidgetCell[] _allCells;
    private int[] _columnWidths = [];
    private int[] _headerRowHeights = [];
    private int[] _bodyRowHeights = [];

    public TableWidget(
        string vnodeId,
        IReadOnlyList<TableWidgetRow> headerRows,
        IReadOnlyList<TableWidgetRow> bodyRows,
        TableWidgetBorderStyle border = TableWidgetBorderStyle.Rounded,
        bool expand = false,
        bool showHeaders = true,
        int? width = null,
        Style? borderStyle = null,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
        : base(vnodeId, key, attributes, headerRows.Concat(bodyRows).SelectMany(row => row.Cells).Select(cell => cell.Child).ToArray(), zIndex)
    {
        if (width is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive when specified.");
        }

        HeaderRows = headerRows ?? throw new ArgumentNullException(nameof(headerRows));
        BodyRows = bodyRows ?? throw new ArgumentNullException(nameof(bodyRows));
        Border = border;
        Expand = expand;
        ShowHeaders = showHeaders;
        Width = width;
        BorderStyle = borderStyle;
        _allCells = HeaderRows.Concat(BodyRows).SelectMany(row => row.Cells).ToArray();
    }

    public IReadOnlyList<TableWidgetRow> HeaderRows { get; }

    public IReadOnlyList<TableWidgetRow> BodyRows { get; }

    public TableWidgetBorderStyle Border { get; }

    public bool Expand { get; }

    public bool ShowHeaders { get; }

    public int? Width { get; }

    public Style? BorderStyle { get; }

    private int ColumnCount => Math.Max(
        HeaderRows.Select(row => row.Cells.Count).DefaultIfEmpty(0).Max(),
        BodyRows.Select(row => row.Cells.Count).DefaultIfEmpty(0).Max());

    private bool HasBorder => Border != TableWidgetBorderStyle.None;

    private bool HasHeaderSeparator => HasBorder && ShowHeaders && HeaderRows.Count > 0;

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        if (ColumnCount == 0 || constraints.MaxWidth == 0 || constraints.MaxHeight == 0)
        {
            return constraints.Constrain(LayoutSize.Empty);
        }

        MeasureCells(context, constraints);
        var preferredColumns = CalculatePreferredColumnWidths();
        var desiredWidth = Width ?? (Expand ? constraints.MaxWidth : CalculateTotalWidth(preferredColumns));
        desiredWidth = Math.Min(desiredWidth, constraints.MaxWidth);
        _columnWidths = ResolveColumnWidths(preferredColumns, desiredWidth);
        _headerRowHeights = CalculateRowHeights(HeaderRows, _columnWidths);
        _bodyRowHeights = CalculateRowHeights(BodyRows, _columnWidths);

        var desiredHeight = CalculateTotalHeight(_headerRowHeights, _bodyRowHeights);
        return constraints.Constrain(new LayoutSize(CalculateTotalWidth(_columnWidths), desiredHeight));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        if (ColumnCount == 0 || bounds.IsEmpty)
        {
            return;
        }

        if (_columnWidths.Length != ColumnCount)
        {
            MeasureCells(context, new BoxConstraints(0, bounds.Width, 0, bounds.Height));
        }

        _columnWidths = ResolveColumnWidths(CalculatePreferredColumnWidths(), bounds.Width);
        _headerRowHeights = CalculateRowHeights(HeaderRows, _columnWidths);
        _bodyRowHeights = CalculateRowHeights(BodyRows, _columnWidths);

        var y = bounds.Y + (HasBorder ? 1 : 0);
        ArrangeRows(context, HeaderRows, _headerRowHeights, bounds, ref y);
        if (HasHeaderSeparator)
        {
            y++;
        }

        ArrangeRows(context, BodyRows, _bodyRowHeights, bounds, ref y);
    }

    protected override void PaintCore(PaintContext context)
    {
        if (Bounds.IsEmpty)
        {
            return;
        }

        if (HasBorder)
        {
            PaintBorder(context.Canvas);
        }

        foreach (var cell in _allCells)
        {
            cell.Child.Paint(context);
        }
    }

    public override LayoutBox CreateLayoutBox()
    {
        var headerBoxes = CreateRowLayoutBoxes(HeaderRows, "thead");
        var bodyBoxes = CreateRowLayoutBoxes(BodyRows, "tbody");
        return new LayoutBox(VNodeId, Bounds, ZIndex, headerBoxes.Concat(bodyBoxes).ToArray());
    }

    private void MeasureCells(LayoutContext context, BoxConstraints constraints)
    {
        foreach (var cell in _allCells)
        {
            cell.Child.Measure(context, new BoxConstraints(0, constraints.MaxWidth, 0, constraints.MaxHeight));
        }
    }

    private int[] CalculatePreferredColumnWidths()
    {
        var widths = new int[ColumnCount];
        foreach (var row in HeaderRows.Concat(BodyRows))
        {
            for (var column = 0; column < row.Cells.Count; column++)
            {
                var cell = row.Cells[column];
                widths[column] = Math.Max(widths[column], cell.Width ?? cell.Child.DesiredSize.Width + cell.PaddingLeft + cell.PaddingRight);
            }
        }

        for (var column = 0; column < widths.Length; column++)
        {
            widths[column] = Math.Max(1, widths[column]);
        }

        return widths;
    }

    private int[] ResolveColumnWidths(int[] preferredColumns, int targetWidth)
    {
        var available = Math.Max(0, targetWidth - VerticalLineCount);
        if (preferredColumns.Length == 0 || available == 0)
        {
            return new int[preferredColumns.Length];
        }

        var widths = preferredColumns.ToArray();
        var preferred = widths.Sum();
        if (available >= preferred)
        {
            var remaining = available - preferred;
            var column = 0;
            while (remaining > 0)
            {
                widths[column % widths.Length]++;
                column++;
                remaining--;
            }

            return widths;
        }

        while (widths.Sum() > available && widths.Any(width => width > 1))
        {
            var widestIndex = 0;
            for (var i = 1; i < widths.Length; i++)
            {
                if (widths[i] > widths[widestIndex])
                {
                    widestIndex = i;
                }
            }

            widths[widestIndex]--;
        }

        return widths;
    }

    private int[] CalculateRowHeights(IReadOnlyList<TableWidgetRow> rows, int[] columnWidths)
    {
        var heights = new int[rows.Count];
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var height = 1;
            for (var column = 0; column < row.Cells.Count && column < columnWidths.Length; column++)
            {
                var cell = row.Cells[column];
                height = Math.Max(height, cell.Child.DesiredSize.Height + cell.PaddingTop + cell.PaddingBottom);
            }

            heights[rowIndex] = height;
        }

        return heights;
    }

    private void ArrangeRows(LayoutContext context, IReadOnlyList<TableWidgetRow> rows, int[] rowHeights, LayoutRect tableBounds, ref int y)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rowHeight = rowHeights[rowIndex];
            var x = tableBounds.X + (HasBorder ? 1 : 0);
            row.Bounds = new LayoutRect(tableBounds.X, y, tableBounds.Width, rowHeight);

            for (var column = 0; column < row.Cells.Count && column < _columnWidths.Length; column++)
            {
                var columnWidth = _columnWidths[column];
                var cell = row.Cells[column];
                cell.Bounds = new LayoutRect(x, y, columnWidth, rowHeight);
                ArrangeCell(context, cell);
                x += columnWidth + (HasBorder ? 1 : 0);
            }

            y += rowHeight;
        }
    }

    private static void ArrangeCell(LayoutContext context, TableWidgetCell cell)
    {
        var contentWidth = Math.Max(0, cell.Bounds.Width - cell.PaddingLeft - cell.PaddingRight);
        var contentHeight = Math.Max(0, cell.Bounds.Height - cell.PaddingTop - cell.PaddingBottom);
        var childWidth = Math.Min(contentWidth, cell.Child.DesiredSize.Width);
        var childHeight = Math.Min(contentHeight, cell.Child.DesiredSize.Height);
        var horizontalOffset = cell.Alignment switch
        {
            HorizontalAlignment.Center => Math.Max(0, (contentWidth - childWidth) / 2),
            HorizontalAlignment.Right => Math.Max(0, contentWidth - childWidth),
            _ => 0,
        };
        var childX = cell.Bounds.X + cell.PaddingLeft + horizontalOffset;
        var childY = cell.Bounds.Y + cell.PaddingTop;
        cell.Child.Arrange(context, new LayoutRect(childX, childY, childWidth, childHeight));
    }

    private void PaintBorder(TerminalCanvas canvas)
    {
        var chars = ResolveBorderChars(Border);
        var right = Bounds.Right - 1;
        var bottom = Bounds.Bottom - 1;

        if (Bounds.Width == 1 || Bounds.Height == 1)
        {
            canvas.Fill(Bounds, chars.Horizontal, BorderStyle);
            return;
        }

        for (var y = Bounds.Y + 1; y < bottom; y++)
        {
            canvas.Write(Bounds.X, y, chars.Vertical.ToString(), BorderStyle);
            canvas.Write(right, y, chars.Vertical.ToString(), BorderStyle);

            var x = Bounds.X;
            for (var column = 0; column < _columnWidths.Length - 1; column++)
            {
                x += _columnWidths[column] + 1;
                canvas.Write(x, y, chars.Vertical.ToString(), BorderStyle);
            }
        }

        PaintHorizontalBorder(canvas, Bounds.Y, chars.TopLeft, chars.TopJoin, chars.TopRight, chars.Horizontal);
        if (HasHeaderSeparator)
        {
            var separatorY = Bounds.Y + 1 + _headerRowHeights.Sum();
            if (separatorY < bottom)
            {
                PaintHorizontalBorder(canvas, separatorY, chars.LeftJoin, chars.CenterJoin, chars.RightJoin, chars.Horizontal);
            }
        }

        PaintHorizontalBorder(canvas, bottom, chars.BottomLeft, chars.BottomJoin, chars.BottomRight, chars.Horizontal);
    }

    private void PaintHorizontalBorder(TerminalCanvas canvas, int y, char left, char join, char right, char horizontal)
    {
        canvas.Write(Bounds.X, y, left.ToString(), BorderStyle);
        var x = Bounds.X + 1;
        for (var column = 0; column < _columnWidths.Length; column++)
        {
            canvas.Fill(new LayoutRect(x, y, _columnWidths[column], 1), horizontal, BorderStyle);
            x += _columnWidths[column];
            canvas.Write(x, y, column == _columnWidths.Length - 1 ? right.ToString() : join.ToString(), BorderStyle);
            x++;
        }
    }

    private IEnumerable<LayoutBox> CreateRowLayoutBoxes(IReadOnlyList<TableWidgetRow> rows, string sectionName)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            yield return new LayoutBox(
                $"{VNodeId}:{sectionName}:{rowIndex}",
                row.Bounds,
                ZIndex,
                row.Cells.Select(cell => new LayoutBox(cell.VNodeId, cell.Bounds, ZIndex, [cell.Child.CreateLayoutBox()])).ToArray());
        }
    }

    private int CalculateTotalWidth(int[] columnWidths)
        => columnWidths.Sum() + VerticalLineCount;

    private int CalculateTotalHeight(int[] headerRowHeights, int[] bodyRowHeights)
        => headerRowHeights.Sum()
            + bodyRowHeights.Sum()
            + (HasBorder ? 2 : 0)
            + (HasHeaderSeparator ? 1 : 0);

    private int VerticalLineCount => HasBorder ? ColumnCount + 1 : 0;

    private static TableBorderChars ResolveBorderChars(TableWidgetBorderStyle border)
        => border switch
        {
            TableWidgetBorderStyle.Ascii => new TableBorderChars('-', '|', '+', '+', '+', '+', '+', '+', '+', '+', '+'),
            TableWidgetBorderStyle.Square => new TableBorderChars('─', '│', '┌', '┬', '┐', '├', '┼', '┤', '└', '┴', '┘'),
            _ => new TableBorderChars('─', '│', '╭', '┬', '╮', '├', '┼', '┤', '╰', '┴', '╯'),
        };

    private readonly record struct TableBorderChars(
        char Horizontal,
        char Vertical,
        char TopLeft,
        char TopJoin,
        char TopRight,
        char LeftJoin,
        char CenterJoin,
        char RightJoin,
        char BottomLeft,
        char BottomJoin,
        char BottomRight);
}

public sealed class TableWidgetRow
{
    public TableWidgetRow(IReadOnlyList<TableWidgetCell> cells)
    {
        Cells = cells ?? throw new ArgumentNullException(nameof(cells));
    }

    public IReadOnlyList<TableWidgetCell> Cells { get; }

    public LayoutRect Bounds { get; internal set; }
}

public sealed class TableWidgetCell
{
    public TableWidgetCell(
        string vnodeId,
        Widget child,
        HorizontalAlignment alignment = HorizontalAlignment.Left,
        int paddingLeft = 1,
        int paddingTop = 0,
        int paddingRight = 1,
        int paddingBottom = 0,
        int? width = null)
    {
        if (paddingLeft < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingLeft), "Padding cannot be negative.");
        }

        if (paddingTop < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingTop), "Padding cannot be negative.");
        }

        if (paddingRight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingRight), "Padding cannot be negative.");
        }

        if (paddingBottom < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingBottom), "Padding cannot be negative.");
        }

        if (width is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive when specified.");
        }

        VNodeId = vnodeId ?? throw new ArgumentNullException(nameof(vnodeId));
        Child = child ?? throw new ArgumentNullException(nameof(child));
        Alignment = alignment;
        PaddingLeft = paddingLeft;
        PaddingTop = paddingTop;
        PaddingRight = paddingRight;
        PaddingBottom = paddingBottom;
        Width = width;
    }

    public string VNodeId { get; }

    public Widget Child { get; }

    public HorizontalAlignment Alignment { get; }

    public int PaddingLeft { get; }

    public int PaddingTop { get; }

    public int PaddingRight { get; }

    public int PaddingBottom { get; }

    public int? Width { get; }

    public LayoutRect Bounds { get; internal set; }
}
