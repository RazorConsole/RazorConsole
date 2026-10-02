// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using RazorConsole.TankBattle.Components;

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<App>(configure: hostBuilder =>
    {
        hostBuilder.ConfigureServices(services =>
        {
            services.Configure<ConsoleAppOptions>(options =>
            {
                options.AutoClearConsole = true;
                options.EnableTerminalResizing = true;
                options.RenderingPipeline = RazorConsoleRenderingPipeline.WidgetLayout;
            });
        });
    });

await builder.Build().RunAsync();
