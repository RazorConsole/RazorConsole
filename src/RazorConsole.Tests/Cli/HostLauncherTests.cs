// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Host;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The host starts itself from whatever binary the tool runs as: the NativeAOT executable, the tool's apphost, or
/// <c>dotnet RazorConsole.Cli.dll</c>.
/// </summary>
public sealed class HostLauncherTests
{
    [Theory]
    [InlineData("/usr/local/bin/razorconsole")]
    [InlineData(@"C:\tools\razorconsole.exe")]
    [InlineData(@"C:\Users\me\.dotnet\tools\razorconsole.exe")]
    public void ANativeBinaryOrAnApphost_RunsItselfWithTheHostVerb(string processPath)
    {
        var (fileName, arguments) = ProcessHostLauncher.HostCommand(processPath, "/base");

        fileName.ShouldBe(processPath);
        arguments.ShouldBe(["__host"]);
    }

    [Theory]
    [InlineData("/usr/share/dotnet/dotnet")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(@"C:\Program Files\dotnet\DOTNET.EXE")]
    public void TheDotnetHost_RunsTheToolsAssemblyWithTheHostVerb(string processPath)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "razorconsole-cli");

        var (fileName, arguments) = ProcessHostLauncher.HostCommand(processPath, baseDirectory);

        fileName.ShouldBe(processPath);
        arguments.ShouldBe([Path.Combine(baseDirectory, "RazorConsole.Cli.dll"), "__host"]);
    }

    [Fact]
    public void ThisProcess_HasAHostCommand()
    {
        var (fileName, arguments) = ProcessHostLauncher.HostCommand();

        fileName.ShouldBe(Environment.ProcessPath);
        arguments[^1].ShouldBe("__host");
    }
}
