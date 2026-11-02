// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using RazorConsole.Cli.Host;
using RazorConsole.Testing;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// Which <c>dotnet</c> builds and runs a project: the first on PATH, as it is, and only then the muxer's fallbacks. A
/// version manager's shim picks the SDK by directory when it runs, so it must never be resolved to its target.
/// </summary>
public sealed class DotnetLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rc-dotnet-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void ADotnetOnPath_WinsOverHostPathRootAndTheDefaultLocation()
    {
        var shims = MakeDirectory("shims", "dotnet");
        var hostPath = MakeDirectory("host", "dotnet");
        var root = MakeDirectory("root", "dotnet");
        var fallback = MakeDirectory("default", "dotnet");

        var found = Resolve(
            new() { ["PATH"] = shims, ["DOTNET_HOST_PATH"] = Path.Combine(hostPath, "dotnet"), ["DOTNET_ROOT"] = root },
            windows: false,
            fallback);

        // As PATH gives it: the shim itself, not what it would run.
        found.ShouldBe(Path.Combine(shims, "dotnet"));
    }

    [Fact]
    public void TheFirstDotnetOnPath_Wins_AndEntriesWithoutOneAreSkipped()
    {
        var empty = MakeDirectory("empty");
        var first = MakeDirectory("first", "dotnet");
        var second = MakeDirectory("second", "dotnet");

        var found = Resolve(new() { ["PATH"] = string.Join(Path.PathSeparator, empty, first, second) }, windows: false);

        found.ShouldBe(Path.Combine(first, "dotnet"));
    }

    [Fact]
    public void WithoutOneOnPath_TheFallbacksCome_InTheMuxersOrder()
    {
        var hostPath = MakeDirectory("host", "dotnet");
        var archRoot = MakeDirectory("arch", "dotnet");
        var root = MakeDirectory("root", "dotnet");
        var fallback = MakeDirectory("default", "dotnet");
        var variables = new Dictionary<string, string?> { ["PATH"] = MakeDirectory("empty") };

        // DOTNET_HOST_PATH, then DOTNET_ROOT_<ARCH>, then DOTNET_ROOT, then the default install location.
        variables["DOTNET_HOST_PATH"] = Path.Combine(hostPath, "dotnet");
        variables["DOTNET_ROOT_X64"] = archRoot;
        variables["DOTNET_ROOT"] = root;
        Resolve(variables, windows: false, fallback).ShouldBe(Path.Combine(hostPath, "dotnet"));

        variables.Remove("DOTNET_HOST_PATH");
        Resolve(variables, windows: false, fallback).ShouldBe(Path.Combine(archRoot, "dotnet"));

        variables.Remove("DOTNET_ROOT_X64");
        Resolve(variables, windows: false, fallback).ShouldBe(Path.Combine(root, "dotnet"));

        variables.Remove("DOTNET_ROOT");
        Resolve(variables, windows: false, fallback).ShouldBe(Path.Combine(fallback, "dotnet"));
    }

    [Fact]
    public void OnWindows_PathextDecidesTheExtension_AndAnExtensionlessFileIsNoProgram()
    {
        var both = MakeDirectory("both", "dotnet.exe", "dotnet.cmd");
        var scriptOnly = MakeDirectory("script", "dotnet");

        Resolve(new() { ["PATH"] = both, ["PATHEXT"] = ".exe;.cmd" }, windows: true).ShouldBe(Path.Combine(both, "dotnet.exe"));
        Resolve(new() { ["PATH"] = both, ["PATHEXT"] = ".cmd;.exe" }, windows: true).ShouldBe(Path.Combine(both, "dotnet.cmd"));

        // A Unix shim has no extension: PATHEXT does not list it, so a Windows search skips it for the next entry.
        var real = MakeDirectory("real", "dotnet.exe");
        Resolve(new() { ["PATH"] = string.Join(Path.PathSeparator, scriptOnly, real), ["PATHEXT"] = ".exe" }, windows: true)
            .ShouldBe(Path.Combine(real, "dotnet.exe"));
    }

    [Fact]
    public async Task TheDotnetOnPath_IsTheOneThatRuns_ShimsIncluded()
    {
        // A fake dotnet that says which one it is, standing in for a version manager's shim.
        var shims = MakeDirectory("shims");
        var name = OperatingSystem.IsWindows() ? "dotnet.cmd" : "dotnet";
        var script = Path.Combine(shims, name);
        await File.WriteAllTextAsync(script, OperatingSystem.IsWindows() ? "@echo shim-ran\r\n" : "#!/bin/sh\necho shim-ran\n", TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var found = Resolve(new() { ["PATH"] = shims, ["PATHEXT"] = ".cmd" }, OperatingSystem.IsWindows());
        var (exitCode, tail) = await PtySession.RunCapturedAsync(new ProcessStartInfo(found), TimeSpan.FromMinutes(1), "fake dotnet", TestContext.Current.CancellationToken);

        found.ShouldBe(script);
        exitCode.ShouldBe(0);
        tail.ShouldContain("shim-ran");
    }

    [Fact]
    public void AProjectsDirectory_IsItsOwnForADirectory_AndTheParentForAFile()
    {
        var project = MakeDirectory("app", "app.csproj");

        ProjectFramework.ProjectDirectory(project).ShouldBe(project);
        ProjectFramework.ProjectDirectory(Path.Combine(project, "app.csproj")).ShouldBe(project);
    }

    [Fact]
    public async Task ChildOutput_IsReadAsUtf8_WhateverTheCodePageIs()
    {
        // The app prints UTF-8 to a pipe, as dotnet build does; decoding it with the ANSI code page gives mojibake.
        var startInfo = new ProcessStartInfo(PtyTerminal.ResolveDotnetHost())
        {
            ArgumentList = { Path.Combine(AppContext.BaseDirectory, "RazorConsole.Tests.PtyApp.dll"), "args", "Привет ● héllo — 你好" },
        };

        var (exitCode, tail) = await PtySession.RunCapturedAsync(startInfo, TimeSpan.FromMinutes(1), "args", TestContext.Current.CancellationToken);

        exitCode.ShouldBe(0);
        tail.ShouldContain("[Привет ● héllo — 你好]");
    }

    private string MakeDirectory(string name, params string[] files)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(path, file), "x");
        }

        return path;
    }

    private static string Resolve(Dictionary<string, string?> variables, bool windows, string? defaultDirectory = null)
        => DotnetLocator.Resolve(name => variables.GetValueOrDefault(name), windows, defaultDirectory is null ? [] : [defaultDirectory], "X64");
}
