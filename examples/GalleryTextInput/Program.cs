// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<GalleryTextInput.Components.App>(configure: hostBuilder =>
    {
        hostBuilder.ConfigureServices(services =>
        {
            services.Configure<ConsoleAppOptions>(options =>
            {
                options.EnableTerminalResizing = true;
            });
        });
    });

var host = builder.Build();

await host.RunAsync();
