// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using RazorConsole.Core;
using RazorConsole.Gallery.Components;
using RazorConsole.Gallery.Services;

var builder = Host
    .CreateApplicationBuilder(args);

builder.UseRazorConsole<App>(configure: config =>
{
    config.Services.Configure<ConsoleAppOptions>(opt =>
    {
        opt.EnableTerminalResizing = true;
    });
});

builder.Services.AddHttpClient<INuGetUpgradeChecker, NuGetUpgradeChecker>(client =>
{
    client.BaseAddress = new Uri("https://api.nuget.org/v3-flatcontainer/", UriKind.Absolute);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("RazorConsoleGallery/1.0");
});

await builder
    .Build()
    .RunAsync();
