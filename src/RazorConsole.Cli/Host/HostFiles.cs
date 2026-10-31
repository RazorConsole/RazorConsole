// Copyright (c) RazorConsole. All rights reserved.

using System.Reflection;
using System.Text.Json;
using RazorConsole.Core.Diagnostics;

namespace RazorConsole.Cli.Host;

/// <summary>
/// The session host's files in the per-user state directory (see <see cref="DiagnosticsPaths"/>):
/// <c>host.lock</c>, held open by the running host; <c>host.json</c>, describing it; <c>current</c>, the current
/// session's name; and <c>sessions/&lt;name&gt;.json</c>, one ledger entry per running child process.
/// </summary>
internal sealed class HostFiles(string stateDirectory)
{
    private const UnixFileMode UserReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public string StateDirectory { get; } = stateDirectory;

    public string LockFile => Path.Combine(StateDirectory, "host.lock");

    public string InfoFile => Path.Combine(StateDirectory, "host.json");

    public string CurrentFile => Path.Combine(StateDirectory, "current");

    public string SessionsDirectory => Path.Combine(StateDirectory, "sessions");

    public string Endpoint => DiagnosticsPaths.HostEndpoint(StateDirectory);

    /// <summary>
    /// The tool version without build metadata, for example <c>0.4.0-preview.1</c>.
    /// </summary>
    public static string ToolVersion { get; } = ReadToolVersion();

    public static HostFiles Resolve(Func<string, string?> getEnvironmentVariable)
        => new(DiagnosticsPaths.ResolveStateDirectory(
            getEnvironmentVariable,
            OperatingSystem.IsWindows() ? DiagnosticsPlatform.Windows
                : OperatingSystem.IsMacOS() ? DiagnosticsPlatform.MacOS
                : DiagnosticsPlatform.Unix));

    public string LedgerFile(string session) => Path.Combine(SessionsDirectory, session + ".json");

    /// <summary>
    /// Opens <c>host.lock</c> exclusively, or returns <see langword="null"/> when another process holds it, which means
    /// a host is alive. Nothing else is needed: the lock goes away with its process, however that process ends.
    /// </summary>
    public FileStream? TryAcquireLock()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UserReadWrite;
        }

        try
        {
            return new FileStream(LockFile, options);
        }
        catch (IOException ex) when (ex is not DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a host process holds the lock right now.
    /// </summary>
    public bool IsLocked()
    {
        // A host creates the lock before anything else; without it (or the directory) none runs.
        if (!File.Exists(LockFile))
        {
            return false;
        }

        using var stream = TryAcquireLock();
        return stream is null;
    }

    public HostInfo? TryReadInfo()
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(InfoFile), HostJsonContext.Default.HostInfo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public string? TryReadCurrent()
    {
        try
        {
            var name = File.ReadAllText(CurrentFile).Trim();
            return name.Length == 0 ? null : name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void WriteCurrent(string name)
    {
        DiagnosticsPaths.EnsureUserOnlyDirectory(StateDirectory);
        WriteAtomically(CurrentFile, System.Text.Encoding.UTF8.GetBytes(name));
    }

    /// <summary>
    /// Clears <c>current</c> if it still names <paramref name="name"/>.
    /// </summary>
    public void ClearCurrent(string name)
    {
        if (string.Equals(TryReadCurrent(), name, StringComparison.Ordinal))
        {
            TryDelete(CurrentFile);
        }
    }

    /// <summary>
    /// Writes <paramref name="payload"/> aside and moves it into place, so readers never see a partial file.
    /// On Linux and macOS the file is created with mode 0600.
    /// </summary>
    public static void WriteAtomically(string path, ReadOnlySpan<byte> payload)
    {
        var temporary = path + "." + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UserReadWrite;
        }

        try
        {
            using (var file = new FileStream(temporary, options))
            {
                file.Write(payload);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ReadToolVersion()
    {
        var version = typeof(HostFiles).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(HostFiles).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
