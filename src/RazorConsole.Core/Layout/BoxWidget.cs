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
        int marginLeft = 0,
        int marginTop = 0,
        int marginRight = 0,
        int marginBottom = 0,
        int? width = null,
        int? height = null,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0,
        bool expand = false,
        bool fillWidth = false,
        bool fillHeight = false,
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

        if (marginLeft < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marginLeft), "Margin cannot be negative.");
        }

        if (marginTop < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marginTop), "Margin cannot be negative.");
        }

        if (marginRight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marginRight), "Margin cannot be negative.");
        }

        if (marginBottom < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marginBottom), "Margin cannot be negative.");
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
        MarginLeft = marginLeft;
        MarginTop = marginTop;
        MarginRight = marginRight;
        MarginBottom = marginBottom;
        Width = width;
        Height = height;
        Expand = expand;
        FillWidth = fillWidth || expand;
        FillHeight = fillHeight;
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

    public int MarginLeft { get; }

    public int MarginTop { get; }

    public int MarginRight { get; }

    public int MarginBottom { get; }

    public int? Width { get; }

    public int? Height { get; }

    public bool Expand { get; }

    public bool FillWidth { get; }

    public bool FillHeight { get; }

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
        var outerHorizontalInset = MarginLeft + MarginRight;
        var outerVerticalInset = MarginTop + MarginBottom;
        var childConstraints = constraints.Deflate(
            MarginLeft + leftInset,
            MarginTop + topInset,
            MarginRight + rightInset,
            MarginBottom + bottomInset);
        var childSize = Child.Measure(context, childConstraints);

        var contentWidth = childSize.Width + leftInset + rightInset;
        var contentHeight = childSize.Height + topInset + bottomInset;
        var width = Width ?? (FillWidth ? Math.Max(0, constraints.MaxWidth - outerHorizontalInset) : contentWidth);
        if (Title is not null && Border.Top != BoxBorderStyle.None)
        {
            width = Math.Max(width, Segment.CellCount([new Segment(Title)]) + 4);
        }

        var height = Height ?? (FillHeight ? Math.Max(0, constraints.MaxHeight - outerVerticalInset) : contentHeight);
        return constraints.Constrain(new LayoutSize(
            width + outerHorizontalInset,
            height + outerVerticalInset));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var leftInset = BorderLeftThickness + PaddingLeft;
        var topInset = BorderTopThickness + PaddingTop;
        var rightInset = BorderRightThickness + PaddingRight;
        var bottomInset = BorderBottomThickness + PaddingBottom;
        var contentBounds = GetContentBounds(bounds);
        var childX = contentBounds.X + leftInset;
        var childY = contentBounds.Y + topInset;
        var availableWidth = Math.Max(0, contentBounds.Width - leftInset - rightInset);
        var availableHeight = Math.Max(0, contentBounds.Height - topInset - bottomInset);
        var childWidth = FillsWidth(Child) ? availableWidth : Math.Min(Child.DesiredSize.Width, availableWidth);
        var childHeight = FillsHeight(Child) ? availableHeight : Math.Min(Child.DesiredSize.Height, availableHeight);
        Child.Arrange(context, new LayoutRect(childX, childY, childWidth, childHeight));
    }

    protected override void PaintCore(PaintContext context)
    {
        var contentBounds = GetContentBounds(Bounds);
        if (contentBounds.IsEmpty)
        {
            return;
        }

        PaintBorder(context.Canvas, contentBounds);
        Child.Paint(context);
    }

    private LayoutRect GetContentBounds(LayoutRect bounds)
        => new(
            bounds.X + MarginLeft,
            bounds.Y + MarginTop,
            Math.Max(0, bounds.Width - MarginLeft - MarginRight),
            Math.Max(0, bounds.Height - MarginTop - MarginBottom));

    private void PaintBorder(TerminalCanvas canvas, LayoutRect bounds)
    {
        var right = bounds.Right - 1;
        var bottom = bounds.Bottom - 1;
        var hasTop = Border.Top != BoxBorderStyle.None;
        var hasRight = Border.Right != BoxBorderStyle.None;
        var hasBottom = Border.Bottom != BoxBorderStyle.None;
        var hasLeft = Border.Left != BoxBorderStyle.None;

        if (hasTop)
        {
            var chars = ResolveBorderChars(Border.Top);
            canvas.Fill(new LayoutRect(bounds.X, bounds.Y, bounds.Width, 1), chars.Horizontal, BorderStyle);
        }

        if (hasBottom)
        {
            var chars = ResolveBorderChars(Border.Bottom);
            canvas.Fill(new LayoutRect(bounds.X, bottom, bounds.Width, 1), chars.Horizontal, BorderStyle);
        }

        if (hasLeft)
        {
            var chars = ResolveBorderChars(Border.Left);
            var y = bounds.Y + (hasTop ? 1 : 0);
            var height = Math.Max(0, bounds.Height - (hasTop ? 1 : 0) - (hasBottom ? 1 : 0));
            canvas.Fill(new LayoutRect(bounds.X, y, 1, height), chars.Vertical, BorderStyle);
        }

        if (hasRight)
        {
            var chars = ResolveBorderChars(Border.Right);
            var y = bounds.Y + (hasTop ? 1 : 0);
            var height = Math.Max(0, bounds.Height - (hasTop ? 1 : 0) - (hasBottom ? 1 : 0));
            canvas.Fill(new LayoutRect(right, y, 1, height), chars.Vertical, BorderStyle);
        }

        PaintCorners(canvas, bounds, right, bottom, hasTop, hasRight, hasBottom, hasLeft);
        PaintTitle(canvas, bounds);
    }

    private void PaintCorners(TerminalCanvas canvas, LayoutRect bounds, int right, int bottom, bool hasTop, bool hasRight, bool hasBottom, bool hasLeft)
    {
        if (hasTop && hasLeft)
        {
            canvas.Write(bounds.X, bounds.Y, ResolveBorderChars(Border.Top).TopLeft.ToString(), BorderStyle);
        }

        if (hasTop && hasRight)
        {
            canvas.Write(right, bounds.Y, ResolveBorderChars(Border.Top).TopRight.ToString(), BorderStyle);
        }

        if (hasBottom && hasLeft)
        {
            canvas.Write(bounds.X, bottom, ResolveBorderChars(Border.Bottom).BottomLeft.ToString(), BorderStyle);
        }

        if (hasBottom && hasRight)
        {
            canvas.Write(right, bottom, ResolveBorderChars(Border.Bottom).BottomRight.ToString(), BorderStyle);
        }
    }

    private void PaintTitle(TerminalCanvas canvas, LayoutRect bounds)
    {
        if (Title is null || Border.Top == BoxBorderStyle.None || bounds.Width <= 4)
        {
            return;
        }

        var maxTitleWidth = Math.Max(0, bounds.Width - 4);
        canvas.Write(bounds.X + 2, bounds.Y, Title, maxTitleWidth, BorderStyle);
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

    private static bool FillsWidth(Widget child)
        => IsTruthy(child, "data-expand") || IsTruthy(child, "data-fill-width");

    private static bool FillsHeight(Widget child)
        => IsTruthy(child, "data-expand") || IsTruthy(child, "data-fill-height");

    private static bool IsTruthy(Widget child, string name)
        => child.Attributes.TryGetValue(name, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private readonly record struct BorderChars(
        char Horizontal,
        char Vertical,
        char TopLeft,
        char TopRight,
        char BottomLeft,
        char BottomRight);
}
