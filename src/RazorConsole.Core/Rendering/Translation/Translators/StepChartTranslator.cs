// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class StepChartTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        var width = VdomSpectreTranslator.TryGetIntAttribute(node, "data-width", 60);
        var height = VdomSpectreTranslator.TryGetIntAttribute(node, "data-height", 20);
        var showAxes = VdomSpectreTranslator.TryGetBoolAttribute(node, "data-show-axes", out var axes) && axes;
        var axesColor = VdomSpectreTranslator.GetAttribute(node, "data-axes-color") ?? Color.Gray.ToHex();
        var labelsColor = VdomSpectreTranslator.GetAttribute(node, "data-labels-color") ?? Color.Gray.ToHex();
        var title = VdomSpectreTranslator.GetAttribute(node, "data-title");
        var titleColor = VdomSpectreTranslator.GetAttribute(node, "data-title-color") ?? Color.Gray.ToHex();

        var seriesList = new List<SeriesData>();
        foreach (var child in node.Children)
        {
            if (child.TagName != "chartseries")
            {
                continue;
            }

            var color = VdomSpectreTranslator.GetAttribute(child, "data-color") ?? "white";
            var pointsStr = VdomSpectreTranslator.GetAttribute(child, "data-points") ?? "";
            var points = ParsePoints(pointsStr);
            seriesList.Add(new SeriesData("#" + color, points));
        }

        if (seriesList.Count == 0)
        {
            return next(node);
        }

        var chartText = RenderStepChart(seriesList, width, height, showAxes, axesColor, labelsColor, title, titleColor);
        return new Markup(chartText);
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element && node.TagName == "stepchart";

    private List<(double X, double Y)> ParsePoints(string data)
    {
        var points = new List<(double, double)>();
        if (string.IsNullOrEmpty(data))
        {
            return points;
        }

        foreach (var pair in data.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(',');
            if (parts.Length == 2 &&
                double.TryParse(parts[0], out var x) &&
                double.TryParse(parts[1], out var y))
            {
                points.Add((x, y));
            }
        }

        return points.OrderBy<(double, double), object>(p => p.Item1).ToList();
    }

    private string RenderStepChart(
        List<SeriesData> series,
        int width, int height,
        bool showAxes, string axesColor,
        string labelsColor,
        string? title, string? titleColor)
    {
        var allPoints = series.SelectMany(s => s.Points).ToList();
        if (allPoints.Count == 0)
        {
            return "[grey]No data[/]";
        }

        var minX = allPoints.Min(p => p.X);
        var maxX = allPoints.Max(p => p.X);
        var minY = Math.Min(0, allPoints.Min(p => p.Y));
        var maxY = allPoints.Max(p => p.Y);

        // Adjust for axes
        var chartWidth = showAxes ? width - 3 : width;
        var chartHeight = showAxes ? height - 2 : height;

        // Create canvas, fill with spaces
        var canvas = new (char Ch, string Color)[chartWidth, chartHeight];
        for (int x = 0; x < chartWidth; x++)
        {
            for (int y = 0; y < chartHeight; y++)
            {
                canvas[x, y] = (' ', "white");
            }
        }

        // Unicode box drawing chars
        // ReSharper disable InconsistentNaming
        const char HORIZONTAL = '─';
        const char VERTICAL = '│';
        const char CORNER_RIGHT_UP = '┘';
        const char CORNER_RIGHT_DOWN = '┐';
        const char CORNER_LEFT_UP = '┌';
        const char CORNER_LEFT_DOWN = '└';

        foreach (var s in series)
        {
            var points = s.Points;
            for (int i = 0; i < points.Count; i++)
            {
                var (px, py) = points[i];
                // Map to canvas coordinates
                int col = (int)Math.Round((px - minX) / (maxX - minX) * (chartWidth - 1));
                int row = (int)Math.Round((py - minY) / (maxY - minY) * (chartHeight - 1));
                col = Math.Clamp(col, 0, chartWidth - 1);
                row = Math.Clamp(row, 0, chartHeight - 1);

                // Draw vertical connector to next point first
                if (i < points.Count - 1)
                {
                    var (nextX, nextY) = points[i + 1];
                    int nextCol = (int)Math.Round((nextX - minX) / (maxX - minX) * (chartWidth - 1));
                    int nextRow = (int)Math.Round((nextY - minY) / (maxY - minY) * (chartHeight - 1));
                    nextCol = Math.Clamp(nextCol, 0, chartWidth - 1);
                    nextRow = Math.Clamp(nextRow, 0, chartHeight - 1);

                    // Draw horizontal line
                    for (int c = col; c < nextCol; c++)
                    {
                        if (canvas[c, row].Ch != VERTICAL
                            && canvas[c, row].Ch != CORNER_RIGHT_UP && canvas[c, row].Ch != CORNER_RIGHT_DOWN
                            && canvas[c, row].Ch != CORNER_LEFT_UP && canvas[c, row].Ch != CORNER_LEFT_DOWN)
                        {
                            canvas[c, row] = (HORIZONTAL, s.Color);
                        }
                    }

                    if (nextRow != row)
                    {
                        // Draw vertical line (excluding corners)
                        int fromRow = Math.Min(row, nextRow);
                        int toRow = Math.Max(row, nextRow);
                        for (int r = fromRow + 1; r < toRow; r++)
                        {
                            canvas[nextCol, r] = (VERTICAL, s.Color);
                        }

                        // Add corners at both ends
                        if (nextRow > row)
                        {
                            // Going up: CORNER_RIGHT_UP at bottom, CORNER_LEFT_UP at top
                            canvas[nextCol, row] = (CORNER_RIGHT_UP, s.Color);
                            canvas[nextCol, nextRow] = (CORNER_LEFT_UP, s.Color);
                        }
                        else
                        {
                            // Going down: CORNER_RIGHT_DOWN at top, CORNER_LEFT_DOWN at bottom
                            canvas[nextCol, row] = (CORNER_RIGHT_DOWN, s.Color);
                            canvas[nextCol, nextRow] = (CORNER_LEFT_DOWN, s.Color);
                        }
                    }
                }
            }
        }

        // Build output
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(title))
        {
            sb.AppendLine($"[#{titleColor}]{title}[/]");
        }

        // Y-axis arrow at top
        if (showAxes)
        {
            sb.AppendLine($"[#{axesColor}]   ▲[/]");
        }

        // Calculate Y labels to avoid duplicates
        var yLabels = CalculateNonOverlappingLabels(minY, maxY, chartHeight, 5);

        for (int row = chartHeight - 1; row >= 0; row--)
        {
            // Y-axis label with tick mark
            if (showAxes)
            {
                var yVal = minY + (maxY - minY) * row / (chartHeight - 1);

                // Check if this row should have a label
                var closestLabel = yLabels.OrderBy(l => Math.Abs(l.Value - yVal)).First();
                if (Math.Abs(closestLabel.Value - yVal) < (maxY - minY) / (chartHeight * 2))
                {
                    sb.Append($"[#{labelsColor}]{closestLabel.Value,2:0.#}[/][#{axesColor}]─┤[/]");
                }
                else
                {
                    sb.Append($"[#{axesColor}]   │[/]");
                }
            }

            // Chart row
            for (int col = 0; col < chartWidth; col++)
            {
                var (ch, color) = canvas[col, row];
                if (ch != ' ')
                {
                    sb.Append($"[{color}]{ch}[/]");
                }
                else
                {
                    sb.Append(' ');
                }
            }

            sb.AppendLine();
        }

        // X-axis
        if (showAxes)
        {
            sb.Append($"[#{axesColor}]   └");

            // Calculate X labels
            var xLabels = CalculateNonOverlappingLabels(minX, maxX, chartWidth, 5);
            var tickPositions = xLabels
                .Select(l => (int)Math.Round((l.Value - minX) / (maxX - minX) * (chartWidth - 1)))
                .ToArray();

            for (int c = 0; c < chartWidth; c++)
            {
                sb.Append(tickPositions.Contains(c) ? "┬" : "─");
            }

            sb.AppendLine("▶[/]");

            // X labels - ensure no overlaps
            sb.Append("[grey]   ");
            int prevEnd = 0;
            foreach (var label in xLabels)
            {
                var labelStr = $"{label.Value:0.#}";
                int pos = (int)Math.Round((label.Value - minX) / (maxX - minX) * (chartWidth - 1));
                int centerPos = pos - labelStr.Length / 2;
                int spaces = Math.Max(1, centerPos - prevEnd);
                sb.Append(new string(' ', spaces));
                sb.Append($"[#{labelsColor}]{labelStr}[/]");
                prevEnd = centerPos + labelStr.Length;
            }

            sb.AppendLine("[/]");
        }

        return sb.ToString();
    }

    private List<(double Value, int Position)> CalculateNonOverlappingLabels(
        double min, double max, int totalPositions, int maxLabels)
    {
        var labels = new List<(double Value, int Position)>();

        var range = max - min;
        var roughStep = range / (maxLabels - 1);

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        var normalized = roughStep / magnitude;
        double niceStep;
        if (normalized < 1.5)
        {
            niceStep = 1 * magnitude;
        }
        else if (normalized < 3)
        {
            niceStep = 2 * magnitude;
        }
        else if (normalized < 7)
        {
            niceStep = 5 * magnitude;
        }
        else
        {
            niceStep = 10 * magnitude;
        }

        // Generate labels
        var start = Math.Ceiling(min / niceStep) * niceStep;
        for (double val = start; val <= max; val += niceStep)
        {
            int pos = (int)Math.Round((val - min) / range * (totalPositions - 1));
            labels.Add((val, pos));
        }

        if (labels.Count == 0 || labels[0].Value > min)
        {
            labels.Insert(0, (min, 0));
        }

        if (labels.Count == 0 || labels[^1].Value < max)
        {
            labels.Add((max, totalPositions - 1));
        }

        return labels;
    }

    private record SeriesData(string Color, List<(double X, double Y)> Points);
}

