// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core;
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Vdom;

public sealed class CustomTranslatorTests
{
    [Fact]
    public void CanRegisterCustomTranslatorWithPriority()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        services.AddSingleton<ITranslationMiddleware, CustomTestTranslator>();

        var serviceProvider = services.BuildServiceProvider();
        var middlewares = serviceProvider.GetServices<ITranslationMiddleware>();

        // Act
        var middlewareList = new List<ITranslationMiddleware>(middlewares);

        // Assert - Custom translator should be in the list
        // We should have at least one more translator than the default count
        middlewareList.Count.ShouldBeGreaterThan(20);
    }

    [Fact]
    public void CustomTranslatorWithHighPriority_ProcessedFirst()
    {
        // Arrange - Register custom translator first so it's processed before others
        var services = new ServiceCollection();
        services.AddSingleton<ITranslationMiddleware, CustomTestTranslator>();
        services.AddRazorConsoleServices();

        var serviceProvider = services.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<TranslationContext>();

        var node = VNode.CreateElement("custom-element");
        node.SetAttribute("data-custom", "true");

        // Act
        var renderable = context.Translate(node);

        // Assert
        renderable.ShouldBeOfType<Text>();
    }

    [Fact]
    public void CanRegisterCustomTranslatorInstance()
    {
        // Arrange - Register custom translator first so it's processed before others
        var services = new ServiceCollection();
        services.AddSingleton<ITranslationMiddleware>(_ => new CustomTestTranslator());
        services.AddRazorConsoleServices();

        var serviceProvider = services.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<TranslationContext>();

        var node = VNode.CreateElement("custom-element");
        node.SetAttribute("data-custom", "true");

        // Act
        var renderable = context.Translate(node);

        // Assert
        renderable.ShouldBeOfType<Text>();
    }

    // Test custom translator
    private sealed class CustomTestTranslator : ITranslationMiddleware
    {
        public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
        {
            if (node.Kind != VNodeKind.Element)
            {
                return next(node);
            }

            if (!string.Equals(node.TagName, "custom-element", StringComparison.OrdinalIgnoreCase))
            {
                return next(node);
            }

            if (!node.TryGetAttributeValue<string>("data-custom", out var value)
                || !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return next(node);
            }

            return new Text("Custom Translation!");
        }
    }
}
