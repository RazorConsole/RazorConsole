// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Renderables;

/// <summary>
/// Applies a background color to all rendered segments of an existing renderable.
/// </summary>
public sealed class BackgroundRenderable : IRenderable
{
    private readonly IRenderable _inner;
    private readonly Color _background;

    public BackgroundRenderable(IRenderable inner, Color background)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _background = background;
    }

    public Measurement Measure(RenderOptions options, int maxWidth)
        => _inner.Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        foreach (var segment in _inner.Render(options, maxWidth))
        {
            if (segment.IsLineBreak)
            {
                yield return segment;
            }
            else
            {
                yield return new Segment(
                    segment.Text,
                    new Style(
                        foreground: segment.Style?.Foreground,
                        background: _background,
                        decoration: segment.Style?.Decoration));
            }
        }
    }
}
