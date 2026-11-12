// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core;
using RazorConsole.Core.Rendering;

namespace RazorConsole.Tests.Rendering;

public sealed class ConsoleRendererOptionsTests
{
    [Fact]
    public void Constructor_WithOnlyRequiredArguments_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        var serviceProvider = services.BuildServiceProvider();

        using var renderer = new ConsoleRenderer(serviceProvider, NullLoggerFactory.Instance);

        renderer.ShouldNotBeNull();
    }
}
