// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Controllers;

/// <summary>
/// Represents the outcome of rendering a Razor component for the console.
/// </summary>
internal sealed class ConsoleViewResult
{
    private ConsoleViewResult(
        string html,
        FrameSource source,
        IReadOnlyCollection<IAnimatedConsoleRenderable> animatedRenderables,
        VNode? vdomRoot)
    {
        Html = html ?? string.Empty;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        AnimatedRenderables = animatedRenderables ?? Array.Empty<IAnimatedConsoleRenderable>();
        VdomRoot = vdomRoot;
    }

    /// <summary>
    /// Gets the raw HTML produced by the Razor renderer.
    /// </summary>
    public string Html { get; }

    /// <summary>
    /// Gets the laid-out view that the frame loop paints.
    /// </summary>
    public FrameSource Source { get; }

    /// <summary>
    /// Gets animated renderables that require live display refreshes.
    /// </summary>
    internal IReadOnlyCollection<IAnimatedConsoleRenderable> AnimatedRenderables { get; }

    /// <summary>
    /// Gets the virtual DOM representation of the rendered view.
    /// </summary>
    internal VNode? VdomRoot { get; }

    internal static ConsoleViewResult Create(
        string html,
        VNode? vdomRoot,
        FrameSource source,
        IReadOnlyCollection<IAnimatedConsoleRenderable> animatedRenderables)
        => new(html, source, animatedRenderables, vdomRoot);

    internal static ConsoleViewResult FromSnapshot(
        ConsoleRenderer.RenderSnapshot snapshot)
    {
        if (snapshot.Source is null)
        {
            throw new InvalidOperationException("Unable to create a view result because the snapshot has nothing to paint.");
        }

        var html = VdomHtmlSerializer.Serialize(snapshot.Root);
        return Create(html, snapshot.Root, snapshot.Source, snapshot.AnimatedRenderables);
    }
}
