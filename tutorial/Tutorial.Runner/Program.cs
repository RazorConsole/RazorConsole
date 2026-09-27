// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using Tutorial.Components.Chapters;

var builder = Host.CreateApplicationBuilder(args);
if (args.Contains("--mouse-events", StringComparer.OrdinalIgnoreCase))
{
    builder.UseRazorConsole<MouseEvents>(configure: app =>
    {
        app.Services.Configure<ConsoleAppOptions>(options =>
        {
            options.ConsoleLiveDisplayOptions.EnableMouseEvents = true;
        });
    });
}
else if (args.Contains("--state-events", StringComparer.OrdinalIgnoreCase))
{
    builder.UseRazorConsole<StateAndEvents>();
}
else if (args.Contains("--text-input", StringComparer.OrdinalIgnoreCase))
{
    builder.UseRazorConsole<TextInputAndFocus>();
}
else if (args.Contains("--layout", StringComparer.OrdinalIgnoreCase))
{
    builder.UseRazorConsole<WidgetLayoutAndResize>(configure: app =>
    {
        app.Services.Configure<ConsoleAppOptions>(options => options.EnableTerminalResizing = true);
    });
}
else if (args.Contains("--routing", StringComparer.OrdinalIgnoreCase))
{
    builder.UseRazorConsole<RoutingDemo>();
}
else if (args.Contains("--async-work", StringComparer.OrdinalIgnoreCase))
{
    builder.UseRazorConsole<AsyncWork>();
}
else if (args.Contains("--complete-app", StringComparer.OrdinalIgnoreCase))
{
    builder.UseRazorConsole<CompleteApp>(configure: app =>
    {
        app.Services.Configure<ConsoleAppOptions>(options =>
        {
            options.ConsoleLiveDisplayOptions.EnableMouseEvents = true;
        });
    });
}
else
{
    builder.UseRazorConsole<HelloWorld>();
}

using var host = builder.Build();
await host.RunAsync();
