// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using RazorConsole.Cli.Host;
using RazorConsole.Cli.Operations;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Testing;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The session host in-process: one host per state directory, recovery from a stale <c>host.json</c>, the idle exit,
/// version skew, the orphan sweep, user-only state files, and a real PTY session driven through <see cref="HostOperations"/>.
/// </summary>
public sealed class SessionHostTests : IAsyncDisposable
{
    private const int TestTimeoutMs = 60_000;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rc-host-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<InProcessLauncher> _launchers = [];

    private HostFiles Files => new(_directory);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string AppPath => Path.Combine(AppContext.BaseDirectory, "RazorConsole.Tests.PtyApp.dll");

    public async ValueTask DisposeAsync()
    {
        foreach (var launcher in _launchers)
        {
            await launcher.StopAllAsync();
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ConcurrentStarts_ShareOneHost()
    {
        var launcher = Launcher();
        var connector = new HostConnector(Files, launcher, "1.0.0");

        var clients = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => connector.ConnectOrStartAsync(Token))));
        try
        {
            var status = await StatusAsync(clients[0]);
            status.Clients.ShouldBe(10);
            status.Version.ShouldBe("1.0.0");

            // Every client may launch a host; all but the one holding the lock give up within a second.
            var runs = launcher.Runs;
            var deadline = DateTimeOffset.UtcNow + Patience;
            while (runs.Count(run => run.IsCompleted) < runs.Count - 1 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, Token);
            }

