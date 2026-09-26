// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Rendering;

public interface ITerminalViewport
{
    int Width { get; }
    int Height { get; }
    event Action? OnResized;
}
