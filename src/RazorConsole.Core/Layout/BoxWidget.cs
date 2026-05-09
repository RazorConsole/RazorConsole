// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

public enum BoxBorderStyle
{
    Square,
    Rounded,
    Double,
    Heavy,
    Ascii,
    None,
}

public readonly record struct BoxBorder(
    BoxBorderStyle Top,
    BoxBorderStyle Right,
    BoxBorderStyle Bottom,
    BoxBorderStyle Left)
{
    public static BoxBorder None { get; } = new(BoxBorderStyle.None);

    public static BoxBorder Square { get; } = new(BoxBorderStyle.Square);

    public BoxBorder(BoxBorderStyle border)
        : this(border, border, border, border)
    {
    }
}

public sealed class BoxWidget : Widget
{
    public BoxWidget(
        string vnodeId,
        Widget child,
        int paddingLeft = 0,
        int paddingTop = 0,
        int paddingRight = 0,
        int paddingBottom = 0,
        int? width = null,
        int? height = null,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0,
        bool expand = false,
        string? title = null,
        BoxBorderStyle border = BoxBorderStyle.None,
        BoxBorderStyle? borderTop = null,
        BoxBorderStyle? borderRight = null,
        BoxBorderStyle? borderBottom = null,
        BoxBorderStyle? borderLeft = null,
        Style? borderStyle = null)
        : base(vnodeId, key, attributes, [child], zIndex)
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

        if (height is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive when specified.");
        }

