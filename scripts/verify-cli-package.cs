// Copyright (c) RazorConsole. All rights reserved.

// Checks the packed RazorConsole.Cli dotnet tool, and optionally installs it and runs it. Works the same on Windows,
// Linux and macOS:
//
//   dotnet run scripts/verify-cli-package.cs -- --package-directory <dir> [--install]
//   dotnet run scripts/verify-cli-package.cs -- --native <path to the razorconsole binary>
//
// Package checks open the .nupkg and fail, listing every problem found, unless:
//   - both tools/net8.0/any and tools/net10.0/any have a DotnetToolSettings.xml for the command "razorconsole";
//   - both folders contain RazorConsole.Cli.dll, RazorConsole.Core.dll and RazorConsole.Testing.dll;
//   - no RazorConsole.Tests* assembly or source file is packed;
//   - the nuspec is a DotnetTool with a readme and no dependency on RazorConsole.Core or RazorConsole.Testing
//     (they are bundled, so a dependency would be a version-skew bug);
//   - the packed readme is the tool's own, not the repository README;
//   - the net8.0 runtimeconfig rolls forward to a newer major runtime, so the tool starts where only .NET 9 is installed.
//
// With --install the tool is also installed into a temporary tool path from the package directory alone, then
// "razorconsole --version" must print the package version and "razorconsole doctor --json" must succeed against a
// fresh state directory. Doctor starts the real session host and a pseudoterminal session, so this runs the packed tool
// end to end. The host is stopped and the tool uninstalled at the end.
//
// With --native the same run happens against a NativeAOT binary: --version must succeed and doctor must pass, which
// starts the session host from the native binary itself.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var arguments = Arguments.Parse(args);
if (arguments.Error is { } usage)
{
    Console.Error.WriteLine(usage);
    Console.Error.WriteLine("Usage: verify-cli-package.cs --package-directory <dir> [--install] | --native <binary>");
    return 2;
}

try
{
    if (arguments.Native is { } native)
    {
        await Tool.VerifyAsync(Path.GetFullPath(native), expectedVersion: null);
        return 0;
    }

    var version = Package.Verify(arguments.PackageDirectory!);
    if (arguments.Install)
    {
        await Tool.InstallAndVerifyAsync(Path.GetFullPath(arguments.PackageDirectory!), version);
    }

    return 0;
}
catch (VerificationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

internal sealed class VerificationException(string message) : Exception(message);

internal sealed record Arguments(string? PackageDirectory, bool Install, string? Native, string? Error)
{
    public static Arguments Parse(string[] args)
    {
        string? directory = null;
        string? native = null;
        var install = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--package-directory" when i + 1 < args.Length:
                    directory = args[++i];
                    break;
                case "--native" when i + 1 < args.Length:
                    native = args[++i];
                    break;
                case "--install":
                    install = true;
                    break;
                default:
                    return new Arguments(null, false, null, $"Unknown or incomplete argument '{args[i]}'.");
            }
        }

        return (directory, native) switch
        {
            (null, null) => new Arguments(null, false, null, "Give --package-directory or --native."),
            (not null, not null) => new Arguments(null, false, null, "--package-directory and --native cannot be combined."),
            _ => new Arguments(directory, install, native, null),
        };
    }
}

internal static class Package
{
    private static readonly string[] Frameworks = ["net8.0", "net10.0"];
    private static readonly string[] Assemblies = ["RazorConsole.Cli.dll", "RazorConsole.Core.dll", "RazorConsole.Testing.dll"];

