// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Renderables;

internal sealed record StepChartSeries(Color Color, IReadOnlyList<(double X, double Y)> Points);

/// <summary>
/// Draws step series with box-drawing characters, <see cref="Width"/> wide or, when it is null, as wide as it is rendered.
/// </summary>
internal sealed class StepChartRenderable : IRenderable
{
    public const string DefaultAxisFormat = "0.#";

    private const string NoData = "No data";
    private const int MaxAxisLabels = 5;
    private const int MinYLabelWidth = 2;
    private const char Horizontal = '─';
    private const char Vertical = '│';
    private const char CornerRightUp = '┘';
    private const char CornerRightDown = '┐';
    private const char CornerLeftUp = '┌';
    private const char CornerLeftDown = '└';

    private readonly IReadOnlyList<StepChartSeries> _series;

    public StepChartRenderable(IEnumerable<StepChartSeries> series)
        => _series = series
            .Select(s => s with { Points = s.Points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).OrderBy(p => p.X).ToArray() })
            .ToArray();

    /// <summary>Gets the chart width as the <c>StepChart</c> component defines it, or null to fill the render width.</summary>
    public int? Width { get; init; }

    public int Height { get; init; } = 20;

    public bool ShowAxes { get; init; } = true;

    public Color AxesColor { get; init; } = Color.Grey;

    public Color LabelsColor { get; init; } = Color.Grey;

    public string? Title { get; init; }

    public Color TitleColor { get; init; } = Color.Grey;

    public string XAxisFormat { get; init; } = DefaultAxisFormat;

    public string YAxisFormat { get; init; } = DefaultAxisFormat;

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        var width = CreatePlot(maxWidth)?.TotalWidth ?? NoData.Length;
        if (CreateTitle() is { } title)
        {
            width = Math.Max(width, title.Measure(options, maxWidth).Max);
        }

        width = Math.Min(width, maxWidth);
        return new Measurement(width, width);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var lines = new List<IEnumerable<Segment>>();
        if (CreateTitle() is { } title)
        {
            lines.AddRange(Segment.SplitLines(title.Render(options, maxWidth)));
        }

        if (CreatePlot(maxWidth) is { } plot)
        {
            lines.AddRange(plot.RenderLines());
        }
        else
        {
            lines.Add([new Segment(NoData, new Style(Color.Grey))]);
        }

        return lines.SelectMany((line, index) => index == 0 ? line : line.Prepend(Segment.LineBreak));
    }

    private IRenderable? CreateTitle()
        => string.IsNullOrEmpty(Title) ? null : new Markup(Title, new Style(TitleColor));

    private Plot? CreatePlot(int maxWidth)
    {
        var points = _series.SelectMany(s => s.Points).ToArray();
        if (points.Length == 0)
        {
            return null;
        }

        var x = new AxisRange(points.Min(p => p.X), points.Max(p => p.X));
        var y = new AxisRange(Math.Min(0, points.Min(p => p.Y)), points.Max(p => p.Y));
        var height = Math.Max(1, ShowAxes ? Height - 2 : Height);
        var yLabels = ShowAxes ? RowLabels(y, height) : new string?[height];
        var yLabelWidth = yLabels.Select(label => label?.GetCellWidth() ?? 0).Prepend(MinYLabelWidth).Max();

        // The gutter holds the Y labels plus "─┤"; the X axis ends in "▶" one column past the plot.
        var gutter = ShowAxes ? yLabelWidth + 2 : 0;
        var available = Math.Max(1, maxWidth - gutter - (ShowAxes ? 1 : 0));

        // An explicit Width keeps the footprint StepChart always had: Width + 2 columns with axes, Width without.
        var width = Width is { } explicitWidth
            ? Math.Clamp(ShowAxes ? explicitWidth + 1 - gutter : explicitWidth, 1, available)
            : available;

        return new Plot(this, x, y, width, height, yLabels, yLabelWidth);
    }

    private string?[] RowLabels(AxisRange range, int rows)
    {
        var labels = new string?[rows];
        if (!range.HasSpan)
        {
            labels[0] = Format(range.Min, YAxisFormat);
            return labels;
        }

        var values = AxisValues(range);
        for (var row = 0; row < rows; row++)
        {
            var rowValue = rows == 1 ? range.Min : range.Min + range.Span * row / (rows - 1);
            var closest = values.MinBy(value => Math.Abs(value - rowValue));
            if (Math.Abs(closest - rowValue) < range.Span / (rows * 2))
            {
                labels[row] = Format(closest, YAxisFormat);
            }
        }

        return labels;
    }

    private static List<double> AxisValues(AxisRange range)
    {
        if (!range.HasSpan)
        {
            return [range.Min];
        }

        var roughStep = range.Span / (MaxAxisLabels - 1);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        var step = magnitude * (roughStep / magnitude) switch
        {
            < 1.5 => 1,
            < 3 => 2,
            < 7 => 5,
            _ => 10,
        };

        // step >= roughStep / 1.5, so at most 7 steps fit in the range.
        var start = Math.Ceiling(range.Min / step) * step;
        var values = Enumerable.Range(0, MaxAxisLabels * 2)
            .Select(i => start + i * step)
            .TakeWhile(value => value <= range.Max)
            .ToList();

        if (values.Count == 0 || values[0] > range.Min)
        {
            values.Insert(0, range.Min);
        }

        if (values[^1] < range.Max)
        {
            values.Add(range.Max);
        }

        return values;
    }

    // Labels are display text, so they use the current culture; -0 prints as 0.
    private static string Format(double value, string format)
        => (value == 0 ? 0 : value).ToString(format, CultureInfo.CurrentCulture);

    private readonly record struct AxisRange(double Min, double Max)
    {
        public bool HasSpan => Max > Min;

        public double Span => Max - Min;

        public int Scale(double value, int size)
            => HasSpan ? Math.Clamp((int)Math.Round((value - Min) / Span * (size - 1)), 0, size - 1) : 0;
    }

    private sealed class Plot(
        StepChartRenderable chart,
        AxisRange x,
        AxisRange y,
        int width,
        int height,
        string?[] yLabels,
        int yLabelWidth)
    {
        private int Gutter => chart.ShowAxes ? yLabelWidth + 2 : 0;

        public int TotalWidth => Gutter + width + (chart.ShowAxes ? 1 : 0);

        public IEnumerable<IEnumerable<Segment>> RenderLines()
        {
            var axes = new Style(chart.AxesColor);
            var labels = new Style(chart.LabelsColor);
            var blankLabel = new string(' ', yLabelWidth + 1);
            var canvas = Draw();

            if (chart.ShowAxes)
            {
                yield return [new Segment(blankLabel + "▲", axes)];
            }

            for (var row = height - 1; row >= 0; row--)
            {
                var line = new List<Segment>();
                if (chart.ShowAxes)
                {
                    line.AddRange(yLabels[row] is { } label
                        ? [new Segment(new string(' ', yLabelWidth - label.GetCellWidth()) + label, labels), new Segment("─┤", axes)]
                        : [new Segment(blankLabel + "│", axes)]);
                }

                for (var col = 0; col < width; col++)
                {
                    var (ch, style) = canvas[col, row];
                    line.Add(new Segment(ch.ToString(), style ?? Style.Plain));
                }

                yield return line;
            }

            if (!chart.ShowAxes)
            {
                yield break;
            }

            var xValues = AxisValues(x);
            var ticks = xValues.Select(value => x.Scale(value, width)).ToHashSet();
            var axis = string.Concat(Enumerable.Range(0, width).Select(col => ticks.Contains(col) ? '┬' : '─'));
            yield return [new Segment(blankLabel + "└" + axis + "▶", axes)];
            yield return XLabels(xValues, labels);
        }

        private List<Segment> XLabels(List<double> values, Style style)
        {
            var line = new List<Segment>();
            var end = 0;
            var next = Gutter;
            foreach (var value in values)
            {
                var label = Format(value, chart.XAxisFormat);
                var length = label.GetCellWidth();
                var start = Math.Min(Math.Max(next, Gutter + x.Scale(value, width) - length / 2), TotalWidth - length);
                if (start < next)
                {
                    continue;
                }

                line.Add(new Segment(new string(' ', start - end)));
                line.Add(new Segment(label, style));
                end = start + length;
                next = end + 1;
            }

            return line;
        }

        private (char Ch, Style? Style)[,] Draw()
        {
            var canvas = new (char Ch, Style? Style)[width, height];
            for (var col = 0; col < width; col++)
            {
                for (var row = 0; row < height; row++)
                {
                    canvas[col, row] = (' ', null);
                }
            }

            foreach (var series in chart._series)
            {
                var style = new Style(series.Color);
                var points = series.Points;
                for (var i = 0; i < points.Count - 1; i++)
                {
                    var col = x.Scale(points[i].X, width);
                    var row = y.Scale(points[i].Y, height);
                    var nextCol = x.Scale(points[i + 1].X, width);
                    var nextRow = y.Scale(points[i + 1].Y, height);

                    for (var c = col; c < nextCol; c++)
                    {
                        if (canvas[c, row].Ch is not (Vertical or CornerRightUp or CornerRightDown or CornerLeftUp or CornerLeftDown))
                        {
                            canvas[c, row] = (Horizontal, style);
                        }
                    }

                    if (nextRow == row)
                    {
                        continue;
                    }

                    for (var r = Math.Min(row, nextRow) + 1; r < Math.Max(row, nextRow); r++)
                    {
                        canvas[nextCol, r] = (Vertical, style);
                    }

                    var up = nextRow > row;
                    canvas[nextCol, row] = (up ? CornerRightUp : CornerRightDown, style);
                    canvas[nextCol, nextRow] = (up ? CornerLeftUp : CornerLeftDown, style);
                }
            }

            return canvas;
        }
    }
}