        PaddingLeft = paddingLeft;
        PaddingTop = paddingTop;
        PaddingRight = paddingRight;
        PaddingBottom = paddingBottom;
        Width = width;
        Height = height;
        Expand = expand;
        Title = string.IsNullOrWhiteSpace(title) ? null : title;
        Border = new BoxBorder(
            borderTop ?? border,
            borderRight ?? border,
            borderBottom ?? border,
            borderLeft ?? border);
        BorderStyle = borderStyle;
    }

    public Widget Child => Children[0];

    public int PaddingLeft { get; }

    public int PaddingTop { get; }

    public int PaddingRight { get; }

    public int PaddingBottom { get; }

    public int? Width { get; }

    public int? Height { get; }

    public bool Expand { get; }

    public string? Title { get; }

    public BoxBorder Border { get; }

    public Style? BorderStyle { get; }

    private int BorderLeftThickness => Border.Left == BoxBorderStyle.None ? 0 : 1;

    private int BorderTopThickness => Border.Top == BoxBorderStyle.None ? 0 : 1;

    private int BorderRightThickness => Border.Right == BoxBorderStyle.None ? 0 : 1;

    private int BorderBottomThickness => Border.Bottom == BoxBorderStyle.None ? 0 : 1;

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        var leftInset = BorderLeftThickness + PaddingLeft;
        var topInset = BorderTopThickness + PaddingTop;
        var rightInset = BorderRightThickness + PaddingRight;
        var bottomInset = BorderBottomThickness + PaddingBottom;
        var childConstraints = constraints.Deflate(leftInset, topInset, rightInset, bottomInset);
        var childSize = Child.Measure(context, childConstraints);

        var width = Width ?? (Expand ? constraints.MaxWidth : childSize.Width + leftInset + rightInset);
        if (Title is not null && Border.Top != BoxBorderStyle.None)
        {
            width = Math.Max(width, Segment.CellCount([new Segment(Title)]) + 4);
        }

        var height = Height ?? childSize.Height + topInset + bottomInset;
        return constraints.Constrain(new LayoutSize(width, height));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var leftInset = BorderLeftThickness + PaddingLeft;
        var topInset = BorderTopThickness + PaddingTop;
        var rightInset = BorderRightThickness + PaddingRight;
        var bottomInset = BorderBottomThickness + PaddingBottom;
        var childX = bounds.X + leftInset;
        var childY = bounds.Y + topInset;
        var availableWidth = Math.Max(0, bounds.Width - leftInset - rightInset);
        var availableHeight = Math.Max(0, bounds.Height - topInset - bottomInset);
        var expandChild = IsExpanding(Child);
        var childWidth = expandChild ? availableWidth : Math.Min(Child.DesiredSize.Width, availableWidth);
        var childHeight = expandChild ? availableHeight : Math.Min(Child.DesiredSize.Height, availableHeight);
        Child.Arrange(context, new LayoutRect(childX, childY, childWidth, childHeight));
    }

    protected override void PaintCore(PaintContext context)
    {
        if (Bounds.IsEmpty)
        {
            return;
        }

        PaintBorder(context.Canvas);
        Child.Paint(context);
    }

    private void PaintBorder(TerminalCanvas canvas)
    {
        var right = Bounds.Right - 1;
        var bottom = Bounds.Bottom - 1;
        var hasTop = Border.Top != BoxBorderStyle.None;
        var hasRight = Border.Right != BoxBorderStyle.None;
        var hasBottom = Border.Bottom != BoxBorderStyle.None;
        var hasLeft = Border.Left != BoxBorderStyle.None;

        if (hasTop)
        {
            var chars = ResolveBorderChars(Border.Top);
            canvas.Fill(new LayoutRect(Bounds.X, Bounds.Y, Bounds.Width, 1), chars.Horizontal, BorderStyle);
        }

        if (hasBottom)
        {
            var chars = ResolveBorderChars(Border.Bottom);
            canvas.Fill(new LayoutRect(Bounds.X, bottom, Bounds.Width, 1), chars.Horizontal, BorderStyle);
        }

        if (hasLeft)
        {
            var chars = ResolveBorderChars(Border.Left);
            var y = Bounds.Y + (hasTop ? 1 : 0);
            var height = Math.Max(0, Bounds.Height - (hasTop ? 1 : 0) - (hasBottom ? 1 : 0));
            canvas.Fill(new LayoutRect(Bounds.X, y, 1, height), chars.Vertical, BorderStyle);
        }

        if (hasRight)
        {
            var chars = ResolveBorderChars(Border.Right);
            var y = Bounds.Y + (hasTop ? 1 : 0);
            var height = Math.Max(0, Bounds.Height - (hasTop ? 1 : 0) - (hasBottom ? 1 : 0));
            canvas.Fill(new LayoutRect(right, y, 1, height), chars.Vertical, BorderStyle);
        }

        PaintCorners(canvas, right, bottom, hasTop, hasRight, hasBottom, hasLeft);
        PaintTitle(canvas);
    }

    private void PaintCorners(TerminalCanvas canvas, int right, int bottom, bool hasTop, bool hasRight, bool hasBottom, bool hasLeft)
    {
        if (hasTop && hasLeft)
        {
            canvas.Write(Bounds.X, Bounds.Y, ResolveBorderChars(Border.Top).TopLeft.ToString(), BorderStyle);
        }

        if (hasTop && hasRight)
        {
            canvas.Write(right, Bounds.Y, ResolveBorderChars(Border.Top).TopRight.ToString(), BorderStyle);
        }

        if (hasBottom && hasLeft)
        {
            canvas.Write(Bounds.X, bottom, ResolveBorderChars(Border.Bottom).BottomLeft.ToString(), BorderStyle);
        }

        if (hasBottom && hasRight)
        {
            canvas.Write(right, bottom, ResolveBorderChars(Border.Bottom).BottomRight.ToString(), BorderStyle);
        }
    }

    private void PaintTitle(TerminalCanvas canvas)
    {
        if (Title is null || Border.Top == BoxBorderStyle.None || Bounds.Width <= 4)
        {
            return;
        }

        var maxTitleWidth = Math.Max(0, Bounds.Width - 4);
        canvas.Write(Bounds.X + 2, Bounds.Y, Title, maxTitleWidth, BorderStyle);
    }

    private static BorderChars ResolveBorderChars(BoxBorderStyle border)
        => border switch
        {
            BoxBorderStyle.Rounded => new BorderChars('─', '│', '╭', '╮', '╰', '╯'),
            BoxBorderStyle.Double => new BorderChars('═', '║', '╔', '╗', '╚', '╝'),
            BoxBorderStyle.Heavy => new BorderChars('━', '┃', '┏', '┓', '┗', '┛'),
            BoxBorderStyle.Ascii => new BorderChars('-', '|', '+', '+', '+', '+'),
            _ => new BorderChars('─', '│', '┌', '┐', '└', '┘'),
        };

    private static bool IsExpanding(Widget child)
        => child.Attributes.TryGetValue("data-expand", out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private readonly record struct BorderChars(
        char Horizontal,
        char Vertical,
        char TopLeft,
        char TopRight,
        char BottomLeft,
        char BottomRight);
}