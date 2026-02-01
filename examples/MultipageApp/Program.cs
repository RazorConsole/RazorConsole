// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

// Set console encoding to UTF-8 for Unicode symbols
Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<MultipageApp.Components.App>();

var host = builder.Build();

await host.RunAsync();

