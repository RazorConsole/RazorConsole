// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics.CodeAnalysis;
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
using RazorConsole.Core.Layout;
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
        services.AddOptions<ConsoleAppOptions>();
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
        services.TryAddSingleton<MouseEventManager>();
        services.TryAddSingleton<TerminalInputDispatcher>();
        services.TryAddSingleton<ITerminalActions, TerminalActions>();
        services.TryAddSingleton<ISyntaxLanguageRegistry, ColorCodeLanguageRegistry>();
        services.TryAddSingleton<ISyntaxThemeRegistry, SyntaxThemeRegistry>();
        services.TryAddSingleton<SpectreMarkupFormatter>();
        services.TryAddSingleton<SyntaxHighlightingService>();
        services.TryAddSingleton<MarkdownRenderingService>();
        services.TryAddSingleton<IConsoleInput, ConsoleInput>();
        services.TryAddSingleton<TerminalMonitor>();
        services.TryAddSingleton<ITerminalViewport>(sp => sp.GetRequiredService<TerminalMonitor>());
        services.TryAddSingleton<ScrollableLayoutCoordinator>();
        services.TryAddSingleton<ScrollStateRegistry>();
        services.TryAddSingleton<LayoutEngine>();
        services.TryAddSingleton(sp => new WidgetTranslationContext(
            sp.GetServices<IWidgetTranslator>(),
            sp.GetService<ScrollableLayoutCoordinator>(),
            sp.GetService<SyntaxHighlightingService>(),
            sp.GetService<ScrollStateRegistry>()));
        services.TryAddSingleton<VNodeIdAccessor>();
        services.TryAddSingleton<IVNodeIdAccessor>(sp => sp.GetRequiredService<VNodeIdAccessor>());
        services.TryAddSingleton<VNodeLayoutAccessor>();
        services.TryAddSingleton<IVNodeLayoutAccessor>(sp => sp.GetRequiredService<VNodeLayoutAccessor>());

        // Add ConsoleAppOptions as a singleton by resolving the IOptions value in a factory to avoid IOptions dependency in injecting components.
        services.AddSingleton<ConsoleAppOptions>(resolver => resolver.GetRequiredService<IOptions<ConsoleAppOptions>>().Value);

        return services;
    }

    /// <summary>
    /// Adds a custom <see cref="IWidgetTranslator"/> that runs before the built-in widget translation.
    /// </summary>
    /// <typeparam name="TTranslator">The translator type, resolved as a singleton.</typeparam>
    /// <param name="services">The service collection to add the translator to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>Translators run in the order they are added.</remarks>
    public static IServiceCollection AddWidgetTranslator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTranslator>(this IServiceCollection services)
        where TTranslator : class, IWidgetTranslator
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWidgetTranslator, TTranslator>();
        return services;
    }

    /// <summary>
    /// Adds a custom <see cref="IWidgetTranslator"/> instance that runs before the built-in widget translation.
    /// </summary>
    /// <param name="services">The service collection to add the translator to.</param>
    /// <param name="translator">The translator.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>Translators run in the order they are added.</remarks>
    public static IServiceCollection AddWidgetTranslator(this IServiceCollection services, IWidgetTranslator translator)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(translator);
        services.AddSingleton(translator);
        return services;
    }
}