            runs.Count(run => !run.IsCompleted).ShouldBe(1);
            (await Task.WhenAll(runs.Where(run => run.IsCompleted))).ShouldAllBe(started => !started);
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task StaleHostJson_IsReplacedByANewHost()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Files.InfoFile,
            """{"pid":2147483000,"processStartTime":"2000-01-01T00:00:00+00:00","protocol":1,"version":"0.0.1","endpoint":"gone"}""",
            Token);

        var connector = new HostConnector(Files, Launcher(), "1.0.0");
        await using var client = await connector.ConnectOrStartAsync(Token);

        client.HostVersion.ShouldBe("1.0.0");
        var info = Files.TryReadInfo().ShouldNotBeNull();
        info.Pid.ShouldBe(Environment.ProcessId);
        info.Version.ShouldBe("1.0.0");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task IdleHost_Exits_AndRemovesHostJson()
    {
        var launcher = Launcher(TimeSpan.FromMilliseconds(300));
        var connector = new HostConnector(Files, launcher, "1.0.0");
        await (await connector.ConnectOrStartAsync(Token)).DisposeAsync();

        (await launcher.Runs.Single().WaitAsync(Patience, Token)).ShouldBeTrue();
        File.Exists(Files.InfoFile).ShouldBeFalse();
        Files.IsLocked().ShouldBeFalse();
        (await connector.ConnectExistingAsync(Token)).ShouldBeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OlderHost_WithoutSessions_IsReplacedByANewerClient()
    {
        var old = Launcher(version: "1.0.0");
        await (await new HostConnector(Files, old, "1.0.0").ConnectOrStartAsync(Token)).DisposeAsync();

        var connector = new HostConnector(Files, Launcher(version: "1.1.0"), "1.1.0");
        await using var client = await connector.ConnectOrStartAsync(Token);

        client.HostVersion.ShouldBe("1.1.0");
        (await old.Runs.Single().WaitAsync(Patience, Token)).ShouldBeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task NewerHost_IsNotReplacedByAnOlderClient()
    {
        await (await new HostConnector(Files, Launcher(version: "2.0.0"), "2.0.0").ConnectOrStartAsync(Token)).DisposeAsync();

        var connector = new HostConnector(Files, Launcher(version: "1.0.0"), "1.0.0");
        var exception = await Should.ThrowAsync<DiagnosticsProtocolException>(() => connector.ConnectOrStartAsync(Token));

        exception.Code.ShouldBe(DiagnosticsErrorCodes.HostVersionMismatch);
        var data = exception.ErrorData.ShouldNotBeNull();
        data.GetProperty("hostVersion").GetString().ShouldBe("2.0.0");
        data.GetProperty("clientVersion").GetString().ShouldBe("1.0.0");
        data.GetProperty("sessions").GetInt32().ShouldBe(0);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task StateFiles_AreUserOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            // The pipe is created with PipeOptions.CurrentUserOnly (DiagnosticsTransportTests), and the default
            // state directory is under %LOCALAPPDATA%.
            Assert.Skip("Unix file modes only.");
            return;
        }

        await using var client = await new HostConnector(Files, Launcher(), "1.0.0").ConnectOrStartAsync(Token);

        const UnixFileMode userReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.GetUnixFileMode(_directory).ShouldBe(userReadWrite | UnixFileMode.UserExecute);
        File.GetUnixFileMode(Files.Endpoint).ShouldBe(userReadWrite);
        File.GetUnixFileMode(Files.LockFile).ShouldBe(userReadWrite);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OrphanSweep_KillsALedgerProcessWithAMatchingStartTime()
    {
        using var helper = StartHelper();
        WriteLedger("s1", helper.Id, helper.StartTime.ToUniversalTime());

        await (await new HostConnector(Files, Launcher(), "1.0.0").ConnectOrStartAsync(Token)).DisposeAsync();

        await helper.WaitForExitAsync(Token).WaitAsync(Patience, Token);
        helper.HasExited.ShouldBeTrue();
        File.Exists(Files.LedgerFile("s1")).ShouldBeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OrphanSweep_SparesAReusedPid_AndRemovesTheEntry()
    {
        using var helper = StartHelper();
        try
        {
            // The pid is alive, but it started an hour after the recorded process: another process reused the pid.
            WriteLedger("s1", helper.Id, helper.StartTime.ToUniversalTime() - TimeSpan.FromHours(1));

            await (await new HostConnector(Files, Launcher(), "1.0.0").ConnectOrStartAsync(Token)).DisposeAsync();

            File.Exists(Files.LedgerFile("s1")).ShouldBeFalse();
            helper.HasExited.ShouldBeFalse();
        }
        finally
        {
            helper.Kill(entireProcessTree: true);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OrphanSweep_RemovesTheEntryOfAProcessThatIsGone()
    {
        int pid;
        DateTimeOffset startTime;
        using (var helper = StartHelper())
        {
            pid = helper.Id;
            startTime = helper.StartTime.ToUniversalTime();
            helper.Kill(entireProcessTree: true);
            await helper.WaitForExitAsync(Token).WaitAsync(Patience, Token);
        }

        WriteLedger("s1", pid, startTime);

        await using var client = await new HostConnector(Files, Launcher(), "1.0.0").ConnectOrStartAsync(Token);

        File.Exists(Files.LedgerFile("s1")).ShouldBeFalse();
        Directory.EnumerateFileSystemEntries(Files.SessionsDirectory).ShouldBeEmpty();
        (await StatusAsync(client)).Sessions.ShouldBe(0);
    }

    [Fact]
    public void CompareVersions_OrdersNumbersThenReleaseOverPrerelease()
    {
        HostConnector.CompareVersions("1.2.0", "1.10.0").ShouldBeLessThan(0);
        HostConnector.CompareVersions("1.0.0", "1.0.0-beta.1").ShouldBeGreaterThan(0);
        HostConnector.CompareVersions("1.0.0-beta.2", "1.0.0-beta.1").ShouldBeGreaterThan(0);
        HostConnector.CompareVersions("1.0.0", "1.0.0").ShouldBe(0);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task PtySession_StartsTakesInputShowsTheScreenAndStops()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "PTY is not supported on this OS.");

        var operations = new HostOperations(new HostConnector(Files, Launcher(version: "1.0.0"), "1.0.0"));
        var options = new CliGlobalOptions(false, null, TimeSpan.FromSeconds(20));

        var start = await operations.ExecuteAsync(
            new StartRequest(null, StartTargetKind.Dll, AppPath, ["echo", "h"], 80, 12, new Dictionary<string, string>(), null, StartRequest.DefaultTerminalType, false),
            options,
            Token);
        Succeeded(start);
        start.Session.ShouldBe("s1");
        start.Data!.Value.GetProperty("kind").GetString().ShouldBe("pty");
        Files.TryReadCurrent().ShouldBe("s1");
        File.Exists(Files.LedgerFile("s1")).ShouldBeTrue();

        await WaitForScreenAsync(operations, options, "[h] ready");

        Succeeded(await operations.ExecuteAsync(new TypeRequest("hello"), options, Token));
        var keys = await operations.ExecuteAsync(new KeysRequest([new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)], null), options, Token);
        Succeeded(keys);
        keys.Data!.Value.GetProperty("sent").GetInt32().ShouldBe(1);

        var screen = await WaitForScreenAsync(operations, options, "[h] got hello");
        screen.Data!.Value.GetProperty("source").GetString().ShouldBe("pty");
        screen.Data!.Value.GetProperty("width").GetInt32().ShouldBe(80);

        var output = await operations.ExecuteAsync(new OutputRequest(null, null), options, Token);
        Succeeded(output);
        output.Data!.Value.GetProperty("offset").GetInt64().ShouldBe(0);
        output.Text.ShouldNotBeNull().ShouldContain("[h] got hello");

        var list = await operations.ExecuteAsync(new ListRequest(), options, Token);
        Succeeded(list);
        list.Text.ShouldNotBeNull().ShouldStartWith("* s1 ");

        // A newer client never replaces a host that holds sessions.
        var newer = new HostConnector(Files, Launcher(version: "9.0.0"), "9.0.0");
        var mismatch = await Should.ThrowAsync<DiagnosticsProtocolException>(() => newer.ConnectOrStartAsync(Token));
        mismatch.Code.ShouldBe(DiagnosticsErrorCodes.HostVersionMismatch);
        mismatch.ErrorData!.Value.GetProperty("sessions").GetInt32().ShouldBe(1);

        // PtyApp's echo mode exits with 42 on Ctrl+C.
        var stop = await operations.ExecuteAsync(new StopRequest(null, TimeSpan.FromSeconds(10)), options, Token);
        Succeeded(stop);
        stop.Data!.Value.GetProperty("exitCode").GetInt32().ShouldBe(42);
        stop.Data!.Value.GetProperty("killed").GetBoolean().ShouldBeFalse();
        Files.TryReadCurrent().ShouldBeNull();
        File.Exists(Files.LedgerFile("s1")).ShouldBeFalse();

        var gone = await operations.ExecuteAsync(new ScreenRequest(ScreenFormat.Text, null), options with { Session = "s1" }, Token);
        gone.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.SessionNotFound);

        var hostStop = await operations.ExecuteAsync(new HostStopRequest(), options, Token);
        Succeeded(hostStop);
        var status = await operations.ExecuteAsync(new HostStatusRequest(), options, Token);
        status.Data!.Value.GetProperty("running").GetBoolean().ShouldBeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task SessionCommands_WithoutAHost_ReportSessionNotFound()
    {
        var launcher = Launcher();
        var operations = new HostOperations(new HostConnector(Files, launcher, "1.0.0"));
        var options = new CliGlobalOptions(false, null, TimeSpan.FromSeconds(5));

        (await operations.ExecuteAsync(new TypeRequest("x"), options, Token)).Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.SessionNotFound);
        (await operations.ExecuteAsync(new TypeRequest("x"), options with { Session = "s1" }, Token)).Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.SessionNotFound);

        var list = await operations.ExecuteAsync(new ListRequest(), options, Token);
        Succeeded(list);
        list.Data!.Value.GetProperty("sessions").GetArrayLength().ShouldBe(0);
        launcher.Runs.ShouldBeEmpty();
    }

    // A child that runs until killed: PtyApp's echo mode blocks reading the redirected stdin, which stays open.
    private static Process StartHelper()
    {
        var startInfo = new ProcessStartInfo(PtyTerminal.ResolveDotnetHost())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add(AppPath);
        startInfo.ArgumentList.Add("echo");
        startInfo.ArgumentList.Add("orphan");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The helper did not start.");
    }

    private void WriteLedger(string session, int pid, DateTimeOffset startTime)
    {
        Directory.CreateDirectory(Files.SessionsDirectory);
        File.WriteAllBytes(
            Files.LedgerFile(session),
            JsonSerializer.SerializeToUtf8Bytes(new SessionLedgerEntry(pid, startTime), HostJsonContext.Default.SessionLedgerEntry));
    }

    private static void Succeeded(CliResult result)
        => result.Error.ShouldBeNull(result.Error is { } error ? $"{error.Code}: {error.Message}" : null);

    private static async Task<HostStatusResult> StatusAsync(HostClient client)
        => await client.RequestAsync(
            HostMethods.Status,
            new EmptyParams(),
            HostJsonContext.Default.EmptyParams,
            HostJsonContext.Default.HostStatusResult,
            Patience,
            Token);

    private static async Task<CliResult> WaitForScreenAsync(HostOperations operations, CliGlobalOptions options, string text)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (true)
        {
            var screen = await operations.ExecuteAsync(new ScreenRequest(ScreenFormat.Json, null), options, Token);
            Succeeded(screen);
            if (screen.Text!.Contains(text, StringComparison.Ordinal))
            {
                return screen;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"'{text}' did not appear. Screen:\n{screen.Text}");
            }

            await Task.Delay(50, Token);
        }
    }

    private InProcessLauncher Launcher(TimeSpan? idleTimeout = null, string version = "1.0.0")
    {
        var launcher = new InProcessLauncher(idleTimeout ?? TimeSpan.FromMinutes(10), version);
        _launchers.Add(launcher);
        return launcher;
    }

    // Runs each launched host on the thread pool instead of a new process.
    private sealed class InProcessLauncher(TimeSpan idleTimeout, string version) : IHostLauncher
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

            await Task.WhenAll(hosts.Select(entry => entry.Run)).WaitAsync(Patience);
        }
    }
}
