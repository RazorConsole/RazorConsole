// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text.Json;
using RazorConsole.Components;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Utilities;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Renderables;

/// <summary>
/// Builds the renderables of <c>stepchart</c>, <c>barchart</c> and <c>breakdownchart</c> elements for both rendering pipelines.
/// </summary>
internal static class ChartRenderableBuilder
{
    /// <summary>Returns the chart renderable for a chart element, or null when the node is not a valid chart.</summary>
    public static IRenderable? TryCreate(VNode node)
        => node.Kind != VNodeKind.Element
            ? null
            : node.TagName?.ToLowerInvariant() switch
            {
                "stepchart" => CreateStepChart(node),
                "barchart" => CreateBarChart(node),
                "breakdownchart" => CreateBreakdownChart(node),
                _ => null,
            };

    public static StepChartRenderable CreateStepChart(VNode node)
        => new(node.Children
            .Where(child => child.TagName == "chartseries")
            .Select(child => new StepChartSeries(
                ParseColor(VdomSpectreTranslator.GetAttribute(child, "data-color"), Color.White),
                ParsePoints(VdomSpectreTranslator.GetAttribute(child, "data-points")))))
        {
            Width = VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"), out var width) ? width : null,
            Height = VdomSpectreTranslator.TryGetIntAttribute(node, "data-height", 20),
            ShowAxes = VdomSpectreTranslator.TryGetBoolAttribute(node, "data-show-axes", out var axes) && axes,
            AxesColor = ParseColor(VdomSpectreTranslator.GetAttribute(node, "data-axes-color"), Color.Grey),
            LabelsColor = ParseColor(VdomSpectreTranslator.GetAttribute(node, "data-labels-color"), Color.Grey),
            Title = VdomSpectreTranslator.GetAttribute(node, "data-title"),
            TitleColor = ParseColor(VdomSpectreTranslator.GetAttribute(node, "data-title-color"), Color.Grey),
            XAxisFormat = NonEmpty(VdomSpectreTranslator.GetAttribute(node, "data-x-axis-format")) ?? StepChartRenderable.DefaultAxisFormat,
            YAxisFormat = NonEmpty(VdomSpectreTranslator.GetAttribute(node, "data-y-axis-format")) ?? StepChartRenderable.DefaultAxisFormat,
        };

    public static Spectre.Console.BarChart? CreateBarChart(VNode node)
    {
        if (ParseItems(node) is not { } rawItems
            || ParseCulture(node) is not { } culture)
        {
            return null;
        }

        var barChart = new Spectre.Console.BarChart { Culture = culture };
        try
        {
            AddBarChartItems(barChart, rawItems);
        }
        catch
        {
            return null;
        }

        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"), out var width))
        {
            barChart.Width = width;
        }

        var label = VdomSpectreTranslator.GetAttribute(node, "data-label");
        if (!string.IsNullOrEmpty(label))
        {
            barChart.Label = label;
            var labelStyleAttributes = VdomSpectreTranslator.GetAttribute(node, "data-label-style");
            if (!string.IsNullOrEmpty(labelStyleAttributes) && Style.TryParse(labelStyleAttributes, out var labelStyle) && labelStyle is not null)
            {
                barChart.Label = $"[{labelStyle.ToMarkup()}]{label}[/]";
            }
        }

        var labelAlignmentAttribute = VdomSpectreTranslator.GetAttribute(node, "data-label-alignment");
        if (!string.IsNullOrEmpty(labelAlignmentAttribute) &&
            Enum.TryParse<Justify>(labelAlignmentAttribute, true, out var labelAlignment))
        {
            barChart.LabelAlignment = labelAlignment;
        }

        if (VdomSpectreTranslator.TryParsePositiveDouble(VdomSpectreTranslator.GetAttribute(node, "data-max-value"), out var maxValue))
        {
            barChart.MaxValue = maxValue;
        }

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-show-values", out var showValues))
        {
            barChart.ShowValues = showValues;
        }

        return barChart;
    }

