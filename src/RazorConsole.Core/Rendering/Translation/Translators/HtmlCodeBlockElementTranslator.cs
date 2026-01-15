// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.Syntax;
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using TranslationContext = RazorConsole.Core.Rendering.Translation.Contexts.TranslationContext;

namespace RazorConsole.Core.Rendering.Translation.Translators;

public sealed class HtmlCodeBlockElementTranslator : ITranslationMiddleware
{
    private readonly SyntaxHighlightingService _syntaxService;

    public HtmlCodeBlockElementTranslator(SyntaxHighlightingService syntaxService)
    {
        _syntaxService = syntaxService ?? throw new ArgumentNullException(nameof(syntaxService));
    }

    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (!CanHandle(node))
        {
            return next(node);
        }

        // Look for a code child element
        var codeNode = node.Children.FirstOrDefault(c =>
            c.Kind == VNodeKind.Element &&
            string.Equals(c.TagName, "code", StringComparison.OrdinalIgnoreCase));

        if (codeNode == null)
        {
            // No code element, just render as text
            var text = VdomSpectreTranslator.CollectInnerText(node);
            return new Markup(Markup.Escape(text ?? string.Empty));
        }

        // Extract the code content
        var code = VdomSpectreTranslator.CollectInnerText(codeNode);
        if (string.IsNullOrWhiteSpace(code))
        {
            return new Markup(string.Empty);
        }

        // Try to detect language from class attribute (e.g., "language-csharp")
        string? language = null;
        if (codeNode.TryGetAttributeValue<string>("class", out var classAttr) && !string.IsNullOrEmpty(classAttr))
        {
            var classes = classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var langClass = classes.FirstOrDefault(c => c.StartsWith("language-", StringComparison.OrdinalIgnoreCase));
            if (langClass != null)
            {
                language = langClass.Substring("language-".Length);
            }
        }

        // Use syntax highlighting if available
        try
        {
            var request = new SyntaxHighlightRequest(code, language, null, true, SyntaxOptions.Default);
            var model = _syntaxService.Highlight(request);
            var body = new SyntaxRenderable(model);
            return new Rows([new Markup(" "), body, new Markup(" ")]);
        }
        catch
        {
            // Fall back to plain text if syntax highlighting fails
            return new Markup(Markup.Escape(code));
        }
    }

    private static bool CanHandle(VNode node)
        => node.Kind == VNodeKind.Element
           && string.Equals(node.TagName?.ToLowerInvariant(), "pre");
}

