// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.InteropServices;
using RazorConsole.Testing;

namespace RazorConsole.Cli.Host;

/// <summary>
/// Finds the <c>dotnet</c> that builds and runs a project, the way a shell would: the first <c>dotnet</c> on
/// <c>PATH</c>, then the places the dotnet muxer itself falls back to.
/// </summary>
/// <remarks>
/// What PATH gives is used as it is. A version manager's shim (mise, asdf, ...) picks the SDK by the directory it runs
/// in, so resolving it to its target would pick one for it.
/// </remarks>
internal static class DotnetLocator
{
    /// <summary>
    /// The <c>dotnet</c> of this process's environment.
    /// </summary>
    public static string Resolve()
        => Resolve(
            static name => Environment.GetEnvironmentVariable(name),
            OperatingSystem.IsWindows(),
            DefaultInstallDirectories(),
            RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant());

    /// <summary>
    /// Finds <c>dotnet</c> in this order: <c>PATH</c> (with <c>PATHEXT</c> on Windows), <c>DOTNET_HOST_PATH</c>,
    /// <c>DOTNET_ROOT_&lt;ARCH&gt;</c> and <c>DOTNET_ROOT</c>, the default install directories, and last what
    /// <see cref="PtyTerminal"/> finds from the running process.
    /// </summary>
    /// <param name="variable">Reads an environment variable.</param>
    /// <param name="windows">Whether to search like Windows: <c>dotnet.exe</c> and the other <c>PATHEXT</c> extensions.</param>
    /// <param name="defaultInstallDirectories">The directories the muxer falls back to when nothing else says where .NET is.</param>
    /// <param name="architecture">The process architecture, upper case, for <c>DOTNET_ROOT_&lt;ARCH&gt;</c>.</param>
    internal static string Resolve(Func<string, string?> variable, bool windows, IEnumerable<string> defaultInstallDirectories, string architecture)
    {
        var names = ExecutableNames(variable, windows);
        foreach (var directory in SplitPath(variable("PATH")))
        {
            if (Find(directory, names) is { } onPath)
            {
                return onPath;
            }
        }

        if (variable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath && File.Exists(hostPath))
        {
            return hostPath;
        }

        foreach (var root in new[] { variable("DOTNET_ROOT_" + architecture), variable("DOTNET_ROOT") }.Concat(defaultInstallDirectories))
        {
            if (!string.IsNullOrEmpty(root) && Find(root, names) is { } inRoot)
            {
                return inRoot;
            }
        }

        return PtyTerminal.ResolveDotnetHost();
    }

    internal static IEnumerable<string> DefaultInstallDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/usr/local/share/dotnet";
        }
        else
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/lib/dotnet";
        }
    }

    private static string[] ExecutableNames(Func<string, string?> variable, bool windows)
    {
        if (!windows)
        {
            return ["dotnet"];
        }

        var extensions = (variable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static extension => extension.StartsWith('.'))
            .ToList();
        return [.. extensions.Select(static extension => "dotnet" + extension)];
    }

    private static IEnumerable<string> SplitPath(string? path)
        => (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(static entry => entry.Trim('"'));

    private static string? Find(string directory, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            try
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }

        return null;
    }
}
