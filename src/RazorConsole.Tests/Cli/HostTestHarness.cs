// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Host;

namespace RazorConsole.Tests.Cli;

// Runs each launched host on the thread pool instead of a new process.
internal sealed class InProcessLauncher(TimeSpan idleTimeout, string version) : IHostLauncher
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
        var host = new SessionHost(files, idleTimeout, version);
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
