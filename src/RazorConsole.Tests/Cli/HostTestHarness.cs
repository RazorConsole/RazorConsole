// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Host;

namespace RazorConsole.Tests.Cli;

// Runs each launched host on the thread pool instead of a new process.
internal sealed class InProcessLauncher(TimeSpan idleTimeout, string version, TimeProvider? time = null) : IHostLauncher
{
    private readonly List<(SessionHost Host, Task<bool> Run)> _hosts = [];

    public IReadOnlyList<Task<bool>> Runs
    {
        get
        {
            lock (_hosts)
            {
                return [.. _hosts.Select(entry => entry.Run)];
            }
        }
    }

    public void Launch(HostFiles files)
    {
        var host = new SessionHost(files, idleTimeout, version, time);
        lock (_hosts)
        {
            _hosts.Add((host, Task.Run(() => host.RunAsync(CancellationToken.None))));
        }
    }

    public async Task StopAllAsync()
    {
        (SessionHost Host, Task<bool> Run)[] hosts;
        lock (_hosts)
        {
            hosts = [.. _hosts];
        }

        foreach (var (host, _) in hosts)
        {
            host.RequestShutdown();
        }

        await Task.WhenAll(hosts.Select(entry => entry.Run)).WaitAsync(TimeSpan.FromSeconds(15));
    }
}

// A clock that only moves when the test says so.
internal sealed class ManualTimeProvider : TimeProvider
{
    // Not zero: a zero timestamp means "not taken yet" to the code under test, and a real clock never returns it.
    private long _ticks = TimeSpan.TicksPerSecond;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
