// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<GalleryGrid.Components.App>(configure: hostBuilder =>
    {
        hostBuilder.ConfigureServices(services =>
        {
            services.Configure<ConsoleAppOptions>(options =>
            {
                options.EnableTerminalResizing = true;
                options.RenderingPipeline = ResolveRenderingPipeline(args);
            });
        });
    });

var host = builder.Build();

await host.RunAsync();

static RazorConsoleRenderingPipeline ResolveRenderingPipeline(string[] args)
{
    if (args.Any(arg => string.Equals(arg, "--widget-layout", StringComparison.OrdinalIgnoreCase)))
    {
        return RazorConsoleRenderingPipeline.WidgetLayout;
    }

    var value = Environment.GetEnvironmentVariable("RAZORCONSOLE_RENDERING_PIPELINE");
    if (string.Equals(value, "WidgetLayout", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "widget", StringComparison.OrdinalIgnoreCase))
    {
        return RazorConsoleRenderingPipeline.WidgetLayout;
    }

    return RazorConsoleRenderingPipeline.LegacySpectre;
}
