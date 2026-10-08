// Copyright (c) RazorConsole. All rights reserved.

using FpsBenchmark;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

// Usage: dotnet run -c Release --project examples/FpsBenchmark -- [--legacy|--widget-layout] [--rows N]
var pipeline = args.Contains("--legacy")
    ? RazorConsoleRenderingPipeline.LegacySpectre
    : RazorConsoleRenderingPipeline.WidgetLayout;

var rowsIndex = Array.IndexOf(args, "--rows");
var rows = rowsIndex >= 0 && rowsIndex + 1 < args.Length && int.TryParse(args[rowsIndex + 1], out var parsed) ? parsed : 20;

var counter = new FrameCounter { Pipeline = pipeline, Rows = rows };

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<FpsBenchmark.Components.App>(configure: hostBuilder =>
    {
        hostBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(counter);
            services.Configure<ConsoleAppOptions>(options => options.RenderingPipeline = pipeline);
        });
    });

await builder.Build().RunAsync();
