// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text.Json;
using RazorConsole.Components;
using RazorConsole.Core.Abstractions.Rendering;

using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class BarChartTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!IsBarChart(node))
        {
            return next(node);
        }

        var itemsAttribute = VdomSpectreTranslator.GetAttribute(node, "data-items");
        if (string.IsNullOrEmpty(itemsAttribute))
        {
            return next(node);
        }

        List<JsonElement> rawItems;
        try
        {
            rawItems = JsonSerializer.Deserialize(itemsAttribute, ChartJsonContext.Default.ListJsonElement) ??
                       throw new InvalidOperationException();
        }
        catch
        {
            return next(node);
        }

        var barChart = new Spectre.Console.BarChart();
        try
        {
            AddBarChartItems(barChart, rawItems);
        }
        catch
        {
            return next(node);
        }

        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"),
                out var width))
        {
            barChart.Width = width;
        }

        var label = VdomSpectreTranslator.GetAttribute(node, "data-label");
        if (!string.IsNullOrEmpty(label))
        {
            barChart.Label = label;
            var labelStyleAttributes = VdomSpectreTranslator.GetAttribute(node, "data-label-style");
            if (!string.IsNullOrEmpty(labelStyleAttributes) && Style.TryParse(labelStyleAttributes, out var labelStyle))
            {
                barChart.Label = $"[{labelStyle!.ToMarkup()}]{label}[/]";
            }
        }

        var labelAlignmentAttribute = VdomSpectreTranslator.GetAttribute(node, "data-label-alignment");
        if (!string.IsNullOrEmpty(labelAlignmentAttribute) &&
            Enum.TryParse<Justify>(labelAlignmentAttribute, true, out var labelAlignment))
        {
            barChart.LabelAlignment = labelAlignment;
        }

        if (VdomSpectreTranslator.TryParsePositiveDouble(VdomSpectreTranslator.GetAttribute(node, "data-max-value"),
                out var maxValue))
        {
            barChart.MaxValue = maxValue;
        }

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-show-values", out var showValues))
        {
            barChart.ShowValues = showValues;
        }

        var cultureInfoAttribute = VdomSpectreTranslator.GetAttribute(node, "data-culture");
        CultureInfo cultureInfo;
        if (cultureInfoAttribute != null)
        {
            try
            {
                cultureInfo = CultureInfo.GetCultureInfo(cultureInfoAttribute);
            }
            catch
            {
                return next(node);
            }
        }
        else
        {
            return next(node);
        }
        barChart.Culture = cultureInfo;

        return barChart;
    }

    private static bool IsBarChart(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "barchart", StringComparison.OrdinalIgnoreCase);

    private void AddBarChartItems(Spectre.Console.BarChart barChart, List<JsonElement> rawItems)
    {
        foreach (var el in rawItems)
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidCastException("Only objects are supported");
            }

            string itemLabel;
            if (el.TryGetProperty(nameof(IBarChartItem.Label), out var l))
            {
                var labelValue = l.GetString();
                itemLabel = labelValue ?? throw new InvalidOperationException("Label cannot be null");
            }
            else
            {
                throw new InvalidCastException("Not found label property");
            }

            double itemValue;
            if (el.TryGetProperty(nameof(IBarChartItem.Value), out var v))
            {
                itemValue = v.TryGetDouble(out var value) ? value : throw new InvalidOperationException($"Unable to parse `{itemLabel}` value as a double");
            }
            else
            {
                throw new InvalidCastException("Not found value property");
            }

            Color? color = null;
            if (el.TryGetProperty(nameof(IBarChartItem.Color), out var cProp) &&
                cProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(cProp.GetString()))
            {
                var hex = cProp.GetString();
                if (hex != null && Color.TryFromHex(hex, out var parsed))
                {
                    color = parsed;
                }
            }

            barChart.AddItem(itemLabel, itemValue, color);
        }
    }
}

