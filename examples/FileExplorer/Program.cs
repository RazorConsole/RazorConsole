// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<FileExplorer.Components.FileExplorer>(configure: config =>
    {
        config.ConfigureServices(s =>
            s.Configure<ConsoleAppOptions>(opt =>
                opt.EnableTerminalResizing = true
            )
        );
    });

var host = builder.Build();

await host.RunAsync();
