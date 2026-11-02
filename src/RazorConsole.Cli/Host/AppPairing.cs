// Copyright (c) RazorConsole. All rights reserved.

using System.Security.Cryptography;
using System.Text.Json;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Host;

/// <summary>
/// Finds the diagnostics endpoint of the app a session runs, through the discovery files the apps write.
/// </summary>
internal static class AppPairing
{
    /// <summary>
    /// A random token for one run of a PTY session, 32 hex characters (the endpoint accepts 128 of [A-Za-z0-9-_]).
    /// </summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>
    /// The discovery file whose token is <paramref name="token"/>, else the one of process <paramref name="fallbackPid"/>
    /// (an app that cleared its environment). Files of processes that are gone, or whose pid was reused, are ignored.
    /// </summary>
    public static DiagnosticsDiscoveryInfo? Locate(string stateDirectory, string? token, int? fallbackPid)
    {
        var directory = DiagnosticsPaths.AppsDirectory(stateDirectory);
        string[] files;
        try
        {
            files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        DiagnosticsDiscoveryInfo? byPid = null;
        foreach (var path in files)
        {
            if (Read(path) is not { } info || !IsAlive(info))
            {
                continue;
            }

            if (token is not null && string.Equals(info.SessionToken, token, StringComparison.Ordinal))
            {
                return info;
            }

            if (fallbackPid == info.Pid)
            {
                byPid = info;
            }
        }

        return byPid;
    }

    /// <summary>
    /// The discovery file of process <paramref name="pid"/>, if that process is the app that wrote it.
    /// </summary>
    public static DiagnosticsDiscoveryInfo? ForPid(string stateDirectory, int pid)
        => Read(DiagnosticsPaths.AppDiscoveryFile(stateDirectory, pid)) is { } info && info.Pid == pid && IsAlive(info) ? info : null;

    public static bool IsRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the discovery file at <paramref name="path"/> is left over: it cannot be parsed, or its app is gone (or the
    /// pid belongs to another process now). A file that cannot be read for now is not stale.
    /// </summary>
    /// <param name="path">The discovery file.</param>
    /// <param name="pid">The app's process id, when the file could be parsed.</param>
    public static bool IsStale(string path, out int? pid)
    {
        pid = null;
        try
        {
            var info = JsonSerializer.Deserialize(File.ReadAllBytes(path), DiagnosticsJsonContext.Default.DiagnosticsDiscoveryInfo);
            if (info is null)
            {
                return true;
            }

            pid = info.Pid;
            return !IsAlive(info);
        }
        catch (JsonException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsAlive(DiagnosticsDiscoveryInfo info)
    {
        if (!SessionHost.SameProcess(info.Pid, info.ProcessStartTime, out var process))
        {
            return false;
        }

        process!.Dispose();
        return true;
    }

    private static DiagnosticsDiscoveryInfo? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(path), DiagnosticsJsonContext.Default.DiagnosticsDiscoveryInfo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Being written or removed: the next attempt sees the final state.
            return null;
        }
    }
}
