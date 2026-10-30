// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RazorConsole.Testing;

namespace RazorConsole.Tests.E2E;

/// <summary>
/// The outcome of one <c>razorconsole</c> process.
/// </summary>
internal sealed record CliRun(IReadOnlyList<string> Arguments, int ExitCode, string Output, string Error)
{
    /// <summary>
    /// The one JSON line of a <c>--json</c> run.
    /// </summary>
    public JsonElement Json
    {
        get
        {
            using var document = JsonDocument.Parse(Output.Trim());
            return document.RootElement.Clone();
        }
    }

    /// <summary>
    /// The command, exit code and both output streams, for assertion messages.
    /// </summary>
    public string Describe()
        => $"razorconsole {string.Join(' ', Arguments)} -> exit {ExitCode}{Environment.NewLine}--- stdout ---{Environment.NewLine}{Output}{Environment.NewLine}--- stderr ---{Environment.NewLine}{Error}";
}

/// <summary>
/// A running <c>razorconsole</c> process whose output is read while it runs, for the streaming verbs.
/// </summary>
internal sealed class CliProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly IReadOnlyList<string> _arguments;
    private readonly Task<string> _error;
    private readonly StringBuilder _read = new();

    public CliProcess(Process process, IReadOnlyList<string> arguments)
    {
        _process = process;
        _arguments = arguments;
        _error = process.StandardError.ReadToEndAsync();
    }

    /// <summary>
    /// The next line of standard output, or <see langword="null"/> when the process ended.
    /// </summary>
    public async Task<string?> ReadLineAsync(TimeSpan timeout)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            var line = await _process.StandardOutput.ReadLineAsync(limit.Token);
            if (line is not null)
            {
                _read.AppendLine(line);
            }

            return line;
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No output within {timeout.TotalSeconds:0}s from 'razorconsole {string.Join(' ', _arguments)}'. Read so far:{Environment.NewLine}{_read}");
        }
    }

    /// <summary>
    /// Waits for the process to end and returns what it printed after the lines already read.
    /// </summary>
    public async Task<CliRun> WaitForExitAsync(TimeSpan timeout)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            var rest = await _process.StandardOutput.ReadToEndAsync(limit.Token);
            await _process.WaitForExitAsync(limit.Token);
            return new CliRun(_arguments, _process.ExitCode, rest, await _error);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"'razorconsole {string.Join(' ', _arguments)}' did not end within {timeout.TotalSeconds:0}s. Read so far:{Environment.NewLine}{_read}");
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Runs the real tool for one end-to-end test: <c>dotnet RazorConsole.Cli.dll …</c> as its own process, against its own
/// state directory, so the host it starts is a real background process too.
/// </summary>
/// <remarks>
/// Every test ends with <c>host stop</c>. <see cref="DisposeAsync"/> repeats it, and kills the host and anything
/// registered with <see cref="Track"/> if that did not work, so a failing test leaves nothing running.
/// </remarks>
internal sealed class CliE2E : IAsyncDisposable
{
    private static readonly TimeSpan CommandLimit = TimeSpan.FromSeconds(90);

    private readonly List<int> _tracked = [];

    private CliE2E()
    {
    }

    public static string PtyAppPath => Path.Combine(AppContext.BaseDirectory, "RazorConsole.Tests.PtyApp.dll");

    /// <summary>
    /// The tool as built for its own target frameworks, not the copy in the test output: that one sits next to a
    /// RazorConsole.Core built for the test's framework (net9.0, net11.0), which the tool's runtime cannot load.
    /// </summary>
    private static string CliPath { get; } = FindCli();

    private static string FindCli()
    {
        // artifacts/bin/RazorConsole.Tests/<configuration>_<tfm>/ next to artifacts/bin/RazorConsole.Cli/<configuration>_<tfm>/
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var configuration = testOutput.Name.Split('_')[0];
        var cliBin = Path.Combine(testOutput.Parent?.Parent?.FullName ?? string.Empty, "RazorConsole.Cli");
        foreach (var framework in new[] { "net10.0", "net8.0" })
        {
            var path = Path.Combine(cliBin, $"{configuration}_{framework}", "RazorConsole.Cli.dll");
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new InvalidOperationException($"The tool is not built under '{cliBin}'. Build the solution first.");
    }

    public string Home { get; } = Path.Combine(Path.GetTempPath(), "rc-e2e-" + Guid.NewGuid().ToString("N")[..8]);

    public static CliE2E Create()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "Pseudoterminals are not supported here.");
        var cli = new CliE2E();

        // The state directory exists before the first command. A first command on a machine without it fails today:
        // ProcessHostLauncher starts the host with the state directory as its working directory before anything has
        // created it ("The directory name is invalid"). That is reported with this step, not worked around in the tool.
        Directory.CreateDirectory(cli.Home);
        return cli;
    }

    /// <summary>
    /// Makes sure the process with this id is killed when the test is over, whatever the test did.
    /// </summary>
    public void Track(int processId) => _tracked.Add(processId);

    public Task<CliRun> RunAsync(params string[] arguments) => RunAsync(null, arguments);

    public Task<CliRun> RunAsync(IReadOnlyDictionary<string, string>? environment, params string[] arguments)
        => RunCoreAsync(arguments, environment, TestContext.Current.CancellationToken);

    /// <summary>
    /// Starts the tool and returns at once, to read its output while it runs.
    /// </summary>
    public CliProcess Start(params string[] arguments) => new(StartProcess(arguments, null), arguments);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await RunCoreAsync(["host", "stop"], null, CancellationToken.None);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
        {
            // The host is killed below.
        }

        KillHost();
        foreach (var pid in _tracked)
        {
            Kill(pid);
        }

        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Kill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private Process StartProcess(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo(PtyTerminal.ResolveDotnetHost())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add(CliPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["RAZORCONSOLE_CLI_HOME"] = Home;
        startInfo.Environment.Remove("RAZORCONSOLE_SESSION");
        startInfo.Environment.Remove("RAZORCONSOLE_DIAGNOSTICS");
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The tool did not start.");
        process.StandardInput.Close();
        return process;
    }

    private async Task<CliRun> RunCoreAsync(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken)
    {
        using var process = StartProcess(arguments, environment);
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(CommandLimit);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"'razorconsole {string.Join(' ', arguments)}' did not end within {CommandLimit.TotalSeconds:0}s.");
        }

        return new CliRun(arguments, process.ExitCode, await output, await error);
    }

    // The host the tool started is a separate process; host.json says which.
    private void KillHost()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Home, "host.json")));
            if (document.RootElement.TryGetProperty("pid", out var pid))
            {
                Kill(pid.GetInt32());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }
}
