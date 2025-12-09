// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation;

/// <summary>
/// Helper methods for translating child nodes using the new TranslationContext.
/// </summary>
public static class TranslationHelpers
{
    /// <summary>
    /// Converts child VNodes to a list of renderables using the new TranslationContext.
    /// </summary>
    /// <param name="children">The child VNodes to convert.</param>
    /// <param name="context">The translation context.</param>
    /// <param name="renderables">The resulting list of renderables if successful.</param>
    /// <returns>True if all children were successfully converted; otherwise, false.</returns>
    public static bool TryConvertChildrenToRenderables(
        IReadOnlyList<VNode> children,
        TranslationContext context,
        out List<IRenderable> renderables)
    {
        renderables = new List<IRenderable>();

        for (int i = 0; i < children.Count; ++i)
        {
            var child = children[i];
            switch (child.Kind)
            {
                case VNodeKind.Text:
                    var normalized = VdomSpectreTranslator.NormalizeTextNode(child.Text);

                    if (!normalized.HasContent)
                    {
                        break;
                    }

                    renderables.Add(new Markup(Markup.Escape($"{(normalized.LeadingWhitespace && i != 0 ? " " : "")}{normalized.Content}{(normalized.TrailingWhitespace ? " " : "")}")));
                    break;
                default:
                    try
                    {
                        var childRenderable = context.Translate(child);
                        renderables.Add(childRenderable);
                    }
                    catch
                    {
                        renderables = new List<IRenderable>();
                        return false;
                    }
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Converts child VNodes to a <see cref="BlockInlineRenderable"/>
    /// </summary>
    /// <param name="children">The child VNodes to convert.</param>
    /// <param name="context">The translation context.</param>
    /// <param name="renderable">The resulting renderable if successful.</param>
    /// <returns>True if all children were successfully converted; otherwise, false.</returns>
    public static bool TryConvertChildrenToBlockInlineRenderable(
        IReadOnlyList<VNode> children,
        TranslationContext context,
        out IRenderable? renderable)
    {
        var items = new List<BlockInlineRenderable.RenderableItem>();

        for (int i = 0; i < children.Count; ++i)
        {
            var child = children[i];
            switch (child.Kind)
            {
                case VNodeKind.Text:
                    var normalized = VdomSpectreTranslator.NormalizeTextNode(child.Text);

                    if (!normalized.HasContent)
                    {
                        break;
                    }

                    var r = new Markup(Markup.Escape($"{(normalized.LeadingWhitespace && i != 0 ? " " : "")}{normalized.Content}{(normalized.TrailingWhitespace ? " " : "")}"));
                    items.Add(BlockInlineRenderable.Inline(r));
                    break;
                default:
                    try
                    {
                        var childRenderable = context.Translate(child);
                        var isBlock = VdomSpectreTranslator.ShouldBeBlock(child);
                        if (isBlock)
                        {
                            items.Add(BlockInlineRenderable.Block(childRenderable));
                        }
                        else
                        {
                            items.Add(BlockInlineRenderable.Inline(childRenderable));
                        }
                    }
                    catch
                    {
                        // Skip failed translations
                        continue;
                    }
                    break;
            }
        }

        if (items.Count == 0)
        {
            renderable = null;
            return false;
        }

        renderable = new BlockInlineRenderable(items);
        return true;
    }
}

