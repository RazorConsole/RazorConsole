// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout;

public enum PanelBorderStyle
{
    Square,
    Rounded,
    Double,
    Heavy,
    Ascii,
    None,
}

public sealed class PanelWidget : Widget
{
    public PanelWidget(
        string vnodeId,
        Widget child,
        string? title = null,
        PanelBorderStyle border = PanelBorderStyle.Square,
        int paddingLeft = 0,
        int paddingTop = 0,
        int paddingRight = 0,
        int paddingBottom = 0,
        int? width = null,
        int? height = null,
        bool expand = false,
        Style? borderStyle = null,
        string? key = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        int zIndex = 0)
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

        Title = string.IsNullOrWhiteSpace(title) ? null : title;
        Border = border;
        PaddingLeft = paddingLeft;
        PaddingTop = paddingTop;
        PaddingRight = paddingRight;
        PaddingBottom = paddingBottom;
        Width = width;
        Height = height;
        Expand = expand;
        BorderStyle = borderStyle;
    }

    public Widget Child => Children[0];

    public string? Title { get; }

    public PanelBorderStyle Border { get; }

    public int PaddingLeft { get; }

    public int PaddingTop { get; }

    public int PaddingRight { get; }

    public int PaddingBottom { get; }

    public int? Width { get; }

    public int? Height { get; }

    public bool Expand { get; }

    public Style? BorderStyle { get; }

    private int BorderThickness => Border == PanelBorderStyle.None ? 0 : 1;

    protected override LayoutSize MeasureCore(LayoutContext context, BoxConstraints constraints)
    {
        var leftInset = BorderThickness + PaddingLeft;
        var topInset = BorderThickness + PaddingTop;
        var rightInset = BorderThickness + PaddingRight;
        var bottomInset = BorderThickness + PaddingBottom;
        var childConstraints = constraints.Deflate(leftInset, topInset, rightInset, bottomInset);
        var childSize = Child.Measure(context, childConstraints);

        var desiredWidth = Width ?? (Expand ? constraints.MaxWidth : childSize.Width + leftInset + rightInset);
        if (Title is not null && BorderThickness > 0)
        {
            desiredWidth = Math.Max(desiredWidth, Segment.CellCount([new Segment(Title)]) + 4);
        }

        var desiredHeight = Height ?? childSize.Height + topInset + bottomInset;
        return constraints.Constrain(new LayoutSize(desiredWidth, desiredHeight));
    }

    protected override void ArrangeCore(LayoutContext context, LayoutRect bounds)
    {
        var leftInset = BorderThickness + PaddingLeft;
        var topInset = BorderThickness + PaddingTop;
        var rightInset = BorderThickness + PaddingRight;
        var bottomInset = BorderThickness + PaddingBottom;

        var availableWidth = Math.Max(0, bounds.Width - leftInset - rightInset);
        var availableHeight = Math.Max(0, bounds.Height - topInset - bottomInset);
        var childWidth = Math.Min(Child.DesiredSize.Width, availableWidth);
        var childHeight = Math.Min(Child.DesiredSize.Height, availableHeight);
        Child.Arrange(context, new LayoutRect(bounds.X + leftInset, bounds.Y + topInset, childWidth, childHeight));
    }

    protected override void PaintCore(PaintContext context)
    {
        if (Bounds.IsEmpty)
        {
            return;
        }

        if (BorderThickness > 0)
        {
            PaintBorder(context.Canvas);
        }

        Child.Paint(context);
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

        canvas.Write(Bounds.X, Bounds.Y, chars.TopLeft.ToString(), BorderStyle);
        canvas.Write(right, Bounds.Y, chars.TopRight.ToString(), BorderStyle);
        canvas.Write(Bounds.X, bottom, chars.BottomLeft.ToString(), BorderStyle);
        canvas.Write(right, bottom, chars.BottomRight.ToString(), BorderStyle);

        if (Bounds.Width > 2)
        {
            canvas.Fill(new LayoutRect(Bounds.X + 1, Bounds.Y, Bounds.Width - 2, 1), chars.Horizontal, BorderStyle);
            canvas.Fill(new LayoutRect(Bounds.X + 1, bottom, Bounds.Width - 2, 1), chars.Horizontal, BorderStyle);
        }

        if (Bounds.Height > 2)
        {
            canvas.Fill(new LayoutRect(Bounds.X, Bounds.Y + 1, 1, Bounds.Height - 2), chars.Vertical, BorderStyle);
            canvas.Fill(new LayoutRect(right, Bounds.Y + 1, 1, Bounds.Height - 2), chars.Vertical, BorderStyle);
        }

        PaintTitle(canvas);
    }

    private void PaintTitle(TerminalCanvas canvas)
    {
        if (Title is null || Bounds.Width <= 4)
        {
            return;
        }

        var maxTitleWidth = Math.Max(0, Bounds.Width - 4);
        canvas.Write(Bounds.X + 2, Bounds.Y, Title, maxTitleWidth, BorderStyle);
    }

    private static BorderChars ResolveBorderChars(PanelBorderStyle border)
        => border switch
        {
            PanelBorderStyle.Rounded => new BorderChars('─', '│', '╭', '╮', '╰', '╯'),
            PanelBorderStyle.Double => new BorderChars('═', '║', '╔', '╗', '╚', '╝'),
            PanelBorderStyle.Heavy => new BorderChars('━', '┃', '┏', '┓', '┗', '┛'),
            PanelBorderStyle.Ascii => new BorderChars('-', '|', '+', '+', '+', '+'),
            _ => new BorderChars('─', '│', '┌', '┐', '└', '┘'),
        };

    private readonly record struct BorderChars(
        char Horizontal,
        char Vertical,
        char TopLeft,
        char TopRight,
        char BottomLeft,
        char BottomRight);
}
