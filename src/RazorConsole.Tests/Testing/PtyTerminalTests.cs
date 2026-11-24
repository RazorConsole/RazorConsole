// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Runtime.Versioning;
using RazorConsole.Testing;
using RazorConsole.Testing.Pty;

namespace RazorConsole.Tests.Testing;

/// <summary>
/// PtyTerminal plumbing: process start, input, output, exit codes, and isolation between concurrent terminals.
/// The child is RazorConsole.Tests.PtyApp; see its Program.cs for the modes.
/// </summary>
public sealed class PtyTerminalTests
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void IsSupported_IsTrueOnWindowsLinuxAndMacOS()
    {
        PtyTerminal.IsSupported.ShouldBe(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
    }

    [Fact]
    public async Task StartAsync_PassesArgumentsVerbatim_AndReportsExitCode()
    {
        await using var terminal = await StartAppAsync("args", "a", "b c", "d\"e", "", "back\\slash\\", "ünï");

        var frame = await terminal.WaitForTextAsync("args ", StartTimeout, TestContext.Current.CancellationToken);
        (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(0);

        terminal.CurrentFrame.ContainsText("args [a][b c][d\"e][][back\\slash\\][ünï]").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.HasExited.ShouldBeTrue();
        terminal.ExitCode.ShouldBe(0);
        frame.ShouldNotBeNull();
    }

    [Fact]
    public async Task StartAsync_AppliesEnvironmentOverrides_AndWorkingDirectory()
    {
        var options = PtyTerminal.CreateDotnetOptions(AppPath, 100, 10, ["env", "PTY_TERMINAL_TEST"]);
        options.Environment["PTY_TERMINAL_TEST"] = "value 1";
        await using var terminal = await PtyTerminal.StartAsync(options, TestContext.Current.CancellationToken);

        await terminal.WaitForTextAsync("env PTY_TERMINAL_TEST=value 1", StartTimeout, TestContext.Current.CancellationToken);
        var frame = await terminal.WaitForTextAsync("cwd ", StartTimeout, TestContext.Current.CancellationToken);
        frame.ScreenText.ShouldContain("cwd " + Path.GetDirectoryName(AppPath)!.TrimEnd(Path.DirectorySeparatorChar));
        (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public void BuildEnvironment_InheritsOverridesAndRemoves_AndSetsTermOnUnixOnly()
    {
        var inherited = Environment.GetEnvironmentVariables().Keys.Cast<string>().First(name => !string.Equals(name, "TERM", StringComparison.OrdinalIgnoreCase));
        var options = new PtyTerminalOptions { TerminalType = "xterm-test" };
        options.Environment["PTY_ADDED"] = "added";
        options.Environment[inherited] = null;

        var environment = PtyTerminal.BuildEnvironment(options);

        environment["PTY_ADDED"].ShouldBe("added");
        environment.ContainsKey(inherited).ShouldBeFalse();
        if (OperatingSystem.IsWindows())
        {
            environment.TryGetValue("TERM", out var term).ShouldBe(Environment.GetEnvironmentVariable("TERM") is not null);
            term.ShouldBe(Environment.GetEnvironmentVariable("TERM"));
        }
        else
        {
            environment["TERM"].ShouldBe("xterm-test");
        }

        options.Environment["TERM"] = "explicit";
        PtyTerminal.BuildEnvironment(options)["TERM"].ShouldBe("explicit");
    }

    [Fact]
    public async Task StartAsync_MissingProgram_Throws()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "Pseudoterminals are not supported here.");
        var options = new PtyTerminalOptions { FileName = Path.Combine(AppContext.BaseDirectory, "does-not-exist-" + Guid.NewGuid().ToString("N")) };

        var exception = await Should.ThrowAsync<Exception>(() => PtyTerminal.StartAsync(options, TestContext.Current.CancellationToken));
        exception.Message.ShouldContain("does-not-exist-");
    }

    [Fact]
    public async Task Resize_ResizesTheTerminal_AndNotifiesTheApp()
    {
        await using var terminal = await StartAppAsync(80, 24, "size");
        await terminal.WaitForTextAsync("size 80x24", StartTimeout, TestContext.Current.CancellationToken);

        terminal.Resize(100, 30);

        terminal.Width.ShouldBe(100);
        terminal.Height.ShouldBe(30);
        var frame = await terminal.WaitForTextAsync("size 100x30", StartTimeout, TestContext.Current.CancellationToken);
        frame.Width.ShouldBe(100);
        frame.Height.ShouldBe(30);
    }

    [Fact]
    public async Task CtrlC_ReachesTheApp_AsAnInterrupt()
    {
        await using var terminal = await StartEchoAsync("ctrl");

        await terminal.SendKeyAsync(ConsoleKey.C, '\u0003', ConsoleModifiers.Control, TestContext.Current.CancellationToken);

        (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(42, terminal.DumpDiagnostics());
        await terminal.WaitForTextAsync("[ctrl] interrupted", StartTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unix_TheAppIsTheForegroundProcessGroupOfItsTerminal()
    {
        // SIGINT from Ctrl+C and SIGWINCH from a resize go to this group: without a controlling terminal it is 0.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await using var terminal = await StartEchoAsync("pgrp");
            ForegroundProcessGroup(terminal).ShouldBe(terminal.ProcessId, terminal.DumpDiagnostics());
        }
        else
        {
            Assert.Skip("Linux and macOS only.");
        }
    }

    [Fact]
    public async Task Unix_TheStartRunsNoShellStartupCode_AndPassesTheEnvironmentThrough()
    {
        // On macOS bash attaches the terminal before the app starts; BASH_ENV and SHELLOPTS must not reach it.
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Skip("Linux and macOS only.");
        }

        var directory = Directory.CreateTempSubdirectory("pty-bash-env-");
        try
        {
            var marker = Path.Combine(directory.FullName, "sourced");
            var script = Path.Combine(directory.FullName, "bash_env.sh");
            File.WriteAllText(script, $"echo sourced > '{marker}'\n");
            var options = PtyTerminal.CreateDotnetOptions(AppPath, 200, 10, ["env", "BASH_ENV", "SHELLOPTS"]);
            options.Environment["BASH_ENV"] = script;
            options.Environment["SHELLOPTS"] = "xtrace";

            await using (var terminal = await PtyTerminal.StartAsync(options, TestContext.Current.CancellationToken))
            {
                await terminal.WaitForTextAsync("cwd ", StartTimeout, TestContext.Current.CancellationToken);
                (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(0);

                var screen = terminal.CurrentFrame.ScreenText;
                screen.ShouldContain("env BASH_ENV=" + script);
                screen.ShouldContain("env SHELLOPTS=xtrace");
                screen.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)
                    .ShouldAllBe(line => line.StartsWith("env ", StringComparison.Ordinal) || line.StartsWith("cwd ", StringComparison.Ordinal), terminal.DumpDiagnostics());
                terminal.RawOutput.ShouldNotContain("+ exec");
            }

            File.Exists(marker).ShouldBeFalse();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Unix_TheAppGetsExactlyTheCallersEnvironment()
    {
        // On macOS the environment passes through bash and env(1): what bash acts on, rewrites or adds must not show.
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Skip("Linux and macOS only.");
        }

        var directory = Directory.CreateTempSubdirectory("pty-env-dump-");
        try
        {
            var dump = Path.Combine(directory.FullName, "environment");
            var options = PtyTerminal.CreateDotnetOptions(AppPath, 100, 10, ["envdump", dump]);
            options.Environment["BASH_ENV"] = Path.Combine(directory.FullName, "bash_env.sh");
            options.Environment["ENV"] = Path.Combine(directory.FullName, "env.sh");
            options.Environment["SHELLOPTS"] = "allexport:xtrace";
            options.Environment["POSIXLY_CORRECT"] = "1";
            options.Environment["BASH_FUNC_pty_probe%%"] = "() {  echo probe\n}";
            options.Environment["PTY_SECRET"] = "pty-secret-" + Guid.NewGuid().ToString("N");
            options.Environment["PTY.NOT-AN-IDENTIFIER"] = "kept";
            options.Environment["PTY_SPACES"] = "  a b\tc =  ";
            options.Environment["PWD"] = "/pty/caller/pwd";
            options.Environment["OLDPWD"] = "/pty/caller/oldpwd";
            options.Environment["_"] = "/pty/caller/underscore";
            options.Environment["IFS"] = ":";
            options.Environment["PPID"] = "1";
            options.Environment["OPTIND"] = "7";
            options.Environment["RANDOM"] = "4";
            options.Environment["BASH_VERSION"] = "caller";
            options.Environment["SHLVL"] = null;
            var expected = PtyTerminal.BuildEnvironment(options).Select(pair => pair.Key + "=" + pair.Value);

            await using (var terminal = await PtyTerminal.StartAsync(options, TestContext.Current.CancellationToken))
            {
                await terminal.WaitForTextAsync("dumped", StartTimeout, TestContext.Current.CancellationToken);
                (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(0, terminal.DumpDiagnostics());
            }

            var actual = File.ReadAllText(dump).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            actual.Order(StringComparer.Ordinal).ShouldBe(expected.Order(StringComparer.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BashLaunch_PutsOnlyBashsOwnVariablesInArgv()
    {
        // bash and env(1) exec in the same process, so their argv and the app's are all taken from these arguments.
        // Each lives only until the next exec, so a test sampling another process's argv (ps) would prove nothing.
        var secret = "pty-secret-" + Guid.NewGuid().ToString("N");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GITHUB_TOKEN"] = secret,
            ["PTY_SECRET"] = "prefix " + secret,
            ["PATH"] = "/usr/bin:/bin",
            ["BASH_ENV"] = "/pty/bash_env.sh",
            ["SHELLOPTS"] = "xtrace",
            ["BASH_FUNC_pty_probe%%"] = "() {  echo probe\n}",
            ["SHLVL"] = "3",
        };

        var (arguments, bashEnvironment) = BashLaunch.Build("/usr/local/share/dotnet/dotnet", "/dev/ttys001", "/pty/work", environment);

        arguments.ShouldAllBe(argument => !argument.Contains(secret, StringComparison.Ordinal));
        arguments.SkipWhile(argument => argument != "--").Skip(1).ShouldBe(
            ["BASH_ENV=/pty/bash_env.sh", "SHELLOPTS=xtrace", "BASH_FUNC_pty_probe%%=() {  echo probe\n}", "SHLVL=3", "/usr/local/share/dotnet/dotnet"],
            ignoreOrder: true);
        arguments[^1].ShouldBe("/usr/local/share/dotnet/dotnet");
        bashEnvironment.ShouldBe(["GITHUB_TOKEN=" + secret, "PTY_SECRET=prefix " + secret, "PATH=/usr/bin:/bin", "SHLVL=3"], ignoreOrder: true);
        var unset = arguments.Select((argument, index) => (argument, index))
            .Where(pair => pair.argument == "-u")
            .Select(pair => arguments[pair.index + 1]);
        unset.ShouldBe(BashLaunch.Rewritten.Where(name => name != "SHLVL"), ignoreOrder: true);
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static int ForegroundProcessGroup(PtyTerminal terminal) => ((UnixPtyConnection)terminal.Connection).ForegroundProcessGroup;

    [Fact]
    public void Windows_ConsoleCtrlC_ClearsAndRestoresTheIgnoreBit()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            ClearsAndRestoresTheIgnoreBit();
        }
        else
        {
            Assert.Skip("Windows only.");
        }
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static void ClearsAndRestoresTheIgnoreBit()
    {
        lock (WindowsPtyConnection.s_processCreationLock)
        {
            var original = WindowsPtyConnection.ConsoleCtrlC.TryReadConsoleFlags();
            original.ShouldNotBeNull();
            try
            {
                WindowsNative.SetConsoleCtrlHandler(0, true);
                (WindowsPtyConnection.ConsoleCtrlC.TryReadConsoleFlags()!.Value & 1).ShouldBe(1);

                WindowsPtyConnection.ConsoleCtrlC.ClearIgnore().ShouldBeTrue();
                (WindowsPtyConnection.ConsoleCtrlC.TryReadConsoleFlags()!.Value & 1).ShouldBe(0);

                WindowsPtyConnection.ConsoleCtrlC.Restore(true);
                (WindowsPtyConnection.ConsoleCtrlC.TryReadConsoleFlags()!.Value & 1).ShouldBe(1);

                WindowsNative.SetConsoleCtrlHandler(0, false);
                WindowsPtyConnection.ConsoleCtrlC.ClearIgnore().ShouldBeFalse();
                (WindowsPtyConnection.ConsoleCtrlC.TryReadConsoleFlags()!.Value & 1).ShouldBe(0);
            }
            finally
            {
                WindowsNative.SetConsoleCtrlHandler(0, (original.Value & 1) != 0);
            }
        }
    }

    [Fact]
    public async Task Windows_CtrlC_Works_WhenTheTestProcessIgnoresCtrlC_AndTheBitIsRestored()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            await CtrlCWorksWhileIgnoredAsync();
        }
        else
        {
            Assert.Skip("Windows only.");
        }
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static async Task CtrlCWorksWhileIgnoredAsync()
    {
        bool originallyIgnored;
        lock (WindowsPtyConnection.s_processCreationLock)
        {
            originallyIgnored = (WindowsPtyConnection.ConsoleCtrlC.TryReadConsoleFlags()!.Value & 1) != 0;
            WindowsNative.SetConsoleCtrlHandler(0, true);
        }

        try
        {
            await using var terminal = await StartEchoAsync("ignored");
            lock (WindowsPtyConnection.s_processCreationLock)
            {
                (WindowsPtyConnection.ConsoleCtrlC.TryReadConsoleFlags()!.Value & 1).ShouldBe(1);
            }

            await terminal.TypeAsync("\u0003", TestContext.Current.CancellationToken);
            (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(42, terminal.DumpDiagnostics());
        }
        finally
        {
            lock (WindowsPtyConnection.s_processCreationLock)
            {
                WindowsNative.SetConsoleCtrlHandler(0, originallyIgnored);
            }
        }
    }

    [Fact]
    public async Task WaitUntilAsync_FailsFast_WhenTheOutputEnds()
    {
        await using var terminal = await StartAppAsync("args", "done");
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => terminal.WaitForTextAsync("never shown", TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        exception.Message.ShouldContain("args [done]");
        exception.Message.ShouldContain("exited with code 0");
    }

    [Fact]
    public async Task WaitUntilAsync_TimesOut_WithDiagnostics()
    {
        await using var terminal = await StartEchoAsync("timeout");

        var exception = await Should.ThrowAsync<TerminalTimeoutException>(
            () => terminal.WaitForTextAsync("never shown", TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));

        exception.Diagnostics.ShouldContain("[timeout] ready");
        exception.Diagnostics.ShouldContain("Raw output tail:");
        exception.Message.ShouldContain("text 'never shown'");
    }

    [Fact]
    public async Task WaitForOutputIdleAsync_ReturnsOnceOutputIsQuiet()
    {
        await using var terminal = await StartEchoAsync("idle");

        var frame = await terminal.WaitForOutputIdleAsync(TimeSpan.FromMilliseconds(200), StartTimeout, TestContext.Current.CancellationToken);

        frame.ContainsText("[idle] ready").ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_KillsTheApp_AndRejectsFurtherInput()
    {
        var terminal = await StartEchoAsync("dispose");
        var processId = terminal.ProcessId;

        await terminal.DisposeAsync();

        terminal.HasExited.ShouldBeTrue();
        await Should.ThrowAsync<ObjectDisposedException>(() => terminal.TypeAsync("x", TestContext.Current.CancellationToken));
        Should.Throw<ObjectDisposedException>(() => terminal.Resize(10, 10));
        IsRunning(processId).ShouldBeFalse();
        await terminal.DisposeAsync();
    }

    [Fact]
    public async Task ParallelTerminals_EachSeeOnlyTheirOwnInputOutputAndExitCode()
    {
        const int count = 8;
        var tokens = Enumerable.Range(0, count).Select(i => $"tok{i}x{Guid.NewGuid():N}"[..12]).ToArray();
        var terminals = await Task.WhenAll(Enumerable.Range(0, count).Select(i => StartEchoAsync($"t{i}")));
        try
        {
            terminals.Select(t => t.ProcessId).Distinct().Count().ShouldBe(count);

            await Task.WhenAll(Enumerable.Range(0, count).Select(async i =>
            {
                var terminal = terminals[i];
                await terminal.TypeAsync(tokens[i] + "\r", TestContext.Current.CancellationToken);
                await terminal.WaitForTextAsync($"[t{i}] got {tokens[i]}", StartTimeout, TestContext.Current.CancellationToken);
                await terminal.TypeAsync($"exit {10 + i}\r", TestContext.Current.CancellationToken);
                (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(10 + i, terminal.DumpDiagnostics());
            }));

            for (var i = 0; i < count; i++)
            {
                var raw = terminals[i].RawOutput;
                var screens = string.Concat(terminals[i].Frames.Select(frame => frame.ScreenText));
                terminals[i].ExitCode.ShouldBe(10 + i);
                for (var j = 0; j < count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    raw.ShouldNotContain(tokens[j]);
                    raw.ShouldNotContain($"[t{j}]");
                    screens.ShouldNotContain(tokens[j]);
                }
            }
        }
        finally
        {
            await Task.WhenAll(terminals.Select(terminal => terminal.DisposeAsync().AsTask()));
        }
    }

    [Fact]
    public async Task CtrlC_InOneTerminal_DoesNotAffectTheOthers()
    {
        var terminals = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => StartEchoAsync($"c{i}")));
        try
        {
            await terminals[0].TypeAsync("\u0003", TestContext.Current.CancellationToken);
            (await terminals[0].WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(42);

            await Task.WhenAll(terminals.Skip(1).Select(async (terminal, index) =>
            {
                var id = $"c{index + 1}";
                terminal.HasExited.ShouldBeFalse();
                terminal.CurrentFrame.ContainsText("interrupted").ShouldBeFalse();
                await terminal.TypeAsync("still here\r", TestContext.Current.CancellationToken);
                await terminal.WaitForTextAsync($"[{id}] got still here", StartTimeout, TestContext.Current.CancellationToken);
                await terminal.TypeAsync("exit 3\r", TestContext.Current.CancellationToken);
                (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(3);
            }));
        }
        finally
        {
            await Task.WhenAll(terminals.Select(terminal => terminal.DisposeAsync().AsTask()));
        }
    }

    [Fact]
    public async Task DisposingOneTerminal_DoesNotAffectTheOthers()
    {
        var terminals = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => StartEchoAsync($"d{i}")));
        try
        {
            var disposed = terminals[0].DisposeAsync().AsTask();
            await Task.WhenAll(terminals.Skip(1).Select(async (terminal, index) =>
            {
                var id = $"d{index + 1}";
                for (var round = 0; round < 3; round++)
                {
                    await terminal.TypeAsync($"round {round}\r", TestContext.Current.CancellationToken);
                    await terminal.WaitForTextAsync($"[{id}] got round {round}", StartTimeout, TestContext.Current.CancellationToken);
                }
            }));
            await disposed;

            terminals[0].HasExited.ShouldBeTrue();
            foreach (var terminal in terminals.Skip(1))
            {
                terminal.HasExited.ShouldBeFalse();
            }
        }
        finally
        {
            await Task.WhenAll(terminals.Select(terminal => terminal.DisposeAsync().AsTask()));
        }
    }

    [Fact]
    public async Task ConcurrentInput_ToOneTerminal_IsSerialized()
    {
        const int lines = 60;
        await using var terminal = await StartAppAsync(120, 40, "serial");
        await terminal.WaitForTextAsync("serial ready", StartTimeout, TestContext.Current.CancellationToken);

        using var start = new ManualResetEventSlim();
        var writers = Enumerable.Range(0, lines).Select(n => Task.Run(
            async () =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                await terminal.TypeAsync($"L{n}:{new string((char)('a' + (n % 26)), 60)}\r", TestContext.Current.CancellationToken);
            },
            TestContext.Current.CancellationToken)).ToArray();
        start.Set();
        await Task.WhenAll(writers);
        await terminal.TypeAsync("end\r", TestContext.Current.CancellationToken);

        await terminal.WaitForTextAsync($"summary ok={lines} bad=0 distinct={lines}", StartTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExitCodes_AreReliable_WhileOtherChildProcessesStartAndExit()
    {
        const int count = 8;
        var terminals = await Task.WhenAll(Enumerable.Range(0, count).Select(i => StartEchoAsync($"r{i}")));
        try
        {
            using var stop = new CancellationTokenSource();
            var processExitCodes = new List<(int Expected, int Actual)>();
            var churn = Task.Run(
                async () =>
                {
                    for (var n = 0; !stop.IsCancellationRequested; n++)
                    {
                        var expected = n % 7;
                        using var process = Process.Start(OperatingSystem.IsWindows()
                            ? new ProcessStartInfo("cmd.exe", $"/c exit {expected}") { UseShellExecute = false, CreateNoWindow = true }
                            : new ProcessStartInfo("/bin/sh", ["-c", $"exit {expected}"]) { UseShellExecute = false })!;
                        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                        lock (processExitCodes)
                        {
                            processExitCodes.Add((expected, process.ExitCode));
                        }
                    }
                },
                TestContext.Current.CancellationToken);

            await Task.WhenAll(terminals.Select(async (terminal, i) =>
            {
                await terminal.TypeAsync($"exit {100 + i}\r", TestContext.Current.CancellationToken);
                (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(100 + i, terminal.DumpDiagnostics());
            }));

            // Keep churning while more terminals start and exit, from inside the same window.
            var more = await Task.WhenAll(Enumerable.Range(0, count).Select(i => StartAppAsync("args", $"m{i}")));
            try
            {
                foreach (var terminal in more)
                {
                    (await terminal.WaitForExitAsync(StartTimeout, TestContext.Current.CancellationToken)).ShouldBe(0, terminal.DumpDiagnostics());
                }
            }
            finally
            {
                await Task.WhenAll(more.Select(terminal => terminal.DisposeAsync().AsTask()));
            }

            await stop.CancelAsync();
            await churn;
            lock (processExitCodes)
            {
                processExitCodes.Count.ShouldBeGreaterThan(0);
                processExitCodes.ShouldAllBe(pair => pair.Expected == pair.Actual);
            }
        }
        finally
        {
            await Task.WhenAll(terminals.Select(terminal => terminal.DisposeAsync().AsTask()));
        }
    }

    private static string AppPath => Path.Combine(AppContext.BaseDirectory, "RazorConsole.Tests.PtyApp.dll");

    internal static Task<PtyTerminal> StartAppAsync(params string[] arguments) => StartAppAsync(80, 24, arguments);

    internal static Task<PtyTerminal> StartAppAsync(int width, int height, params string[] arguments)
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "Pseudoterminals are not supported here.");
        return PtyTerminal.StartDotnetAsync(AppPath, width, height, arguments, TestContext.Current.CancellationToken);
    }

    private static async Task<PtyTerminal> StartEchoAsync(string id)
    {
        var terminal = await StartAppAsync("echo", id);
        try
        {
            await terminal.WaitForTextAsync($"[{id}] ready", StartTimeout, TestContext.Current.CancellationToken);
            return terminal;
        }
        catch
        {
            await terminal.DisposeAsync();
            throw;
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
