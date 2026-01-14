// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.Markdown;
using RazorConsole.Core.Rendering.Syntax;
using RazorConsole.Core.Utilities;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core;

/// <summary>
/// Extension methods for registering Razor Console services to a service collection.
/// </summary>
public static class RazorConsoleServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default Razor Console services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// This method registers the core infrastructure services needed for rendering Razor components
    /// to the console, including the renderer, focus management, keyboard handling, syntax highlighting,
    /// and VDOM translation services.
    /// </remarks>
    public static IServiceCollection AddRazorConsoleServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IComponentActivator, ComponentActivator>();
        services.TryAddSingleton<ConsoleNavigationManager>();
        services.TryAddSingleton<NavigationManager>(sp => sp.GetRequiredService<ConsoleNavigationManager>());
        services.AddSingleton<INavigationInterception, NoopNavigationInterception>();
        services.AddSingleton<IScrollToLocationHash, NoopScrollToLocationHash>();
        services.TryAddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.TryAddSingleton<ConsoleRenderer>();
        services.TryAddSingleton<VdomDiffService>();
        services.TryAddSingleton<RendererKeyboardEventDispatcher>();
        services.TryAddSingleton<IKeyboardEventDispatcher>(sp => sp.GetRequiredService<RendererKeyboardEventDispatcher>());
        services.TryAddSingleton<IFocusEventDispatcher>(sp => sp.GetRequiredService<RendererKeyboardEventDispatcher>());
        services.TryAddSingleton<FocusManager>(sp => new FocusManager(sp.GetService<IFocusEventDispatcher>()));
        services.TryAddSingleton<KeyboardEventManager>();
        services.TryAddSingleton<ISyntaxLanguageRegistry, ColorCodeLanguageRegistry>();
        services.TryAddSingleton<ISyntaxThemeRegistry, SyntaxThemeRegistry>();
        services.TryAddSingleton<SpectreMarkupFormatter>();
        services.TryAddSingleton<SyntaxHighlightingService>();
        services.TryAddSingleton<MarkdownRenderingService>();
        services.TryAddSingleton<IConsoleInput, ConsoleInput>();
        services.TryAddSingleton<TerminalMonitor>();

        // Register translation middlewares in order of priority
        // Text nodes and basic elements first
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.TextNodeTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.TextElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.HtmlInlineTextElementTranslator>();

        // Simple elements
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.SpacerElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.NewlineElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.SpinerTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.ButtonElementTranslator>();

        // Code and syntax
        services.AddSingleton<ITranslationMiddleware>(sp =>
            new Rendering.Translation.Translators.HtmlCodeBlockElementTranslator(sp.GetRequiredService<SyntaxHighlightingService>()));
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.SyntaxHighlighterElementTranslator>();

        // Charts
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.StepChartTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.BarChartTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.BreakdownChartTranslator>();

        // HTML buttons
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.HtmlButtonElementTranslator>();

        // Layout elements
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.CanvasElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.PanelElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.RowsElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.ColumnsElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.GridElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.PadderElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.AlignElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.ScrollableTranslator>();

        // Special elements (must be before generic HTML elements)
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.FigletElementTranslator>();

        // HTML elements
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.HtmlHeadingElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.HtmlBlockquoteElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.HtmlHrElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.HtmlTableElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.HtmlListElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.HtmlDivElementTranslator>();
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.HtmlParagraphElementTranslator>();

        // Component and region handling
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.ComponentRegionTranslator>();

        // Debug and fallback
#if DEBUG
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.VDomTreePrinterTranslator>();
#endif
        services.AddSingleton<ITranslationMiddleware, Rendering.Translation.Translators.FallbackTranslator>();

        services.AddSingleton<Rendering.Translation.Contexts.TranslationContext>();


        // Add ConsoleAppOptions as a singleton by resolving the IOptions value in a factory to avoid IOptions dependency in injecting components.
        services.AddSingleton<ConsoleAppOptions>(resolver => resolver.GetRequiredService<IOptions<ConsoleAppOptions>>().Value);

        return services;
    }
}
