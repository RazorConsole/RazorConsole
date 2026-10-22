// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using System.Text.Json;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Layout;
using Spectre.Console;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Turns terminal cells into the protocol's screen shapes. Shared by the app's renderer screen and, through the CLI,
/// the PTY screen, so both describe cells the same way.
/// </summary>
internal static class DiagnosticsScreenReader
{
    public const string RendererSource = "renderer";

    private static readonly (Decoration Flag, string Name)[] Decorations =
    [
        (Decoration.Bold, "bold"),
        (Decoration.Dim, "dim"),
        (Decoration.Italic, "italic"),
        (Decoration.Underline, "underline"),
        (Decoration.Invert, "invert"),
        (Decoration.Conceal, "conceal"),
        (Decoration.SlowBlink, "slowBlink"),
        (Decoration.RapidBlink, "rapidBlink"),
        (Decoration.Strikethrough, "strikethrough"),
    ];

    /// <summary>
    /// Reads the latest painted canvas.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException">Nothing is painted, or the region is invalid.</exception>
    public static ScreenResult Read(DiagnosticsState state, ConsoleAppOptions options, ScreenParams parameters)
    {
        if (state.LatestScreen is not { } screen)
        {
            var reason = options.RenderingPipeline == RazorConsoleRenderingPipeline.WidgetLayout
                ? DiagnosticsUnavailableReasons.NotPainted
                : DiagnosticsUnavailableReasons.LegacyPipeline;
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.ScreenUnavailable,
                reason == DiagnosticsUnavailableReasons.NotPainted
                    ? "The app has not painted a frame yet."
                    : "The legacy rendering pipeline has no canvas to read.",
                errorData: JsonSerializer.SerializeToElement(
                    new DiagnosticsUnavailableData(reason),
                    DiagnosticsJsonContext.Default.DiagnosticsUnavailableData));
        }

        var canvas = screen.Canvas;
        var region = Clip(parameters.Region, canvas.Width, canvas.Height);
        var lines = new string[region.Height];
        var cells = parameters.IncludeCells == true ? new IReadOnlyList<ScreenCell>[region.Height] : null;
        var builder = new StringBuilder(region.Width);
        for (var row = 0; row < region.Height; row++)
        {
            builder.Clear();
            var rowCells = cells is null ? null : new ScreenCell[region.Width];
            for (var column = 0; column < region.Width; column++)
            {
                var cell = canvas[region.X + column, region.Y + row];
                builder.Append(cell.Text);
                if (rowCells is not null)
                {
                    rowCells[column] = ToScreenCell(cell);
                }
            }

            lines[row] = builder.ToString();
            if (cells is not null)
            {
                cells[row] = rowCells!;
            }
        }

        return new ScreenResult(
            RendererSource,
            region.Width,
            region.Height,
            lines,
            screen.Frame.Sequence,
            parameters.Region is null ? null : region,
            cells);
    }

    /// <summary>
    /// The painted cell at a point, or <see langword="null"/> when it lies outside the canvas.
    /// </summary>
    public static ScreenCell? CellAt(TerminalCanvas canvas, int x, int y)
        => x < 0 || y < 0 || x >= canvas.Width || y >= canvas.Height ? null : ToScreenCell(canvas[x, y]);

    public static ScreenCell ToScreenCell(TerminalCell cell)
    {
        if (cell.Style is not { } style)
        {
            return new ScreenCell(cell.Text);
        }

        List<string>? decoration = null;
        foreach (var (flag, name) in Decorations)
        {
            if ((style.Decoration & flag) != 0)
            {
                (decoration ??= []).Add(name);
            }
        }

        return new ScreenCell(cell.Text, ToHex(style.Foreground), ToHex(style.Background), decoration, style.Link);
    }

    private static string? ToHex(Color color)
        => color == Color.Default ? null : "#" + color.ToHex().ToLowerInvariant();

    private static DiagnosticsRegion Clip(DiagnosticsRegion? region, int width, int height)
    {
        if (region is null)
        {
            return new DiagnosticsRegion(0, 0, width, height);
        }

        if (region.X < 0 || region.Y < 0 || region.Width < 0 || region.Height < 0)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "region must not be negative.");
        }

        var x = Math.Min(region.X, width);
        var y = Math.Min(region.Y, height);
        return new DiagnosticsRegion(
            x,
            y,
            (int)Math.Min((long)region.Width, width - x),
            (int)Math.Min((long)region.Height, height - y));
    }
}
