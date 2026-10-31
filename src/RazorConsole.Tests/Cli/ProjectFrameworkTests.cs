// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using RazorConsole.Cli.Host;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The framework <c>start --project</c> runs a multi-targeting project with: the highest one with an installed runtime.
/// </summary>
public sealed class ProjectFrameworkTests
{
    [Theory]
    [InlineData("net8.0;net9.0;net10.0;net11.0", "8.0.8,9.0.1,10.0.0", "net10.0")]
    [InlineData("net8.0;net9.0;net10.0;net11.0", "8.0.8,9.0.1,10.0.0,11.0.0-rc.1.26425.128", "net11.0")]
    [InlineData("net8.0;net9.0;net10.0;net11.0", "6.0.26,8.0.21", "net8.0")]
    [InlineData("net10.0;net8.0", "8.0.8,10.0.0", "net10.0")]
    [InlineData("net8.0;net9.0", "9.0.1,10.0.0", "net9.0")]
    [InlineData("net9.0-windows;net8.0", "9.0.1,8.0.8", "net9.0-windows")]
    [InlineData("net8.0;netstandard2.0;net48", "8.0.8", "net8.0")]
    [InlineData("net9.0", "9.0.1", "net9.0")]
    public void TryChoose_PicksTheHighestFrameworkWithARuntime(string frameworks, string runtimes, string expected)
    {
        ProjectFramework.TryChoose(frameworks.Split(';'), runtimes.Split(','), out var framework, out var error).ShouldBeTrue(error);

        framework.ShouldBe(expected);
        error.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("net8.0;net9.0", "10.0.0,11.0.0")]
    [InlineData("net10.0;net11.0", "8.0.8,9.0.1")]
    [InlineData("net9.0", "")]
    [InlineData("netstandard2.0;net48", "8.0.8")]
    public void TryChoose_WithoutARunnableFramework_ListsTheFrameworksAndTheRuntimes(string frameworks, string runtimes)
    {
        var runtimeList = runtimes.Split(',', StringSplitOptions.RemoveEmptyEntries);

        ProjectFramework.TryChoose(frameworks.Split(';'), runtimeList, out var framework, out var error).ShouldBeFalse();

        framework.ShouldBeEmpty();
        error.ShouldContain(frameworks.Replace(";", ", ", StringComparison.Ordinal));
        foreach (var runtime in runtimeList)
        {
            error.ShouldContain(runtime);
        }

        if (runtimeList.Length == 0)
        {
            error.ShouldContain("none");
        }
    }

    [Fact]
    public void ParseRuntimes_KeepsOnlyTheNetCoreAppVersions()
    {
        const string output = """
            Microsoft.AspNetCore.App 9.0.1 [C:\dotnet\shared\Microsoft.AspNetCore.App]
            Microsoft.NETCore.App 8.0.21 [C:\dotnet\shared\Microsoft.NETCore.App]
            Microsoft.NETCore.App 11.0.0-rc.1.26425.128 [/usr/share/dotnet/shared/Microsoft.NETCore.App]
            Microsoft.WindowsDesktop.App 9.0.1 [C:\dotnet\shared\Microsoft.WindowsDesktop.App]
            """;

        ProjectFramework.ParseRuntimes(output).ShouldBe(["8.0.21", "11.0.0-rc.1.26425.128"]);
    }

    [Fact]
    public void BuildArguments_BuildOnlyTheSelectedFrameworkAndConfiguration_AndLeaveNoBuildServersBehind()
        => PtySession.BuildArguments("app.csproj", "net9.0", "Release")
            .ShouldBe(["build", "app.csproj", "-nologo", "--framework", "net9.0", "--configuration", "Release", "-nodeReuse:false", "-p:UseSharedCompilation=false"]);

    [Fact]
    public async Task RunCaptured_ReturnsWhenTheProcessExits_NotWhenAnInheritorOfItsOutputPipesDoes()
    {
        // The shell starts a background process that inherits stdout and stderr, then exits: like a build server.
        var shell = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "start /b ping -n 25 127.0.0.1 & echo built" } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "sleep 25 & echo built" } };
        var watch = Stopwatch.StartNew();

        var (exitCode, tail) = await PtySession.RunCapturedAsync(shell, TimeSpan.FromMinutes(1), "shell", CancellationToken.None);

        exitCode.ShouldBe(0);
        tail.ShouldContain("built");
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData("net9.0", "", "net9.0")]
    [InlineData("", "net8.0;net9.0", "net8.0,net9.0")]
    [InlineData("net9.0", "net8.0; net10.0 ;", "net8.0,net10.0")]
    [InlineData("", "", "")]
    public void ParseFrameworks_PrefersTheListOverTheSingleProperty(string single, string several, string expected)
        => string.Join(',', ProjectFramework.ParseFrameworks(single, several)).ShouldBe(expected);
}