    public static Spectre.Console.BreakdownChart? CreateBreakdownChart(VNode node)
    {
        if (ParseItems(node) is not { } rawItems
            || ParseCulture(node) is not { } culture)
        {
            return null;
        }

        var breakdownChart = new Spectre.Console.BreakdownChart { Culture = culture };
        try
        {
            AddBreakdownChartItems(breakdownChart, rawItems);
        }
        catch
        {
            return null;
        }

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-compact", out var compact))
        {
            breakdownChart.Compact = compact;
        }

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-expand", out var expand))
        {
            breakdownChart.Expand = expand;
        }

        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"), out var width))
        {
            breakdownChart.Width = width;
        }

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-show-tags", out var showTags))
        {
            breakdownChart.ShowTags = showTags;
        }

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-show-tag-values", out var showTagValues))
        {
            breakdownChart.ShowTagValues = showTagValues;
        }

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-show-tag-values-percentage", out var showTagValuesPercentage) && showTagValuesPercentage)
        {
            breakdownChart.ShowPercentage();
        }

        var valueColorAttribute = VdomSpectreTranslator.GetAttribute(node, "data-value-color");
        if (!string.IsNullOrEmpty(valueColorAttribute) && ColorAttribute.TryParse(valueColorAttribute, out var valueColor))
        {
            breakdownChart.ValueColor = valueColor;
        }

        return breakdownChart;
    }

    private static List<(double X, double Y)> ParsePoints(string? data)
    {
        var points = new List<(double X, double Y)>();
        foreach (var pair in (data ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(',');
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                points.Add((x, y));
            }
        }

        return points;
    }

    private static Color ParseColor(string? hex, Color fallback)
        => !string.IsNullOrWhiteSpace(hex) && ColorAttribute.TryParse(hex, out var color) ? color : fallback;

    private static string? NonEmpty(string? value)
        => string.IsNullOrEmpty(value) ? null : value;

    private static List<JsonElement>? ParseItems(VNode node)
    {
        var itemsAttribute = VdomSpectreTranslator.GetAttribute(node, "data-items");
        if (string.IsNullOrEmpty(itemsAttribute))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(itemsAttribute, ChartJsonContext.Default.ListJsonElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CultureInfo? ParseCulture(VNode node)
    {
        if (VdomSpectreTranslator.GetAttribute(node, "data-culture") is not { } name)
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static void AddBarChartItems(Spectre.Console.BarChart barChart, List<JsonElement> rawItems)
    {
        foreach (var el in rawItems)
        {
            var (label, value) = ParseItem(el, nameof(IBarChartItem.Label), nameof(IBarChartItem.Value));

            Color? color = null;
            if (el.TryGetProperty(nameof(IBarChartItem.Color), out var cProp) &&
                cProp.ValueKind == JsonValueKind.String &&
                cProp.GetString() is { } hex &&
                ColorAttribute.TryParse(hex, out var parsed))
            {
                color = parsed;
            }

            barChart.AddItem(label, value, color);
        }
    }

    private static void AddBreakdownChartItems(Spectre.Console.BreakdownChart breakdownChart, List<JsonElement> rawItems)
    {
        foreach (var el in rawItems)
        {
            var (label, value) = ParseItem(el, nameof(IBreakdownChartItem.Label), nameof(IBreakdownChartItem.Value));

            if (!el.TryGetProperty(nameof(IBreakdownChartItem.Color), out var cProp) || string.IsNullOrWhiteSpace(cProp.GetString()))
            {
                throw new InvalidCastException("Not found color property");
            }

            if (!ColorAttribute.TryParse(cProp.GetString() ?? string.Empty, out var color))
            {
                throw new InvalidOperationException($"Unable to parse `{label}` as a color");
            }

            breakdownChart.AddItem(label, value, color);
        }
    }

    private static (string Label, double Value) ParseItem(JsonElement el, string labelProperty, string valueProperty)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidCastException("Only objects are supported");
        }

        if (!el.TryGetProperty(labelProperty, out var l))
        {
            throw new InvalidCastException("Not found label property");
        }

        var label = l.GetString() ?? throw new InvalidOperationException("Label cannot be null");

        if (!el.TryGetProperty(valueProperty, out var v))
        {
            throw new InvalidCastException("Not found value property");
        }

        return v.TryGetDouble(out var value)
            ? (label, value)
            : throw new InvalidOperationException($"Unable to parse `{label}` value as a double");
    }
}
