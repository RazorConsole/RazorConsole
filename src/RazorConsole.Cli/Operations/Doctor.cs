// Copyright (c) RazorConsole. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RazorConsole.Cli.Host;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Testing;
using RazorConsole.Testing.Pty;

namespace RazorConsole.Cli.Operations;

/// <summary>
/// The outcomes of a doctor check. Plain strings, so they read the same in <c>--json</c> on every target framework.
/// </summary>
internal static class DoctorStatus
{
    public const string Pass = "pass";
    public const string Warn = "warn";
    public const string Fail = "fail";
    public const string Skip = "skip";
}

/// <param name="Name">The check, such as <c>os.pty</c>.</param>
/// <param name="Status">One of <see cref="DoctorStatus"/>.</param>
/// <param name="Detail">What was found, or what to do about it.</param>
internal sealed record DoctorCheck(string Name, string Status, string Detail);

/// <param name="Checks">Every check, in the order they ran.</param>
internal sealed record DoctorResult(IReadOnlyList<DoctorCheck> Checks)
{
    [JsonIgnore]
    public IReadOnlyList<DoctorCheck> Failed => [.. Checks.Where(static check => check.Status == DoctorStatus.Fail)];

    /// <summary>
    /// The checklist, one line per check and a summary.
    /// </summary>
    public string ToText()
    {
        var text = new StringBuilder();
        foreach (var check in Checks)
        {
            text.Append(CultureInfo.InvariantCulture, $"{check.Status,-4}  {check.Name,-11}  {check.Detail}").Append('\n');
        }

        var counts = new[] { DoctorStatus.Pass, DoctorStatus.Warn, DoctorStatus.Fail, DoctorStatus.Skip }
            .Select(status => (Status: status, Count: Checks.Count(check => check.Status == status)))
            .Where(static entry => entry.Count > 0)
            .Select(static entry => $"{entry.Count.ToString(CultureInfo.InvariantCulture)} {entry.Status}");
        text.Append(CultureInfo.InvariantCulture, $"{Checks.Count} checks: {string.Join(", ", counts)}");
        return text.ToString();
    }
}

/// <summary>
/// What doctor asks of the machine that a test cannot change: whether pseudoterminals exist and which dotnet to run.
/// </summary>
internal sealed record DoctorProbes(Func<bool> PtySupported, Func<string> DotnetHost)
{
    public static DoctorProbes Default { get; } = new(() => PtyTerminal.IsSupported, PtyTerminal.ResolveDotnetHost);
}

/// <summary>
/// <c>razorconsole doctor</c>: checks the operating system, dotnet, the state directory, the session host and a real
/// pseudoterminal session, in that order. A check whose prerequisite failed is skipped.
/// </summary>
/// <remarks>
/// Doctor starts the session host when none runs, as <c>start</c> does, and stops it again when it is done, also when a
/// check failed. A host that was already running is never stopped.
/// </remarks>
internal sealed partial class Doctor(HostConnector connector, DoctorProbes probes)
{
    private const int MaxMacSocketPath = 103;
    private const int MaxUnixSocketPath = 107;
    private const int LongestPid = 10;

    private static readonly TimeSpan DotnetTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SmokeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HostRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(10);

    private HostFiles Files => connector.Files;

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionPattern();

