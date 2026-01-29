// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering;

public record OverlayItem(
    IRenderable Renderable,
    int? Top,
    int? Left,
    int? Right,
    int? Bottom,
    int ZIndex
);
