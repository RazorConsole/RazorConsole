// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text.Json;
using RazorConsole.Components;

using RazorConsole.Core.Utilities;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Layout.Leaves;

internal static class BreakdownChartRenderableBuilder
{
    public static IRenderable? TryCreate(VNode node)
    {
        if (!IsBreakdownChart(node))
        {
            return null;
        }

        var itemsAttribute = VNodeAttributeReader.GetAttribute(node, "data-items");
        if (string.IsNullOrEmpty(itemsAttribute))
        {
            return null;
        }

        List<JsonElement> rawItems;
        try
        {
            rawItems = JsonSerializer.Deserialize(itemsAttribute, ChartJsonContext.Default.ListJsonElement) ??
                       throw new InvalidOperationException();
        }
        catch
        {
            return null;
        }

        var breakdownChart = new Spectre.Console.BreakdownChart();
        try
        {
            AddBreakdownChartItems(breakdownChart, rawItems);
        }
        catch
        {
            return null;
        }

        if (VNodeAttributeReader.TryGetBoolAttribute(node, "data-compact", out var compact))
        {
            breakdownChart.Compact = compact;
        }

        var cultureInfoAttribute = VNodeAttributeReader.GetAttribute(node, "data-culture");
        CultureInfo cultureInfo;
        if (cultureInfoAttribute != null)
        {
            try
            {
                cultureInfo = CultureInfo.GetCultureInfo(cultureInfoAttribute);
            }
            catch
            {
                return null;
            }
        }
        else
        {
            return null;
        }
        breakdownChart.Culture = cultureInfo;

        if (VNodeAttributeReader.TryGetBoolAttribute(node, "data-expand", out var expand))
        {
            breakdownChart.Expand = expand;
        }

        if (VNodeAttributeReader.TryParsePositiveInt(VNodeAttributeReader.GetAttribute(node, "data-width"),
                out var width))
        {
            breakdownChart.Width = width;
        }

        if (VNodeAttributeReader.TryGetBoolAttribute(node, "data-show-tags", out var showTags))
        {
            breakdownChart.ShowTags = showTags;
        }

        if (VNodeAttributeReader.TryGetBoolAttribute(node, "data-show-tag-values", out var showTagValues))
        {
            breakdownChart.ShowTagValues = showTagValues;
        }

        if (VNodeAttributeReader.TryGetBoolAttribute(node, "data-show-tag-values-percentage", out var showTagValuesPercentage) && showTagValuesPercentage)
        {
            breakdownChart.ShowPercentage();
        }

        var valueColorAttribute = VNodeAttributeReader.GetAttribute(node, "data-value-color");
        if (!string.IsNullOrEmpty(valueColorAttribute) && ColorAttribute.TryParse(valueColorAttribute, out var valueColor))
        {
            breakdownChart.ValueColor = valueColor;
        }

        return breakdownChart;
    }

    private static bool IsBreakdownChart(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "breakdownchart", StringComparison.OrdinalIgnoreCase);

    private static void AddBreakdownChartItems(Spectre.Console.BreakdownChart breakdownChart, List<JsonElement> rawItems)
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
                if (hex != null && ColorAttribute.TryParse(hex, out var parsed))
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

