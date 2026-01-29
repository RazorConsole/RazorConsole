// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<MultipageApp.Components.App>();

var host = builder.Build();

await host.RunAsync();

