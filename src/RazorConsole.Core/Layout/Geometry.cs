// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Layout;

public readonly record struct LayoutSize
{
    public static LayoutSize Empty { get; } = new(0, 0);

    public int Width { get; }

    public int Height { get; }

    public LayoutSize(int width, int height)
    {
        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width cannot be negative.");
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height cannot be negative.");
        }

        Width = width;
        Height = height;
    }
}

public readonly record struct LayoutPoint(int X, int Y);

public readonly record struct LayoutRect
{
    public static LayoutRect Empty { get; } = new(0, 0, 0, 0);

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public LayoutRect(int x, int y, int width, int height)
    {
        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width cannot be negative.");
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height cannot be negative.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width == 0 || Height == 0;

    public bool Contains(LayoutPoint point)
        => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public LayoutRect Intersect(LayoutRect other)
    {
        var x1 = Math.Max(X, other.X);
        var y1 = Math.Max(Y, other.Y);
        var x2 = Math.Min(Right, other.Right);
        var y2 = Math.Min(Bottom, other.Bottom);

        if (x2 <= x1 || y2 <= y1)
        {
            return Empty;
        }

        return new LayoutRect(x1, y1, x2 - x1, y2 - y1);
    }
}

public readonly record struct BoxConstraints
{
    public int MinWidth { get; }

    public int MaxWidth { get; }

    public int MinHeight { get; }

    public int MaxHeight { get; }

    public BoxConstraints(int minWidth, int maxWidth, int minHeight, int maxHeight)
    {
        if (minWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minWidth), "Minimum width cannot be negative.");
        }

        if (maxWidth < minWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Maximum width cannot be smaller than minimum width.");
        }

        if (minHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minHeight), "Minimum height cannot be negative.");
        }

        if (maxHeight < minHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHeight), "Maximum height cannot be smaller than minimum height.");
        }

        MinWidth = minWidth;
        MaxWidth = maxWidth;
        MinHeight = minHeight;
        MaxHeight = maxHeight;
    }

    public LayoutSize Constrain(LayoutSize size)
        => new(
            Math.Clamp(size.Width, MinWidth, MaxWidth),
            Math.Clamp(size.Height, MinHeight, MaxHeight));

    public BoxConstraints Deflate(int left, int top, int right, int bottom)
    {
        if (left < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(left), "Inset cannot be negative.");
        }

        if (top < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "Inset cannot be negative.");
        }

        if (right < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(right), "Inset cannot be negative.");
        }

        if (bottom < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bottom), "Inset cannot be negative.");
        }

        var horizontal = left + right;
        var vertical = top + bottom;

        return new BoxConstraints(
            Math.Max(0, MinWidth - horizontal),
            Math.Max(0, MaxWidth - horizontal),
            Math.Max(0, MinHeight - vertical),
            Math.Max(0, MaxHeight - vertical));
    }
}
