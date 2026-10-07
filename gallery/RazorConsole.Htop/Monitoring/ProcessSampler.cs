// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;

namespace RazorConsole.Htop.Monitoring;

public sealed record ProcessEntry(int Id, string Name, double Cpu, long MemoryBytes, int Threads, TimeSpan CpuTime);

public sealed record SystemSnapshot(
    IReadOnlyList<ProcessEntry> Processes,
    double TotalCpuPercent,
    long UsedMemoryBytes,
    long TotalMemoryBytes,
    int ThreadCount);

/// <summary>
/// Samples running processes using only <see cref="Process"/>, so it works on Windows, Linux and macOS under Native AOT.
/// CPU is reported htop-style: 100% equals one fully busy core.
/// </summary>
public sealed class ProcessSampler
{
    private readonly Dictionary<int, TimeSpan> _previousCpu = [];
    private long _previousTimestamp;

    public SystemSnapshot Sample()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = _previousTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(_previousTimestamp, now).TotalSeconds;
        var nextCpu = new Dictionary<int, TimeSpan>();
        var entries = new List<ProcessEntry>();
        long memory = 0;
        var threads = 0;
        double cpuSum = 0;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var id = process.Id;
                    var cpuTime = TryGet(() => process.TotalProcessorTime, TimeSpan.Zero);
                    var cpu = 0.0;
                    if (elapsedSeconds > 0 && _previousCpu.TryGetValue(id, out var previous) && cpuTime >= previous)
                    {
                        cpu = (cpuTime - previous).TotalSeconds / elapsedSeconds * 100.0;
                    }

                    nextCpu[id] = cpuTime;
                    var working = TryGet(() => process.WorkingSet64, 0L);
                    var threadCount = TryGet(() => process.Threads.Count, 0);
                    entries.Add(new ProcessEntry(id, process.ProcessName, cpu, working, threadCount, cpuTime));
                    memory += working;
                    threads += threadCount;
                    cpuSum += cpu;
                }
                catch (Exception)
                {
                    // The process exited or is not accessible; skip it.
                }
            }
        }

        _previousCpu.Clear();
        foreach (var pair in nextCpu)
        {
            _previousCpu[pair.Key] = pair.Value;
        }

        _previousTimestamp = now;
        var totalCpu = Math.Min(100.0, cpuSum / Environment.ProcessorCount);
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return new SystemSnapshot(entries, totalCpu, Math.Min(memory, total), total, threads);
    }

    private static T TryGet<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}

public enum SortColumn { Pid, Name, Cpu, Memory, Threads, Time }

public static class ProcessSorting
{
    public static IEnumerable<ProcessEntry> Sort(IEnumerable<ProcessEntry> source, SortColumn column, bool descending)
    {
        IOrderedEnumerable<ProcessEntry> ordered = column switch
        {
            SortColumn.Pid => Order(source, p => p.Id, descending),
            SortColumn.Name => descending
                ? source.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            SortColumn.Cpu => Order(source, p => p.Cpu, descending),
            SortColumn.Memory => Order(source, p => p.MemoryBytes, descending),
            SortColumn.Threads => Order(source, p => p.Threads, descending),
            _ => Order(source, p => p.CpuTime, descending),
        };
        return ordered.ThenBy(p => p.Id);
    }

    private static IOrderedEnumerable<ProcessEntry> Order<TKey>(IEnumerable<ProcessEntry> source, Func<ProcessEntry, TKey> key, bool descending)
        => descending ? source.OrderByDescending(key) : source.OrderBy(key);
}
