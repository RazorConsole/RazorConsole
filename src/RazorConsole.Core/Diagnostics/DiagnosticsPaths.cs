// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Per-user locations shared by diagnostics endpoints and the CLI session host.
/// </summary>
/// <remarks>
/// Windows: <c>%LOCALAPPDATA%\RazorConsole\cli</c> with named pipes. Linux: <c>$XDG_RUNTIME_DIR/razorconsole</c>,
/// else <c>~/.cache/razorconsole/run</c>. macOS: <c>$TMPDIR/razorconsole</c>. Unix directories are mode 0700 and
/// hold the sockets. RAZORCONSOLE_CLI_HOME overrides the directory on every OS.
/// </remarks>
internal static class DiagnosticsPaths
{
    internal const string HomeEnvironmentVariableName = "RAZORCONSOLE_CLI_HOME";

    private const UnixFileMode UserOnlyDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public static string ResolveStateDirectory()
        => ResolveStateDirectory(Environment.GetEnvironmentVariable, CurrentPlatform());

    internal static string ResolveStateDirectory(Func<string, string?> getEnvironmentVariable, DiagnosticsPlatform platform)
    {
        var overridePath = getEnvironmentVariable(HomeEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        switch (platform)
        {
            case DiagnosticsPlatform.Windows:
                var localAppData = getEnvironmentVariable("LOCALAPPDATA");
                if (string.IsNullOrEmpty(localAppData))
                {
                    localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                }

                return Path.Combine(localAppData, "RazorConsole", "cli");

            case DiagnosticsPlatform.MacOS:
                var temp = getEnvironmentVariable("TMPDIR");
                return Path.Combine(string.IsNullOrEmpty(temp) ? Path.GetTempPath() : temp, "razorconsole");

            default:
                var runtimeDirectory = getEnvironmentVariable("XDG_RUNTIME_DIR");
                if (!string.IsNullOrEmpty(runtimeDirectory))
                {
                    return Path.Combine(runtimeDirectory, "razorconsole");
                }

                var home = getEnvironmentVariable("HOME");
                if (string.IsNullOrEmpty(home))
                {
                    home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                return Path.Combine(home, ".cache", "razorconsole", "run");
        }
    }

    /// <summary>
    /// Creates <paramref name="path"/> if needed. On Unix it is created as, or tightened to, mode 0700;
    /// tightening fails when the directory belongs to someone else.
    /// </summary>
    public static string EnsureUserOnlyDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // %LOCALAPPDATA% is already private to the user; RAZORCONSOLE_CLI_HOME is the user's choice.
            Directory.CreateDirectory(path);
            return path;
        }

        EnsureUnixUserOnlyDirectory(path);
        return path;
    }

    public static string AppsDirectory(string stateDirectory) => Path.Combine(stateDirectory, "apps");

    /// <summary>
    /// The discovery file of the app with process id <paramref name="processId"/>.
    /// </summary>
    public static string AppDiscoveryFile(string stateDirectory, int processId)
        => Path.Combine(AppsDirectory(stateDirectory), processId.ToString(CultureInfo.InvariantCulture) + ".json");

    /// <summary>
    /// The session host endpoint: a pipe name on Windows, a socket path elsewhere.
    /// </summary>
    public static string HostEndpoint(string stateDirectory)
        => OperatingSystem.IsWindows()
            ? $"razorconsole-host-{StableHash(stateDirectory)}"
            : Path.Combine(stateDirectory, "host.sock");

    /// <summary>
    /// The diagnostics endpoint of the app with process id <paramref name="processId"/>.
    /// </summary>
    public static string AppEndpoint(string stateDirectory, int processId)
        => OperatingSystem.IsWindows()
            ? $"razorconsole-app-{StableHash(stateDirectory)}-{processId.ToString(CultureInfo.InvariantCulture)}"
            : Path.Combine(AppsDirectory(stateDirectory), processId.ToString(CultureInfo.InvariantCulture) + ".sock");

    /// <summary>
    /// The longest Unix domain socket path in bytes, not counting the terminator: <c>sun_path</c> holds 104 bytes on macOS
    /// and 108 on Linux.
    /// </summary>
    internal static int MaxSocketPathBytes(DiagnosticsPlatform platform) => platform == DiagnosticsPlatform.MacOS ? 103 : 107;

    /// <summary>
    /// Throws when a socket at <paramref name="path"/> cannot be created or reached on this platform, with a message that
    /// names the way out. Named pipes on Windows have no such limit.
    /// </summary>
    /// <exception cref="PathTooLongException">The path is longer than the platform's socket path limit.</exception>
    internal static void EnsureSocketPathFits(string path) => EnsureSocketPathFits(path, CurrentPlatform());

    internal static void EnsureSocketPathFits(string path, DiagnosticsPlatform platform)
    {
        if (platform == DiagnosticsPlatform.Windows)
        {
            return;
        }

        var limit = MaxSocketPathBytes(platform);
        var bytes = Encoding.UTF8.GetByteCount(path);
        if (bytes > limit)
        {
            throw new PathTooLongException(
                $"The socket path '{path}' needs {bytes.ToString(CultureInfo.InvariantCulture)} bytes and the limit is {limit.ToString(CultureInfo.InvariantCulture)}; set {HomeEnvironmentVariableName} to a shorter path.");
        }
    }

    // Pipe names are machine-wide, so they carry a hash of the per-user directory. FNV-1a: stable across
    // processes and runtimes, unlike string.GetHashCode. Access control comes from PipeOptions.CurrentUserOnly.
    internal static string StableHash(string value)
    {
        var normalized = OperatingSystem.IsWindows() ? value.ToUpperInvariant() : value;
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(normalized))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static DiagnosticsPlatform CurrentPlatform()
        => OperatingSystem.IsWindows() ? DiagnosticsPlatform.Windows
            : OperatingSystem.IsMacOS() ? DiagnosticsPlatform.MacOS
            : DiagnosticsPlatform.Unix;

    [UnsupportedOSPlatform("windows")]
    private static void EnsureUnixUserOnlyDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            Directory.CreateDirectory(path, UserOnlyDirectoryMode);
        }

        if (File.GetUnixFileMode(path) != UserOnlyDirectoryMode)
        {
            File.SetUnixFileMode(path, UserOnlyDirectoryMode);
        }
    }
}

internal enum DiagnosticsPlatform
{
    Windows,
    MacOS,
    Unix,
}
