// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Runtime.Versioning;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Testing.Pty;

namespace RazorConsole.Cli.Host;

/// <summary>
/// Starts a session host for a state directory. Tests replace it to run the host in-process.
/// </summary>
internal interface IHostLauncher
{
    void Launch(HostFiles files);
}

/// <summary>
/// Starts <c>razorconsole __host</c> as a detached background process with no standard streams, so it never holds
/// the caller's console or a CI step's output pipe open after the command that started it returns.
/// </summary>
internal sealed class ProcessHostLauncher : IHostLauncher
{
    public const string HostVerb = "__host";

    public static ProcessHostLauncher Instance { get; } = new();

    public void Launch(HostFiles files)
    {
        // The host's working directory: on a first run nothing has created the state directory yet, and neither
        // CreateProcess nor Process.Start start a process in a directory that does not exist.
        DiagnosticsPaths.EnsureUserOnlyDirectory(files.StateDirectory);
        var (fileName, arguments) = HostCommand();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            LaunchDetachedWindows(fileName, arguments, files.StateDirectory);
            return;
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = files.StateDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The host keeps its ends of the pipes; closing ours means it never writes to this process's output.
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The session host did not start.");
        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();
    }

    /// <summary>
    /// How to run this tool again: the apphost or <c>dotnet RazorConsole.Cli.dll</c>.
    /// </summary>
    internal static (string FileName, IReadOnlyList<string> Arguments) HostCommand()
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The tool's own path is unknown.");
        var name = Path.GetFileNameWithoutExtension(processPath);
        if (string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assembly = Path.Combine(AppContext.BaseDirectory, typeof(ProcessHostLauncher).Assembly.GetName().Name + ".dll");
            return (processPath, [assembly, HostVerb]);
        }

        return (processPath, [HostVerb]);
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static unsafe void LaunchDetachedWindows(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        const uint DetachedProcess = 0x00000008;
        const uint CreateNewProcessGroup = 0x00000200;

        // No inherited handles and null standard handles: Process.Start would inherit every inheritable handle,
        // including a CI runner's output pipe, which then stays open until the host exits.
        var startupInfo = new WindowsNative.StartupInfoEx
        {
            Cb = sizeof(WindowsNative.StartupInfoEx) - sizeof(nint),
            Flags = WindowsNative.StartfUseStdHandles,
        };
        var commandLine = (WindowsPtyConnection.BuildCommandLine(fileName, arguments) + '\0').ToCharArray();
        WindowsNative.ProcessInformation processInformation;
        fixed (char* commandLinePointer = commandLine)
        {
            if (!WindowsNative.CreateProcess(
                null,
                commandLinePointer,
                0,
                0,
                inheritHandles: false,
                DetachedProcess | CreateNewProcessGroup,
                null,
                workingDirectory,
                &startupInfo,
                &processInformation))
            {
                throw new System.ComponentModel.Win32Exception();
            }
        }

        WindowsNative.CloseHandle(processInformation.Thread);
        WindowsNative.CloseHandle(processInformation.Process);
    }
}
