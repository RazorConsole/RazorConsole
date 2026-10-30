// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Diagnostics;

namespace RazorConsole.Tests.Diagnostics;

public sealed class DiagnosticsPathsTests
{
    [Fact]
    public void Override_WinsOnEveryPlatform()
    {
        var home = Path.Combine(Path.GetTempPath(), "rc-home");
        var env = Env(("RAZORCONSOLE_CLI_HOME", home), ("LOCALAPPDATA", "ignored"), ("XDG_RUNTIME_DIR", "ignored"));

        foreach (var platform in Enum.GetValues<DiagnosticsPlatform>())
        {
            DiagnosticsPaths.ResolveStateDirectory(env, platform).ShouldBe(Path.GetFullPath(home));
        }
    }

    [Fact]
    public void Windows_UsesLocalAppData()
    {
        DiagnosticsPaths.ResolveStateDirectory(Env(("LOCALAPPDATA", "L")), DiagnosticsPlatform.Windows)
            .ShouldBe(Path.Combine("L", "RazorConsole", "cli"));
    }

    [Fact]
    public void MacOS_UsesTmpDir()
    {
        DiagnosticsPaths.ResolveStateDirectory(Env(("TMPDIR", "T")), DiagnosticsPlatform.MacOS)
            .ShouldBe(Path.Combine("T", "razorconsole"));
    }

    [Fact]
    public void Linux_PrefersXdgRuntimeDirThenHomeCache()
    {
        DiagnosticsPaths.ResolveStateDirectory(Env(("XDG_RUNTIME_DIR", "R"), ("HOME", "H")), DiagnosticsPlatform.Unix)
            .ShouldBe(Path.Combine("R", "razorconsole"));
        DiagnosticsPaths.ResolveStateDirectory(Env(("HOME", "H")), DiagnosticsPlatform.Unix)
            .ShouldBe(Path.Combine("H", ".cache", "razorconsole", "run"));
    }

    [Fact]
    public void Endpoints_AreStableAndDistinctPerDirectoryAndProcess()
    {
        DiagnosticsPaths.HostEndpoint("a").ShouldBe(DiagnosticsPaths.HostEndpoint("a"));
        DiagnosticsPaths.HostEndpoint("a").ShouldNotBe(DiagnosticsPaths.HostEndpoint("b"));
        DiagnosticsPaths.AppEndpoint("a", 1).ShouldNotBe(DiagnosticsPaths.AppEndpoint("a", 2));
        DiagnosticsPaths.AppEndpoint("a", 1).ShouldNotBe(DiagnosticsPaths.HostEndpoint("a"));
    }

    [Fact]
    public void StableHash_DoesNotDependOnTheProcess()
    {
        // FNV-1a 64 of "123"; string.GetHashCode would change between runs.
        DiagnosticsPaths.StableHash("123").ShouldBe("456fc2181822c4db");

        // Windows paths are case-insensitive, so their hash is too.
        (DiagnosticsPaths.StableHash("abc") == DiagnosticsPaths.StableHash("ABC")).ShouldBe(OperatingSystem.IsWindows());
    }

    [Theory]
    [InlineData(false, 107, 108)]
    [InlineData(true, 103, 104)]
    public void SocketPath_FitsUpToThePlatformLimit_AndNotOneByteMore(bool macOS, int longestFitting, int shortestFailing)
    {
        var platform = macOS ? DiagnosticsPlatform.MacOS : DiagnosticsPlatform.Unix;
        DiagnosticsPaths.MaxSocketPathBytes(platform).ShouldBe(longestFitting);

        DiagnosticsPaths.EnsureSocketPathFits("/" + new string('a', longestFitting - 1), platform);

        var error = Should.Throw<PathTooLongException>(
            () => DiagnosticsPaths.EnsureSocketPathFits("/" + new string('a', shortestFailing - 1), platform));
        error.Message.ShouldContain($"needs {shortestFailing} bytes");
        error.Message.ShouldContain($"limit is {longestFitting}");
        error.Message.ShouldContain("RAZORCONSOLE_CLI_HOME");
    }

    [Fact]
    public void SocketPath_CountsBytesNotCharacters()
    {
        // 54 two-byte characters plus "/" is 109 bytes but only 55 characters.
        var path = "/" + new string('é', 54);

        Should.Throw<PathTooLongException>(() => DiagnosticsPaths.EnsureSocketPathFits(path, DiagnosticsPlatform.Unix));
    }

    [Fact]
    public void SocketPath_HasNoLimitOnWindows_WherePipesAreNamedInstead()
    {
        DiagnosticsPaths.EnsureSocketPathFits(new string('a', 1000), DiagnosticsPlatform.Windows);
    }

    [Fact]
    public void EnsureUserOnlyDirectory_CreatesAndTightensTheDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "rc-diag-" + Guid.NewGuid().ToString("N")[..8], "state");
        try
        {
            DiagnosticsPaths.EnsureUserOnlyDirectory(path);
            Directory.Exists(path).ShouldBeTrue();

            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode userOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                File.GetUnixFileMode(path).ShouldBe(userOnly);

                File.SetUnixFileMode(path, userOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                DiagnosticsPaths.EnsureUserOnlyDirectory(path);
                File.GetUnixFileMode(path).ShouldBe(userOnly);
            }
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static Func<string, string?> Env(params (string Name, string Value)[] values)
        => name => values.FirstOrDefault(v => v.Name == name).Value;
}
