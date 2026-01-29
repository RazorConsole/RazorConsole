// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class HtmlButtonElementTranslator : ITranslationMiddleware
{
    private static readonly IReadOnlyDictionary<string, string> VariantMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["btn-primary"] = "primary",
        ["btn-success"] = "success",
        ["btn-warning"] = "warning",
        ["btn-danger"] = "danger",
        ["btn-error"] = "danger",
        ["btn-info"] = "neutral",
        ["btn-secondary"] = "neutral",
        ["btn-default"] = "neutral",
    };

    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        if (!TranslationHelpers.TryConvertChildrenToBlockInlineRenderable(node.Children, context, out var children) || children is null)
        {
            return next(node);
        }

        var descriptor = CreateDescriptor(node);
        return ButtonRenderableBuilder.Build(descriptor, children);
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName, "button", StringComparison.OrdinalIgnoreCase);

    private static ButtonRenderableDescriptor CreateDescriptor(VNode node)
    {
        var label = ResolveButtonLabel(node);
        var icon = VdomSpectreTranslator.GetAttribute(node, "data-button-icon");
        var hotKey = ResolveButtonHotKey(node);
        var variant = ResolveButtonVariant(node);
        var style = VdomSpectreTranslator.GetAttribute(node, "data-button-style");
        var activeStyle = VdomSpectreTranslator.GetAttribute(node, "data-button-active-style");
        var disabledStyle = VdomSpectreTranslator.GetAttribute(node, "data-button-disabled-style");
        var borderStyle = VdomSpectreTranslator.GetAttribute(node, "data-button-border-style");
        var activeBorderStyle = VdomSpectreTranslator.GetAttribute(node, "data-button-active-border-style");
        var disabledBorderStyle = VdomSpectreTranslator.GetAttribute(node, "data-button-disabled-border-style");
        var isActive = ResolveButtonIsActive(node);
        var isDisabled = ResolveButtonDisabled(node);
        var expand = ResolveButtonExpand(node);
        var width = ResolveButtonWidth(node);
        var padding = ResolveButtonPadding(node);
        var isDefault = ResolveButtonIsDefault(node);

        return new ButtonRenderableDescriptor(
            label,
            icon,
            hotKey,
            variant,
            style,
            activeStyle,
            disabledStyle,
            borderStyle,
            activeBorderStyle,
            disabledBorderStyle,
            isActive,
            isDisabled,
            expand,
            width,
            padding,
            isDefault);
    }

    private static string? ResolveButtonLabel(VNode node)
    {
        var dataLabel = VdomSpectreTranslator.GetAttribute(node, "data-button-label");
        if (!string.IsNullOrWhiteSpace(dataLabel))
        {
            return dataLabel;
        }

        if (node.TryGetAttributeValue<string>("aria-label", out var ariaLabel) && !string.IsNullOrWhiteSpace(ariaLabel))
        {
            return ariaLabel;
        }

        return VdomSpectreTranslator.CollectInnerText(node);
    }

    private static string? ResolveButtonHotKey(VNode node)
    {
        var explicitHotKey = VdomSpectreTranslator.GetAttribute(node, "data-button-hotkey");
        if (!string.IsNullOrWhiteSpace(explicitHotKey))
        {
            return explicitHotKey;
        }

        var accessKey = VdomSpectreTranslator.GetAttribute(node, "accesskey");
        return string.IsNullOrWhiteSpace(accessKey) ? null : accessKey.Trim();
    }

    private static string? ResolveButtonVariant(VNode node)
    {
        var explicitVariant = VdomSpectreTranslator.GetAttribute(node, "data-button-variant");
        if (!string.IsNullOrWhiteSpace(explicitVariant))
        {
            return explicitVariant;
        }

        if (node.TryGetAttributeValue<string>("class", out var classValue))
        {
            foreach (var token in VdomSpectreTranslator.EnumerateClassNames(classValue))
            {
                if (VariantMappings.TryGetValue(token, out var variant))
                {
                    return variant;
                }
            }
        }

        return null;
    }

    private static bool ResolveButtonIsActive(VNode node)
    {
        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-button-active", out var value))
        {
            return value;
        }

        return VdomSpectreTranslator.HasClass(node, "active");
    }

    private static bool ResolveButtonDisabled(VNode node)
    {
        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-button-disabled", out var value))
        {
            return value;
        }

        if (node.Attributes.ContainsKey("disabled"))
        {
            return true;
        }

        if (node.TryGetAttributeValue<string>("aria-disabled", out var ariaDisabled) && bool.TryParse(ariaDisabled, out var parsedAria))
        {
            return parsedAria;
        }

        return VdomSpectreTranslator.HasClass(node, "disabled");
    }

    private static bool ResolveButtonExpand(VNode node)
    {
        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-button-expand", out var value))
        {
            return value;
        }

        return VdomSpectreTranslator.HasClass(node, "btn-block") || VdomSpectreTranslator.HasClass(node, "w-100");
    }

    private static int? ResolveButtonWidth(VNode node)
    {
        var widthRaw = VdomSpectreTranslator.GetAttribute(node, "data-button-width");
        return VdomSpectreTranslator.TryParsePositiveInt(widthRaw, out var width) ? width : null;
    }

    private static Padding? ResolveButtonPadding(VNode node)
    {
        var paddingRaw = VdomSpectreTranslator.GetAttribute(node, "data-button-padding");
        if (!string.IsNullOrWhiteSpace(paddingRaw) && VdomSpectreTranslator.TryParsePadding(paddingRaw, out var padding))
        {
            return padding;
        }

        return null;
    }

    private static bool ResolveButtonIsDefault(VNode node)
    {
        if (VdomSpectreTranslator.TryGetBoolAttribute(node, "data-button-default", out var value))
        {
            return value;
        }

        if (node.TryGetAttributeValue<string>("type", out var typeValue) && string.Equals(typeValue, "submit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return VdomSpectreTranslator.HasClass(node, "btn-primary") || VdomSpectreTranslator.HasClass(node, "btn-default");
    }
}

