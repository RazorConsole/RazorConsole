// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MultipageApp.Components;
using RazorConsole.Core;

var builder = Host
    .CreateApplicationBuilder(args);

builder.UseRazorConsole<App>(configure: config =>
{
    config.Services.Configure<ConsoleAppOptions>(opt =>
    {
        opt.EnableTerminalResizing = true;
    });
});


await builder.Build().RunAsync();
