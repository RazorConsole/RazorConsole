// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.Translation.Contexts;

namespace RazorConsole.Tests;

internal static class TestHelpers
{
    public static ConsoleRenderer CreateTestRenderer(IServiceProvider? serviceProvider = null)
    {
        if (serviceProvider is null)
        {
            var services = new ServiceCollection();
            services.AddRazorConsoleServices();
            serviceProvider = services.BuildServiceProvider();
        }
        else
        {
            // Check if TranslationContext is registered, if not, add services
            try
            {
                serviceProvider.GetRequiredService<TranslationContext>();
            }
            catch
            {
                var services = new ServiceCollection();
                services.AddRazorConsoleServices();
                serviceProvider = services.BuildServiceProvider();
            }
        }

        var translationContext = serviceProvider.GetRequiredService<TranslationContext>();
        return new ConsoleRenderer(serviceProvider, NullLoggerFactory.Instance, translationContext);
    }

    public static TranslationContext CreateTestTranslationContext(IServiceProvider? serviceProvider = null)
    {
        var services = serviceProvider as IServiceCollection ?? new ServiceCollection();

        // Add default translation middlewares if not already added
        if (serviceProvider is null)
        {
            services.AddRazorConsoleServices();
            serviceProvider = services.BuildServiceProvider();
        }

        return serviceProvider.GetRequiredService<TranslationContext>();
    }
}