    public async Task<DoctorResult> RunAsync(bool fix, CancellationToken cancellationToken)
    {
        var checks = new List<DoctorCheck>();
        var pty = await RunCheckAsync("os.pty", () => Task.FromResult(CheckPty()), checks).ConfigureAwait(false);
        await RunCheckAsync("os.shell", () => Task.FromResult(CheckShell()), checks).ConfigureAwait(false);
        var dotnet = await RunCheckAsync("dotnet", () => CheckDotnetAsync(cancellationToken), checks).ConfigureAwait(false);
        await RunCheckAsync("state.dir", () => Task.FromResult(CheckStateDirectory()), checks).ConfigureAwait(false);

        HostClient? client = null;
        var startedByDoctor = false;
        try
        {
            var start = await CheckHostStartAsync(cancellationToken).ConfigureAwait(false);
            client = start.Client;
            startedByDoctor = start.Started;
            checks.Add(start.Check);

            if (client is null)
            {
                checks.Add(Skip("host.socket", "needs host.start"));
            }
            else
            {
                await RunCheckAsync("host.socket", () => CheckSocketAsync(cancellationToken), checks).ConfigureAwait(false);
            }

            if (client is null || pty.Status != DoctorStatus.Pass || dotnet.Status != DoctorStatus.Pass)
            {
                var missing = client is null ? "host.start" : pty.Status != DoctorStatus.Pass ? "os.pty" : "dotnet";
                checks.Add(Skip("pty.smoke", "needs " + missing));
            }
            else
            {
                await RunCheckAsync("pty.smoke", () => CheckSmokeAsync(client, cancellationToken), checks).ConfigureAwait(false);
            }
        }
        finally
        {
            if (client is not null)
            {
                // Only a host that doctor started is doctor's to stop, however the checks went.
                if (startedByDoctor)
                {
                    await StopStartedHostAsync(client).ConfigureAwait(false);
                }

                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        await RunCheckAsync("apps.stale", () => Task.FromResult(CheckStaleApps(fix)), checks).ConfigureAwait(false);
        return new DoctorResult(checks);
    }

    private static DoctorCheck Pass(string name, string detail) => new(name, DoctorStatus.Pass, detail);

    private static DoctorCheck Warn(string name, string detail) => new(name, DoctorStatus.Warn, detail);

    private static DoctorCheck Fail(string name, string detail) => new(name, DoctorStatus.Fail, detail);

    private static DoctorCheck Skip(string name, string detail) => new(name, DoctorStatus.Skip, detail);

    // A check never takes the command down: what it throws is its own failure. Cancellation still ends the run.
    private static async Task<DoctorCheck> RunCheckAsync(string name, Func<Task<DoctorCheck>> check, List<DoctorCheck> checks)
    {
        DoctorCheck result;
        try
        {
            result = await check().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = Fail(name, ex is DiagnosticsProtocolException protocol ? protocol.Message : $"{ex.GetType().Name}: {ex.Message}");
        }

        checks.Add(result);
        return result;
    }

    private static string FormatMode(UnixFileMode mode) => Convert.ToString((int)mode, 8).PadLeft(4, '0');

    private DoctorCheck CheckPty()
    {
        if (!probes.PtySupported())
        {
            return Fail("os.pty", PtyPlatform.CreateNotSupportedException().Message);
        }

        if (OperatingSystem.IsWindows())
        {
            return Pass("os.pty", "ConPTY, " + RuntimeInformation.OSDescription);
        }

        try
        {
            // The same device the spawn path opens; the handle is closed at once.
            using var handle = File.OpenHandle("/dev/ptmx", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("os.pty", $"/dev/ptmx cannot be opened: {ex.Message}");
        }

        return Pass("os.pty", "/dev/ptmx opens, " + RuntimeInformation.OSDescription);
    }

    private static DoctorCheck CheckShell()
    {
        // Only macOS starts programs through bash (see UnixPtyConnection); Linux spawns them directly.
        if (!OperatingSystem.IsMacOS())
        {
            return Skip("os.shell", "only needed on macOS");
        }

        var missing = new[] { BashLaunch.Bash, "/usr/bin/env" }.Where(static path => !File.Exists(path)).ToList();
        return missing.Count == 0
            ? Pass("os.shell", "/bin/bash and /usr/bin/env exist")
            : Fail("os.shell", "missing: " + string.Join(", ", missing));
    }

    private async Task<DoctorCheck> CheckDotnetAsync(CancellationToken cancellationToken)
    {
        var host = ResolveExecutable(probes.DotnetHost());
        if (host is null)
        {
            return Fail("dotnet", "the dotnet host was not found; put dotnet on PATH or set DOTNET_HOST_PATH");
        }

        var startInfo = new ProcessStartInfo(host, "--list-runtimes")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("The process did not start.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Fail("dotnet", $"{host} cannot run: {ex.Message}");
        }

        using (process)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(DotnetTimeout);
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            _ = process.StandardError.ReadToEndAsync(limit.Token);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return Fail("dotnet", $"{host} --list-runtimes did not finish within {DotnetTimeout.TotalSeconds:0}s");
            }

            if (process.ExitCode != 0)
            {
                return Fail("dotnet", $"{host} --list-runtimes exited with {process.ExitCode.ToString(CultureInfo.InvariantCulture)}");
            }

            var versions = (await output.ConfigureAwait(false))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(static parts => parts.Length >= 2 && parts[0] == "Microsoft.NETCore.App")
                .Select(static parts => parts[1])
                .ToList();
            return versions.Count == 0
                ? Fail("dotnet", $"{host} lists no Microsoft.NETCore.App runtime")
                : Pass("dotnet", $"{host}; Microsoft.NETCore.App {string.Join(", ", versions)}");
        }
    }

    // The full path of a program given as a path or a name on PATH, or null.
    private static string? ResolveExecutable(string name)
    {
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return File.Exists(name) ? Path.GetFullPath(name) : null;
        }

        var file = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, file);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }

        return null;
    }

    private DoctorCheck CheckStateDirectory()
    {
        var directory = Files.StateDirectory;
        var overridden = Environment.GetEnvironmentVariable(DiagnosticsPaths.HomeEnvironmentVariableName) is { Length: > 0 }
            ? $" (from {DiagnosticsPaths.HomeEnvironmentVariableName})"
            : string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            var limit = OperatingSystem.IsMacOS() ? MaxMacSocketPath : MaxUnixSocketPath;
            var longest = new[]
            {
                Files.Endpoint,
                DiagnosticsPaths.AppEndpoint(directory, int.MaxValue) + new string('0', LongestPid - int.MaxValue.ToString(CultureInfo.InvariantCulture).Length),
            }.Max(static path => Encoding.UTF8.GetByteCount(path));
            if (longest > limit)
            {
                return Fail(
                    "state.dir",
                    $"{directory}{overridden}: its sockets need {longest.ToString(CultureInfo.InvariantCulture)} bytes of path and the limit is {limit.ToString(CultureInfo.InvariantCulture)}; set {DiagnosticsPaths.HomeEnvironmentVariableName} to a shorter path");
            }
        }

        if (!Directory.Exists(directory))
        {
            return Pass("state.dir", $"{directory}{overridden}; the host creates it on first use");
        }

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(directory);
            var userOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            if (mode != userOnly)
            {
                return Fail("state.dir", $"{directory}{overridden} has mode {FormatMode(mode)}, not 0700; run chmod 700 '{directory}'");
            }
        }

        var probe = Path.Combine(directory, ".doctor-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("state.dir", $"{directory}{overridden} is not writable: {ex.Message}");
        }

        if (OperatingSystem.IsWindows() && overridden.Length > 0 && !IsUnderProfile(directory))
        {
            return Warn("state.dir", $"{directory}{overridden} is outside your user profile; anyone who can read it can see the session files");
        }

        return Pass("state.dir", directory + overridden);
    }

    private static bool IsUnderProfile(string directory)
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
        {
            if (root.Length > 0 && directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Stops the host doctor started and waits for it to let go of its lock, so the next command starts a clean one.
    // A session someone started on it in the meantime keeps it running.
    private async Task StopStartedHostAsync(HostClient client)
    {
        try
        {
            var status = await client.RequestAsync(
                HostMethods.Status,
                new EmptyParams(),
                HostJsonContext.Default.EmptyParams,
                HostJsonContext.Default.HostStatusResult,
                HostRequestTimeout,
                CancellationToken.None).ConfigureAwait(false);
            if (status.Sessions > 0)
            {
                return;
            }

            await client.RequestAsync(
                HostMethods.Shutdown,
                new EmptyParams(),
                HostJsonContext.Default.EmptyParams,
                HostJsonContext.Default.HostShutdownResult,
                HostRequestTimeout,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DiagnosticsProtocolException or IOException)
        {
            // The host is gone already or going; there is nothing left to stop.
            return;
        }

        var deadline = DateTimeOffset.UtcNow + HostStopTimeout;
        while (Files.IsLocked() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }
    }

    private async Task<(DoctorCheck Check, HostClient? Client, bool Started)> CheckHostStartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var existing = await connector.ConnectExistingAsync(cancellationToken).ConfigureAwait(false);
            var client = existing ?? await connector.ConnectOrStartAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var status = await client.RequestAsync(
                    HostMethods.Status,
                    new EmptyParams(),
                    HostJsonContext.Default.EmptyParams,
                    HostJsonContext.Default.HostStatusResult,
                    HostRequestTimeout,
                    cancellationToken).ConfigureAwait(false);
                var which = existing is null ? "started by doctor, which stops it when done" : "already running, left running";
                var detail = $"{which}: pid {status.Pid.ToString(CultureInfo.InvariantCulture)}, version {status.Version}, protocol {status.Protocol.ToString(CultureInfo.InvariantCulture)}";
                return (Pass("host.start", detail), client, existing is null);
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex) when (ex is DiagnosticsProtocolException or IOException)
        {
            return (Fail("host.start", ex.Message), null, false);
        }
    }

    private async Task<DoctorCheck> CheckSocketAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return await CheckPipeAsync(Files.Endpoint, cancellationToken).ConfigureAwait(false);
        }

        var userReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var mode = File.GetUnixFileMode(Files.Endpoint);
        return mode == userReadWrite
            ? Pass("host.socket", $"{Files.Endpoint} has mode 0600")
            : Fail("host.socket", $"{Files.Endpoint} has mode {FormatMode(mode)}, not 0600");
    }

    [SupportedOSPlatform("windows")]
    private static async Task<DoctorCheck> CheckPipeAsync(string endpoint, CancellationToken cancellationToken)
    {
        // The client pipe is opened CurrentUserOnly, which already fails unless the server is ours.
        var stream = await DiagnosticsTransport.ConnectAsync(endpoint, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User?.Value;
            List<string> others;
            try
            {
                others = [.. ((PipeStream)stream).GetAccessControl()
                    .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                    .Cast<PipeAccessRule>()
                    .Where(rule => rule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow && rule.IdentityReference.Value != user)
                    .Select(static rule => rule.IdentityReference.Value)
                    .Distinct(StringComparer.Ordinal)];
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                // A client handle may lack the right to read the pipe's DACL; the user-only open above still holds.
                return Pass("host.socket", $"pipe {endpoint} opened as {identity.Name} with CurrentUserOnly (its ACL cannot be read from a client)");
            }

            return others.Count == 0
                ? Pass("host.socket", $"pipe {endpoint} allows only {identity.Name}")
                : Fail("host.socket", $"pipe {endpoint} also allows {string.Join(", ", others)}");
        }
    }

    // A real session through the host: the same path as 'start', with a program that prints and exits.
    private async Task<DoctorCheck> CheckSmokeAsync(HostClient client, CancellationToken cancellationToken)
    {
        var host = ResolveExecutable(probes.DotnetHost())!;
        var json = HostJsonContext.Default;
        var parameters = new StartParams(
            null,
            StartTargetKind.Exe,
            host,
            ["--version"],
            20,
            5,
            new Dictionary<string, string>(),
            null,
            StartRequest.DefaultTerminalType,
            NoWait: true,
            (long)SmokeTimeout.TotalMilliseconds);
        var info = await client.RequestAsync(HostMethods.Start, parameters, json.StartParams, json.SessionInfo, HostRequestTimeout, cancellationToken).ConfigureAwait(false);
        try
        {
            WaitResult exited;
            try
            {
                var wait = new WaitParams(info.Name, new WaitSpec(WaitConditionKind.Exit, null, null), (long)SmokeTimeout.TotalMilliseconds);
                exited = await client.RequestAsync(HostMethods.Wait, wait, json.WaitParams, json.WaitResult, SmokeTimeout + HostRequestTimeout, cancellationToken, static _ => { }).ConfigureAwait(false);
            }
            catch (DiagnosticsProtocolException ex) when (ex.Code is DiagnosticsErrorCodes.Timeout or DiagnosticsErrorCodes.ProcessExited)
            {
                return Fail("pty.smoke", $"'dotnet --version' did not finish in a 20x5 terminal within {SmokeTimeout.TotalSeconds:0}s: {ex.Message}");
            }

            var screen = await client.RequestAsync(
                HostMethods.Screen,
                new PtyScreenParams(info.Name, false, false, null),
                json.PtyScreenParams,
                json.PtyScreenResult,
                HostRequestTimeout,
                cancellationToken).ConfigureAwait(false);
            var text = string.Join('\n', screen.Lines.Select(static line => line.TrimEnd())).Trim();
            if (exited.ExitCode is not 0)
            {
                return Fail("pty.smoke", $"'dotnet --version' exited with {exited.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "an unknown code"}; screen: {text}");
            }

            return VersionPattern().IsMatch(text)
                ? Pass("pty.smoke", $"'dotnet --version' printed {VersionPattern().Match(text).Value} and exited 0 in {exited.ElapsedMs.ToString(CultureInfo.InvariantCulture)} ms")
                : Fail("pty.smoke", $"'dotnet --version' exited 0 but its output did not reach the screen; screen: '{text}'");
        }
        finally
        {
            try
            {
                await client.RequestAsync(
                    HostMethods.Stop,
                    new StopParams(info.Name, 5000),
                    json.StopParams,
                    json.StopResult,
                    HostRequestTimeout,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DiagnosticsProtocolException or IOException)
            {
                // The session is gone already or the host is; either way there is nothing left to stop.
            }
        }
    }

    private DoctorCheck CheckStaleApps(bool fix)
    {
        var directory = DiagnosticsPaths.AppsDirectory(Files.StateDirectory);
        if (!Directory.Exists(directory))
        {
            return Pass("apps.stale", "no discovery files");
        }

        var stale = new List<string>();
        var livePids = new HashSet<int>();
        foreach (var path in Directory.GetFiles(directory, "*.json"))
        {
            if (AppPairing.IsStale(path, out var pid))
            {
                stale.Add(path);
            }
            else if (pid is { } live)
            {
                livePids.Add(live);
            }
        }

        // A socket outlives an app that crashed; it is stale when no live discovery file points at its process.
        foreach (var path in Directory.GetFiles(directory, "*.sock"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || !livePids.Contains(pid))
            {
                stale.Add(path);
            }
        }

        if (stale.Count == 0)
        {
            return Pass("apps.stale", $"{livePids.Count.ToString(CultureInfo.InvariantCulture)} live app(s), no stale discovery files");
        }

        var names = string.Join(", ", stale.Select(static path => Path.GetFileName(path)));
        if (!fix)
        {
            return Warn("apps.stale", $"{stale.Count.ToString(CultureInfo.InvariantCulture)} stale file(s): {names}; run 'razorconsole doctor --fix' to remove them");
        }

        foreach (var path in stale)
        {
            HostFiles.TryDelete(path);
        }

        return Pass("apps.stale", $"removed {stale.Count.ToString(CultureInfo.InvariantCulture)} stale file(s): {names}");
    }
}
