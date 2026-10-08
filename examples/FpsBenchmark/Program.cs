// Copyright (c) RazorConsole. All rights reserved.

using FpsBenchmark;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using Spectre.Console;

// Usage: dotnet run -c Release --project examples/FpsBenchmark -- [--rows N]
var rowsIndex = Array.IndexOf(args, "--rows");
var rows = rowsIndex >= 0 && rowsIndex + 1 < args.Length && int.TryParse(args[rowsIndex + 1], out var parsed) ? parsed : 20;

var counter = new FrameCounter { Rows = rows };

AnsiConsole.Console = new CountingConsole(AnsiConsole.Console, counter);

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<FpsBenchmark.Components.App>(configure: hostBuilder =>
    {
        hostBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(counter);
            services.Configure<ConsoleAppOptions>(options => options.RenderingPipeline = RazorConsoleRenderingPipeline.WidgetLayout);
        });
    });

await builder.Build().RunAsync();
