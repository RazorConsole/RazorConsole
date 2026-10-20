// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Focus;

internal interface IFocusEventDispatcher
{
    Task DispatchAsync(Vdom.VNodeEvent @event, EventArgs eventArgs, CancellationToken cancellationToken);
}
