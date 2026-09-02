// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components.Web;

namespace RazorConsole.Components;

/// <summary>
/// Cascaded from <see cref="Scrollable{TItem}"/> when in selection mode.
/// Carries the relative selected index (within the visible page) and a key handler
/// so child components like <see cref="SpectreTBody"/> can delegate navigation.
/// </summary>
public sealed record ScrollableSelection(int RelativeSelectedIndex, Func<KeyboardEventArgs, Task> HandleKeyDown);
