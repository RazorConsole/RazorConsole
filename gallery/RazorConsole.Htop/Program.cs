// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<RazorConsole.Htop.Components.App>(configure: hostBuilder =>
    {
        hostBuilder.ConfigureServices(services =>
        {
            services.Configure<ConsoleAppOptions>(options =>
            {
                options.AutoClearConsole = true;
                options.EnableTerminalResizing = true;
                options.RenderingPipeline = RazorConsoleRenderingPipeline.WidgetLayout;
                options.ConsoleLiveDisplayOptions.EnableMouseEvents = true;
            });
        });
    });

await builder.Build().RunAsync();

