// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests;

internal static class TestHelpers
{
    public static ConsoleRenderer CreateTestRenderer(IServiceProvider? serviceProvider = null)
    {
        if (serviceProvider?.GetService<WidgetTranslationContext>() is null)
        {
            var services = new ServiceCollection();
            services.AddRazorConsoleServices();
            serviceProvider = services.BuildServiceProvider();
        }

        return new ConsoleRenderer(
            serviceProvider,
            NullLoggerFactory.Instance,
            serviceProvider.GetService<WidgetTranslationContext>(),
            serviceProvider.GetService<LayoutEngine>(),
            serviceProvider.GetService<VNodeIdAccessor>(),
            serviceProvider.GetService<VNodeLayoutAccessor>(),
            serviceProvider.GetService<TerminalMonitor>());
    }
}
