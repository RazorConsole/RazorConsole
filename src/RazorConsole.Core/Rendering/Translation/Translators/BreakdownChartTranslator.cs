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

public sealed class BreakdownChartTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!IsBreakdownChart(node))
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

        var breakdownChart = new Spectre.Console.BreakdownChart();
        try
        {
            AddBreakdownChartItems(breakdownChart, rawItems);
        }
        catch
        {
            return next(node);
        }

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-compact", out var compact))
        {
            breakdownChart.Compact = compact;
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
        breakdownChart.Culture = cultureInfo;

        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-expand", out var expand))
        {
            breakdownChart.Expand = expand;
        }

        if (VdomSpectreTranslator.TryParsePositiveInt(VdomSpectreTranslator.GetAttribute(node, "data-width"),
                out var width))
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
        if (!string.IsNullOrEmpty(valueColorAttribute) && Color.TryFromHex(valueColorAttribute, out var valueColor))
        {
            breakdownChart.ValueColor = valueColor;
        }

        return breakdownChart;
    }

    private static bool IsBreakdownChart(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "breakdownchart", StringComparison.OrdinalIgnoreCase);

    private void AddBreakdownChartItems(Spectre.Console.BreakdownChart breakdownChart, List<JsonElement> rawItems)
    {
        foreach (var el in rawItems)
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidCastException("Only objects are supported");
            }

            string itemLabel;
            if (el.TryGetProperty(nameof(IBreakdownChartItem.Label), out var l))
            {
                var labelValue = l.GetString();
                itemLabel = labelValue ?? throw new InvalidOperationException("Label cannot be null");
            }
            else
            {
                throw new InvalidCastException("Not found label property");
            }

            double itemValue;
            if (el.TryGetProperty(nameof(IBreakdownChartItem.Value), out var v))
            {
                itemValue = v.TryGetDouble(out var value)
                    ? value
                    : throw new InvalidOperationException($"Unable to parse `{itemLabel}` value as a double");
            }
            else
            {
                throw new InvalidCastException("Not found value property");
            }

            Color color;
            if (el.TryGetProperty(nameof(IBreakdownChartItem.Color), out var cProp) &&
                !string.IsNullOrWhiteSpace(cProp.GetString()))
            {
                var hex = cProp.GetString();
                if (hex != null && Color.TryFromHex(hex, out var parsed))
                {
                    color = parsed;
                }
                else
                {
                    throw new InvalidOperationException($"Unable to parse `{itemLabel}` as a color");
                }
            }
            else
            {
                throw new InvalidCastException("Not found color property");
            }

            breakdownChart.AddItem(itemLabel, itemValue, color);
        }
    }
}