    // Returns the package version.
    public static string Verify(string packageDirectory)
    {
        var packages = Directory.GetFiles(packageDirectory, "RazorConsole.Cli.*.nupkg")
            .Where(static path => !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (packages.Length != 1)
        {
            throw new VerificationException($"Expected exactly one RazorConsole.Cli.<version>.nupkg in '{packageDirectory}', found {packages.Length}.");
        }

        var file = Path.GetFileName(packages[0]);
        var version = file["RazorConsole.Cli.".Length..^".nupkg".Length];
        Console.WriteLine($"Package: {packages[0]}");
        Console.WriteLine($"Version: {version}");

        var problems = new List<string>();
        void Add(string message)
        {
            problems.Add(message);
            Console.WriteLine($"  FAIL  {message}");
        }

        using (var archive = ZipFile.OpenRead(packages[0]))
        {
            var names = archive.Entries.Select(static entry => entry.FullName).ToList();

            foreach (var framework in Frameworks)
            {
                var folder = $"tools/{framework}/any";
                var settings = Read(archive, $"{folder}/DotnetToolSettings.xml");
                if (settings is null)
                {
                    Add($"{folder}/DotnetToolSettings.xml is missing.");
                }
                else
                {
                    if (!settings.Contains("Name=\"razorconsole\"", StringComparison.Ordinal))
                    {
                        Add($"{folder}/DotnetToolSettings.xml does not declare the command 'razorconsole'.");
                    }

                    if (!settings.Contains("EntryPoint=\"RazorConsole.Cli.dll\"", StringComparison.Ordinal))
                    {
                        Add($"{folder}/DotnetToolSettings.xml does not use RazorConsole.Cli.dll as the entry point.");
                    }
                }

                foreach (var assembly in Assemblies)
                {
                    if (!names.Contains($"{folder}/{assembly}"))
                    {
                        Add($"{folder}/{assembly} is missing.");
                    }
                }
            }

            foreach (var name in names.Where(static name => Regex.IsMatch(name, @"(^|/)RazorConsole\.Tests") || Regex.IsMatch(name, @"\.(cs|razor)$")))
            {
                Add($"{name} must not be packed.");
            }

            var nuspecName = names.FirstOrDefault(static name => name.EndsWith(".nuspec", StringComparison.Ordinal));
            var nuspec = nuspecName is null ? null : Read(archive, nuspecName);
            if (nuspec is null)
            {
                Add("The package has no nuspec.");
            }
            else
            {
                if (!nuspec.Contains("<packageType name=\"DotnetTool\"", StringComparison.Ordinal))
                {
                    Add("The nuspec is not a DotnetTool package.");
                }

                if (!nuspec.Contains("<readme>", StringComparison.Ordinal))
                {
                    Add("The nuspec has no readme.");
                }

                if (Regex.IsMatch(nuspec, "<dependency id=\"RazorConsole\\.(Core|Testing)\""))
                {
                    Add("The nuspec depends on RazorConsole.Core or RazorConsole.Testing, which are bundled in the tool.");
                }
            }

            var readme = Read(archive, "README.md");
            if (readme is null)
            {
                Add("README.md is missing.");
            }
            else if (!readme.TrimStart('﻿').StartsWith("# RazorConsole.Cli", StringComparison.Ordinal))
            {
                Add("README.md is not the tool's own readme (it must start with '# RazorConsole.Cli').");
            }

            var runtimeConfig = Read(archive, "tools/net8.0/any/RazorConsole.Cli.runtimeconfig.json");
            if (runtimeConfig is null)
            {
                Add("tools/net8.0/any/RazorConsole.Cli.runtimeconfig.json is missing.");
            }
            else if (!Regex.IsMatch(runtimeConfig, "\"rollForward\"\\s*:\\s*\"Major\""))
            {
                Add("tools/net8.0/any/RazorConsole.Cli.runtimeconfig.json does not roll forward to a newer major runtime; the tool would not start where only a newer .NET is installed. Set <RollForward>Major</RollForward>.");
            }
        }

        if (problems.Count > 0)
        {
            throw new VerificationException($"The package '{file}' has {problems.Count} problem(s):\n - {string.Join("\n - ", problems)}");
        }

        Console.WriteLine("  ok    package contents");
        return version;
    }

    private static string? Read(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        if (entry is null)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

internal static class Tool
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(3);

    public static async Task InstallAndVerifyAsync(string packageDirectory, string version)
    {
        var work = Path.Combine(Path.GetTempPath(), "rc-cli-verify-" + Guid.NewGuid().ToString("N")[..8]);
        var toolPath = Path.Combine(work, "tool");
        Directory.CreateDirectory(work);

        // Only the package directory is a source, so what is installed is what was packed. Running from the work directory
        // keeps the repository's global.json out of the way.
        var config = Path.Combine(work, "nuget.config");
        await File.WriteAllTextAsync(config, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="package-under-test" value="{System.Security.SecurityElement.Escape(packageDirectory)}" />
              </packageSources>
            </configuration>
            """);

        var installed = false;
        var tool = Path.Combine(toolPath, OperatingSystem.IsWindows() ? "razorconsole.exe" : "razorconsole");
        var stateHome = Path.Combine(work, "home");
        try
        {
            var install = await RunAsync("dotnet", ["tool", "install", "RazorConsole.Cli", "--tool-path", toolPath, "--configfile", config, "--version", version], work, null);
            if (install.ExitCode != 0)
            {
                throw new VerificationException($"dotnet tool install failed (exit {install.ExitCode}):\n{install.Everything}");
            }

            installed = true;
            Console.WriteLine("  ok    dotnet tool install");
            await VerifyAsync(tool, version, stateHome);
        }
        finally
        {
            if (File.Exists(tool))
            {
                await RunAsync(tool, ["host", "stop"], work, stateHome);
            }

            if (installed)
            {
                await RunAsync("dotnet", ["tool", "uninstall", "RazorConsole.Cli", "--tool-path", toolPath], work, null);
            }

            TryDelete(work);
        }
    }

    // Runs --version and doctor --json of a binary. Doctor starts the session host from the binary, so a binary that cannot
    // start itself again fails here.
    public static async Task VerifyAsync(string tool, string? expectedVersion, string? stateHome = null)
    {
        var work = Path.Combine(Path.GetTempPath(), "rc-cli-verify-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);

        // A state directory that does not exist yet, as on a user's first run.
        stateHome ??= Path.Combine(work, "home");
        try
        {
            var reported = await RunAsync(tool, ["--version"], work, stateHome);
            if (reported.ExitCode != 0 || reported.Output.Length == 0
                || (expectedVersion is not null && !reported.Output.StartsWith(expectedVersion, StringComparison.Ordinal)))
            {
                throw new VerificationException($"razorconsole --version printed '{reported.Everything}' (exit {reported.ExitCode}), expected {(expectedVersion is null ? "a version" : $"it to start with '{expectedVersion}'")}.");
            }

            Console.WriteLine($"  ok    razorconsole --version ({reported.Output})");

            var doctor = await RunAsync(tool, ["doctor", "--json"], work, stateHome);
            JsonDocument? result = null;
            try
            {
                result = JsonDocument.Parse(doctor.Output);
            }
            catch (JsonException)
            {
                // Reported below with the raw output.
            }

            using (result)
            {
                if (doctor.ExitCode != 0 || result is null
                    || !result.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                {
                    throw new VerificationException($"razorconsole doctor --json failed (exit {doctor.ExitCode}):\n{doctor.Everything}");
                }

                var checks = result.RootElement.GetProperty("checks").EnumerateArray().ToList();
                if (checks.Any(static check => check.GetProperty("status").GetString() == "fail"))
                {
                    throw new VerificationException($"razorconsole doctor reported failed checks:\n{doctor.Everything}");
                }

                Console.WriteLine($"  ok    razorconsole doctor ({checks.Count} checks)");
            }
        }
        finally
        {
            await RunAsync(tool, ["host", "stop"], work, stateHome);
            TryDelete(work);
        }
    }

    private sealed record RunResult(int ExitCode, string Output, string Error)
    {
        public string Everything => Error.Length == 0 ? Output : $"{Output}\n{Error}".Trim();
    }

    private static async Task<RunResult> RunAsync(string file, string[] arguments, string workingDirectory, string? stateHome)
    {
        var startInfo = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (stateHome is not null)
        {
            startInfo.Environment["RAZORCONSOLE_CLI_HOME"] = stateHome;
        }

        using var process = Process.Start(startInfo) ?? throw new VerificationException($"{file} did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(CommandTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new VerificationException($"{file} {string.Join(' ', arguments)} did not finish in {CommandTimeout.TotalMinutes:0} minutes.");
        }

        // Standard output alone is what a JSON result is parsed from; the error stream only explains a failure.
        return new RunResult(process.ExitCode, (await output).Trim(), (await error).Trim());
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
