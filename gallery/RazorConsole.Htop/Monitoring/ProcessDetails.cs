// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Globalization;

namespace RazorConsole.Htop.Monitoring;

/// <summary>
/// Collects the extra per-process facts shown when a row is expanded. Everything is best effort:
/// protected processes throw on many of these properties, so each lookup is isolated.
/// </summary>
internal static class ProcessDetails
{
    public static IReadOnlyList<string> Load(int pid)
    {
        var lines = new List<string>();
        try
        {
            using var p = Process.GetProcessById(pid);
            Add(lines, "Path", Try(() => p.MainModule?.FileName));
            Add(lines, "Command", CommandLines.Get(pid));

            var started = Try(() => (DateTime?)p.StartTime);
            var startedText = started is { } s
                ? $"{s:yyyy-MM-dd HH:mm:ss} (up {FormatSpan(DateTime.Now - s)})"
                : null;
            var priority = Try(() => $"{p.PriorityClass} (base {p.BasePriority})");
            Add(lines, "Started", startedText, "Priority", priority, "Session", Try(() => p.SessionId.ToString(CultureInfo.InvariantCulture)));

            Add(lines, "Handles", Try(() => p.HandleCount.ToString(CultureInfo.InvariantCulture)),
                "Threads", Try(() => p.Threads.Count.ToString(CultureInfo.InvariantCulture)),
                "Responding", Try(() => p.Responding ? "yes" : "no"));

            Add(lines, "Private", Try(() => FormatBytes(p.PrivateMemorySize64)),
                "Virtual", Try(() => FormatBytes(p.VirtualMemorySize64)),
                "Peak RSS", Try(() => FormatBytes(p.PeakWorkingSet64)),
                "Paged", Try(() => FormatBytes(p.PagedMemorySize64)));

            Add(lines, "User time", Try(() => FormatSpan(p.UserProcessorTime)),
                "Kernel time", Try(() => FormatSpan(p.PrivilegedProcessorTime)));

            if (OperatingSystem.IsLinux())
            {
                var status = ReadLinuxStatus(pid);
                Add(lines, "PPid", status.GetValueOrDefault("PPid"), "State", status.GetValueOrDefault("State"), "UID", status.GetValueOrDefault("Uid")?.Split('\t', ' ')[0]);
            }

            var title = Try(() => p.MainWindowTitle);
            Add(lines, "Window", string.IsNullOrWhiteSpace(title) ? null : title);
        }
        catch (Exception)
        {
        }

        if (lines.Count == 0)
        {
            lines.Add("No further details available (process exited or access denied)");
        }

        return lines;
    }

    private static void Add(List<string> lines, params string?[] pairs)
    {
        var parts = new List<string>();
        for (var i = 0; i + 1 < pairs.Length; i += 2)
        {
            if (!string.IsNullOrEmpty(pairs[i + 1]))
            {
                parts.Add($"{pairs[i]}: {Sanitize(pairs[i + 1]!)}");
            }
        }

        if (parts.Count > 0)
        {
            lines.Add(string.Join("   ", parts));
        }
    }

    // Brackets would be re-interpreted by the console markup layer, so keep the text plain.
    private static string Sanitize(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                '[' => '(',
                ']' => ')',
                '\0' or '\r' or '\n' or '\t' => ' ',
                var c => c,
            };
        }

        return new string(chars).Trim();
    }

    private static T? Try<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return default;
        }
    }

    private static Dictionary<string, string> ReadLinuxStatus(int pid)
    {
        var result = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.ReadLines($"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/status"))
            {
                var index = line.IndexOf(':');
                if (index > 0)
                {
                    result[line[..index]] = line[(index + 1)..].Trim();
                }
            }
        }
        catch (Exception)
        {
        }

        return result;
    }

    private static string FormatSpan(TimeSpan span) => span.TotalDays >= 1
        ? $"{(int)span.TotalDays}d {span.Hours}h"
        : span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m {span.Seconds}s";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "K", "M", "G", "T"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#}{units[unit]}");
    }
}
