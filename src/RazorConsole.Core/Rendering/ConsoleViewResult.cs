// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Controllers;

/// <summary>
/// Represents the outcome of rendering a Razor component for the console.
/// </summary>
public sealed class ConsoleViewResult
{
    private ConsoleViewResult(
        string html,
        IRenderable renderable,
        IReadOnlyCollection<IAnimatedConsoleRenderable> animatedRenderables,
        VNode? vdomRoot,
        long renderRevision = 0)
    {
        Html = html ?? string.Empty;
        Renderable = renderable ?? throw new ArgumentNullException(nameof(renderable));
        AnimatedRenderables = animatedRenderables ?? Array.Empty<IAnimatedConsoleRenderable>();
        VdomRoot = vdomRoot;
        RenderRevision = renderRevision;
    }

    /// <summary>
    /// Gets the raw HTML produced by the Razor renderer.
    /// </summary>
    public string Html { get; }

    /// <summary>
    /// Gets the Spectre.Console renderable representation of the rendered view.
    /// </summary>
    public IRenderable Renderable { get; }

    /// <summary>
    /// Gets animated renderables that require live display refreshes.
    /// </summary>
    internal IReadOnlyCollection<IAnimatedConsoleRenderable> AnimatedRenderables { get; }

    /// <summary>
    /// Gets the virtual DOM representation of the rendered view.
    /// </summary>
    internal VNode? VdomRoot { get; }

    internal long RenderRevision { get; }

    internal static ConsoleViewResult Create(
        string html,
        VNode? vdomRoot,
        IRenderable renderable,
        IReadOnlyCollection<IAnimatedConsoleRenderable> animatedRenderables)
        => new(html, renderable, animatedRenderables, vdomRoot);

    internal static ConsoleViewResult FromSnapshot(
        ConsoleRenderer.RenderSnapshot snapshot)
    {
        if (snapshot.Renderable is null)
        {
            throw new InvalidOperationException("Unable to create a view result because the renderable was not provided.");
        }

        var html = VdomHtmlSerializer.Serialize(snapshot.Root);
        return new ConsoleViewResult(html, snapshot.Renderable, snapshot.AnimatedRenderables, snapshot.Root, snapshot.RenderRevision);
    }
}
