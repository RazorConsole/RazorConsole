// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using Microsoft.AspNetCore.Components;
using RazorConsole.Components;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Rendering;

internal static class TextButtonAdapter
{
    internal static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Component && node.Component is TextButton;

    internal static VNode CreateNode(VNode slot, int componentId)
    {
        var button = (TextButton)slot.Component!;
        var node = VNode.CreateComponent();
        node.Component = button;
        node.ID = slot.ID;
        node.SetKey(slot.ID);
        node.SetAttribute("data-focusable", "true");
        node.SetAttribute("data-display", "block");
        node.SetAttribute("data-focus-key", slot.ID);
        if (button.FocusOrder is { } order)
        {
            node.SetAttribute("data-focus-order", order.ToString(CultureInfo.InvariantCulture));
        }

        Bind("onfocus", _ => button.SetFocused(true));
        Bind("onfocusout", _ => button.SetFocused(false));
        Bind("onmouseenter", _ => button.SetHovered(true));
        Bind("onmouseleave", _ => button.SetHovered(false));
        if (button.OnClick.HasDelegate)
        {
            node.SetComponentEvent("onclick", componentId,
                EventCallback.Factory.Create<EventArgs>(button, _ => button.OnClick.InvokeAsync()));
        }

        return node;

        void Bind(string name, Action<EventArgs> action)
            => node.SetComponentEvent(name, componentId, EventCallback.Factory.Create(button, action));
    }
}
