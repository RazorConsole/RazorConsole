// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using RazorConsole.Cli.Host;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Testing;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// <c>razorconsole doctor</c> against a real session host in this process, each test with its own state directory.
/// </summary>
public sealed class DoctorTests : IAsyncDisposable
{
    private const int TestTimeoutMs = 120_000;

    private static readonly string[] CheckNames =
        ["os.pty", "os.shell", "dotnet", "state.dir", "host.start", "host.socket", "pty.smoke", "apps.stale"];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rc-doc-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<InProcessLauncher> _launchers = [];

    private HostFiles Files => new(_directory);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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
    public async Task Doctor_PassesOnAHealthyMachine_AndStopsTheHostItStarted()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "PTY is not supported on this OS.");
        var connector = Connector(out var launcher);
        var operations = new HostOperations(connector);
        var options = new CliGlobalOptions(false, null, TimeSpan.FromSeconds(20));
        try
        {
            (await HostRunningAsync(connector)).ShouldBeFalse();

            var result = await operations.ExecuteAsync(new DoctorRequest(false), options, Token);
            result.Error.ShouldBeNull(result.Error?.Message + "\n" + result.Text);
            var checks = Checks(result.Data!.Value);
            checks.Select(static check => check.Name).ShouldBe(CheckNames);
            result.Data!.Value.EnumerateObject().Select(static property => property.Name).ShouldBe(["checks"]);
            checks.Where(static check => check.Status is "fail").ShouldBeEmpty();
            Status(checks, "pty.smoke").ShouldBe("pass");
            Status(checks, "host.start").ShouldBe("pass");
            Status(checks, "host.socket").ShouldBe("pass");
            Detail(checks, "host.start").ShouldStartWith("started by doctor");
            result.Text.ShouldNotBeNull().ShouldContain("8 checks:");

            // The host doctor started is gone again, and so is the smoke session with it.
            (await HostRunningAsync(connector)).ShouldBeFalse();
            await Task.WhenAll(launcher.Runs).WaitAsync(TimeSpan.FromSeconds(15), Token);
            (await operations.ExecuteAsync(new ListRequest(), options, Token)).Data!.Value.GetProperty("sessions").GetArrayLength().ShouldBe(0);
        }
        finally
        {
            await StopHostAsync(operations, options);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Doctor_NeverStopsAHostThatWasAlreadyRunning()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "PTY is not supported on this OS.");
        var connector = Connector(out _);
        var operations = new HostOperations(connector);
        var options = new CliGlobalOptions(false, null, TimeSpan.FromSeconds(20));
        try
        {
            await (await connector.ConnectOrStartAsync(Token)).DisposeAsync();
            (await HostRunningAsync(connector)).ShouldBeTrue();

            var result = await operations.ExecuteAsync(new DoctorRequest(false), options, Token);
            result.Error.ShouldBeNull(result.Error?.Message + "\n" + result.Text);
            Detail(Checks(result.Data!.Value), "host.start").ShouldStartWith("already running");
            Status(Checks(result.Data!.Value), "pty.smoke").ShouldBe("pass");

            // Doctor is done and the host it found is still there.
            (await HostRunningAsync(connector)).ShouldBeTrue();
        }
        finally
        {
            await StopHostAsync(operations, options);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task StaleApps_AreReported_AndRemovedWithFix_ButLiveOnesStay()
    {
        var operations = Operations(out _);
        var options = new CliGlobalOptions(false, null, TimeSpan.FromSeconds(20));
        var apps = DiagnosticsPaths.AppsDirectory(_directory);
        Directory.CreateDirectory(apps);

        using var self = Process.GetCurrentProcess();
        var live = Path.Combine(apps, $"{Environment.ProcessId}.json");
        var reusedPid = Path.Combine(apps, "4000000.json");
        var corrupt = Path.Combine(apps, "4000001.json");
        WriteDiscovery(live, self.StartTime.ToUniversalTime());
        WriteDiscovery(reusedPid, self.StartTime.ToUniversalTime().AddDays(-30));
        File.WriteAllText(corrupt, "{ not json");
        try
        {
            var found = Checks((await operations.ExecuteAsync(new DoctorRequest(false), options, Token)).Data!.Value);
            Status(found, "apps.stale").ShouldBe("warn");
            Detail(found, "apps.stale").ShouldContain("4000000.json");
            Detail(found, "apps.stale").ShouldContain("4000001.json");
            Detail(found, "apps.stale").ShouldContain("--fix");
            File.Exists(reusedPid).ShouldBeTrue();

            var fixedUp = Checks((await operations.ExecuteAsync(new DoctorRequest(true), options, Token)).Data!.Value);
            Status(fixedUp, "apps.stale").ShouldBe("pass");
            Detail(fixedUp, "apps.stale").ShouldStartWith("removed 2");
            File.Exists(reusedPid).ShouldBeFalse();
            File.Exists(corrupt).ShouldBeFalse();
            File.Exists(live).ShouldBeTrue();

            var clean = Checks((await operations.ExecuteAsync(new DoctorRequest(false), options, Token)).Data!.Value);
            Detail(clean, "apps.stale").ShouldContain("1 live app(s)");
        }
        finally
        {
            await StopHostAsync(operations, options);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task APtyThatIsNotSupported_FailsOsPty_SkipsTheSmokeTest_AndFailsTheCommand()
    {
        var connector = Connector(out _);
        var probes = DoctorProbes.Default with { PtySupported = static () => false };
        try
        {
            var result = await new Doctor(connector, probes).RunAsync(false, Token);
            Status(result.Checks, "os.pty").ShouldBe("fail");
            Status(result.Checks, "pty.smoke").ShouldBe("skip");
            Detail(result.Checks, "pty.smoke").ShouldBe("needs os.pty");
            result.Failed.Select(static check => check.Name).ShouldBe(["os.pty"]);

            // A failed check does not leave the host doctor started running.
            Detail(result.Checks, "host.start").ShouldStartWith("started by doctor");
            (await HostRunningAsync(connector)).ShouldBeFalse();
        }
        finally
        {
            await StopHostAsync(connector);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ADotnetHostThatDoesNotExist_FailsTheDotnetCheck_AndTheSmokeTest()
    {
        var connector = Connector(out _);
        var probes = DoctorProbes.Default with { DotnetHost = () => Path.Combine(_directory, "no-such-dotnet") };
        try
        {
            var result = await new Doctor(connector, probes).RunAsync(false, Token);
            Status(result.Checks, "dotnet").ShouldBe("fail");
            Detail(result.Checks, "dotnet").ShouldContain("not found");
            Status(result.Checks, "pty.smoke").ShouldBe("skip");
        }
        finally
        {
            await StopHostAsync(connector);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OperationsReport_ChecksFailed_WithTheChecklist_WhenACheckFails()
    {
        // A host that cannot start: nothing launches it, so host.start fails after its short timeout.
        var files = Files;
        var connector = new HostConnector(files, new NoLauncher(), "1.0.0") { StartTimeout = TimeSpan.FromMilliseconds(300) };
        var result = await new HostOperations(connector).ExecuteAsync(new DoctorRequest(false), new CliGlobalOptions(false, null, TimeSpan.FromSeconds(20)), Token);

        var error = result.Error.ShouldNotBeNull();
        error.Code.ShouldBe(DiagnosticsErrorCodes.ChecksFailed);
        CliExitCodes.For(error.Code).ShouldBe(CliExitCodes.AssertionFailed);
        error.Message.ShouldContain("host.start");
        error.Data.ShouldBeNull();
        result.Text.ShouldNotBeNull().ShouldContain("fail  host.start");
        Status(Checks(result.Data!.Value), "host.socket").ShouldBe("skip");
        Status(Checks(result.Data!.Value), "pty.smoke").ShouldBe("skip");

        // In JSON the checks are at the top level, as on success, with the error next to them.
        using var document = JsonDocument.Parse(CliOutput.ToJson(result));
        document.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe("checks_failed");
        Status(Checks(document.RootElement), "host.start").ShouldBe("fail");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task AHostThatCannotBeStarted_IsAHostProblemInTheChecklist_NotAnInternalError()
    {
        var files = Files;
        var connector = new HostConnector(files, new ThrowingLauncher(new System.ComponentModel.Win32Exception(267, "The directory name is invalid.")), "1.0.0");

        // On its own the connector turns it into host_unavailable (exit 8)...
        var thrown = await Should.ThrowAsync<DiagnosticsProtocolException>(() => connector.ConnectOrStartAsync(Token));
        thrown.Code.ShouldBe(DiagnosticsErrorCodes.HostUnavailable);
        thrown.Message.ShouldContain("The directory name is invalid.");
        CliExitCodes.For(thrown.Code).ShouldBe(CliExitCodes.HostUnavailable);

        // ...and doctor reports the failed check and still runs the others.
        var result = await new HostOperations(connector).ExecuteAsync(new DoctorRequest(false), new CliGlobalOptions(false, null, TimeSpan.FromSeconds(20)), Token);
        result.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.ChecksFailed);
        var checks = Checks(result.Data!.Value);
        Status(checks, "host.start").ShouldBe("fail");
        Detail(checks, "host.start").ShouldContain("The directory name is invalid.");
        Status(checks, "host.socket").ShouldBe("skip");
        Status(checks, "pty.smoke").ShouldBe("skip");
    }

    [Fact(Timeout = TestTimeoutMs)]
    [UnsupportedOSPlatform("windows")]
    public async Task OnUnix_AStateDirectoryThatOthersCanRead_Fails()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes are a Unix concept.");
        Directory.CreateDirectory(_directory);
        File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        var connector = Connector(out _);
        try
        {
            var result = await new Doctor(connector, DoctorProbes.Default).RunAsync(false, Token);
            Status(result.Checks, "state.dir").ShouldBe("fail");
            Detail(result.Checks, "state.dir").ShouldContain("0750");
            Detail(result.Checks, "state.dir").ShouldContain("chmod 700");
        }
        finally
        {
            await StopHostAsync(connector);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OnUnix_ATooLongStateDirectory_FailsWithTheByteCount()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Socket path limits are a Unix concept.");
        var longDirectory = Path.Combine(_directory, new string('a', 120));
        var connector = new HostConnector(new HostFiles(longDirectory), new NoLauncher(), "1.0.0") { StartTimeout = TimeSpan.FromMilliseconds(300) };

        var result = await new Doctor(connector, DoctorProbes.Default).RunAsync(false, Token);

        Status(result.Checks, "state.dir").ShouldBe("fail");
        Detail(result.Checks, "state.dir").ShouldContain("bytes of path");
        Detail(result.Checks, "state.dir").ShouldContain("RAZORCONSOLE_CLI_HOME");
    }

    [Fact(Timeout = TestTimeoutMs)]
    [SupportedOSPlatform("windows")]
    public async Task OnWindows_TheHostPipe_IsReachableOnlyByTheCurrentUser()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named pipes are Windows only.");
        var connector = Connector(out _);
        try
        {
            var result = await new Doctor(connector, DoctorProbes.Default).RunAsync(false, Token);
            Status(result.Checks, "host.socket").ShouldBe("pass");
            Detail(result.Checks, "host.socket").ShouldContain(System.Security.Principal.WindowsIdentity.GetCurrent().Name);
        }
        finally
        {
            await StopHostAsync(connector);
        }
    }

    private static IReadOnlyList<DoctorCheck> Checks(JsonElement data)
        => [.. data.GetProperty("checks").EnumerateArray().Select(static check => new DoctorCheck(
            check.GetProperty("name").GetString()!,
            check.GetProperty("status").GetString()!,
            check.GetProperty("detail").GetString()!))];

    private static string Status(IReadOnlyList<DoctorCheck> checks, string name) => checks.Single(check => check.Name == name).Status;

    private static string Detail(IReadOnlyList<DoctorCheck> checks, string name) => checks.Single(check => check.Name == name).Detail;

    private static async Task<bool> HostRunningAsync(HostConnector connector)
    {
        var client = await connector.ConnectExistingAsync(Token);
        if (client is null)
        {
            return false;
        }

        await client.DisposeAsync();
        return true;
    }

    private static void WriteDiscovery(string path, DateTimeOffset startTime)
    {
        var info = new DiagnosticsDiscoveryInfo(Environment.ProcessId, startTime, DiagnosticsProtocol.Version, "unused", "TestApp", "1.0.0");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(info, DiagnosticsJsonContext.Default.DiagnosticsDiscoveryInfo));
    }

    // Every test ends here, so no host lingers for its idle timeout.
    private static async Task StopHostAsync(HostOperations operations, CliGlobalOptions options)
        => (await operations.ExecuteAsync(new HostStopRequest(), options, CancellationToken.None)).Error.ShouldBeNull();

    private static async Task StopHostAsync(HostConnector connector)
        => await new HostOperations(connector).ExecuteAsync(new HostStopRequest(), new CliGlobalOptions(false, null, TimeSpan.FromSeconds(20)), CancellationToken.None);

    private HostConnector Connector(out InProcessLauncher launcher)
    {
        launcher = new InProcessLauncher(TimeSpan.FromMinutes(10), "1.0.0");
        _launchers.Add(launcher);
        return new HostConnector(Files, launcher, "1.0.0");
    }

    private HostOperations Operations(out InProcessLauncher launcher) => new(Connector(out launcher));

    private sealed class ThrowingLauncher(Exception exception) : IHostLauncher
    {
        public void Launch(HostFiles files) => throw exception;
    }

    private sealed class NoLauncher : IHostLauncher
    {
        public void Launch(HostFiles files)
        {
        }
    }
}
