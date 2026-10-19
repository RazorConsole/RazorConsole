// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using RazorConsole.Tests.PtyApp;

// Modes:
//   golden              renders GoldenView with RazorConsole
//   echo <id>           echoes lines as "[id] got <line>"; "exit <code>" exits with that code; Ctrl+C exits with 42
//   serial              validates "L<n>:<60 letters>" lines; "end" prints a summary
//   size                prints the console size whenever it changes
//   env <names...>      prints environment variables and the working directory, then exits
//   envdump <file>      writes every environment variable as "name=value\0" to the file, prints "dumped", then exits
//   args <args...>      prints each argument in brackets, then exits
// Like RazorConsole itself, use UTF-8 regardless of the console code page.
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

return args.FirstOrDefault() switch
{
    "golden" => await RunGoldenAsync(args),
    "echo" => Echo(args[1]),
    "serial" => Serial(),
    "size" => Size(),
    "env" => PrintEnvironment(args[1..]),
    "envdump" => DumpEnvironment(args[1]),
    "args" => PrintArguments(args[1..]),
    _ => 2,
};

static async Task<int> RunGoldenAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args[1..]);
    builder.UseRazorConsole<GoldenView>(configure: config =>
    {
        config.Services.Configure<ConsoleAppOptions>(options =>
        {
            options.RenderingPipeline = RazorConsoleRenderingPipeline.WidgetLayout;
            options.EnableTerminalResizing = true;
            options.ConsoleLiveDisplayOptions.UseAlternateScreenBuffer = true;
        });
    });

    await builder.Build().RunAsync();
    return 0;
}

static int Echo(string id)
{
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.WriteLine($"[{id}] interrupted");
        Environment.Exit(42);
    };

    Console.WriteLine($"[{id}] ready");
    while (Console.ReadLine() is { } line)
    {
        if (line.StartsWith("exit ", StringComparison.Ordinal))
        {
            Console.WriteLine($"[{id}] bye");
            return int.Parse(line.AsSpan(5), CultureInfo.InvariantCulture);
        }

        Console.WriteLine($"[{id}] got {line}");
    }

    // On Windows, Ctrl+C also ends ReadLine; give the handler time to exit with its code instead.
    Thread.Sleep(TimeSpan.FromSeconds(5));
    return 1;
}

static int Serial()
{
    var ok = 0;
    var bad = 0;
    var seen = new HashSet<int>();
    Console.WriteLine("serial ready");
    while (Console.ReadLine() is { } line && line != "end")
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (line.StartsWith('L')
            && colon > 1
            && int.TryParse(line.AsSpan(1, colon - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && line.AsSpan(colon + 1).SequenceEqual(new string((char)('a' + (number % 26)), 60)))
        {
            ok++;
            seen.Add(number);
        }
        else
        {
            bad++;
            Console.WriteLine($"bad line: {line}");
        }
    }

    Console.WriteLine($"summary ok={ok} bad={bad} distinct={seen.Count}");
    Console.ReadLine();
    return 0;
}

static int Size()
{
    var last = (-1, -1);
    while (true)
    {
        var current = (Console.WindowWidth, Console.WindowHeight);
        if (current != last)
        {
            Console.WriteLine($"size {current.WindowWidth}x{current.WindowHeight}");
            last = current;
        }

        Thread.Sleep(20);
    }
}

static int PrintEnvironment(string[] names)
{
    foreach (var name in names)
    {
        Console.WriteLine($"env {name}={Environment.GetEnvironmentVariable(name) ?? "<unset>"}");
    }

    Console.WriteLine($"cwd {Directory.GetCurrentDirectory()}");
    return 0;
}

static int DumpEnvironment(string path)
{
    var entries = Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>()
        .Select(entry => $"{entry.Key}={entry.Value}\0");
    File.WriteAllText(path, string.Concat(entries), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine("dumped");
    return 0;
}

static int PrintArguments(string[] arguments)
{
    Console.WriteLine("args " + string.Concat(arguments.Select(argument => $"[{argument}]")));
    return 0;
}
