// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Host;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// A state directory whose host socket path is over the platform limit (108 bytes on Linux, 104 on macOS) cannot hold a
/// host. Every command that needs the host says so as <c>host_unavailable</c>. Windows uses pipe names, so these run on
/// Linux and macOS only; <see cref="Diagnostics.DiagnosticsPathsTests"/> covers the length rule on every OS.
/// </summary>
public sealed class HostSocketPathTests : IDisposable
{
    private const int TestTimeoutMs = 60_000;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rc-sock-" + Guid.NewGuid().ToString("N")[..8]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string LongDirectory => Path.Combine(_root, new string('a', 120));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OnUnix_EveryCommandThatNeedsTheHost_FailsWithHostUnavailable_AndAnAdvice()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Socket path limits are a Unix concept.");
        var launcher = new CountingLauncher();
        var connector = new HostConnector(new HostFiles(LongDirectory), launcher, "1.0.0") { StartTimeout = TimeSpan.FromMilliseconds(300) };
        var operations = new HostOperations(connector);
        var options = new CliGlobalOptions(false, null, TimeSpan.FromSeconds(5));
        var start = new StartRequest(null, StartTargetKind.Exe, "app", [], StartRequest.DefaultWidth, StartRequest.DefaultHeight, new Dictionary<string, string>(), null, StartRequest.DefaultTerminalType, true);

        foreach (CliRequest request in new CliRequest[] { new ListRequest(), start, new HostStatusRequest(), new HostStopRequest() })
        {
            var result = await operations.ExecuteAsync(request, options, Token);

            var error = result.Error.ShouldNotBeNull(request.GetType().Name);
            error.Code.ShouldBe(DiagnosticsErrorCodes.HostUnavailable, request.GetType().Name);
            CliExitCodes.For(error.Code).ShouldBe(CliExitCodes.HostUnavailable);
            error.Message.ShouldContain("RAZORCONSOLE_CLI_HOME");
            error.Message.ShouldContain("limit is");
        }

        // Nothing was launched to fail in the background, and nothing was created.
        launcher.Launches.ShouldBe(0);
        Directory.Exists(LongDirectory).ShouldBeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OnUnix_TheConnector_AnswersHostUnavailable_WithoutWaitingForTheStartTimeout()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Socket path limits are a Unix concept.");
        var connector = new HostConnector(new HostFiles(LongDirectory), new CountingLauncher(), "1.0.0") { StartTimeout = TimeSpan.FromMinutes(5) };

        var started = await Should.ThrowAsync<DiagnosticsProtocolException>(() => connector.ConnectOrStartAsync(Token));
        var existing = await Should.ThrowAsync<DiagnosticsProtocolException>(() => connector.ConnectExistingAsync(Token));

        started.Code.ShouldBe(DiagnosticsErrorCodes.HostUnavailable);
        existing.Code.ShouldBe(DiagnosticsErrorCodes.HostUnavailable);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OnUnix_TheHostItself_DoesNotHideATooLongPathAsAnotherHostRunning()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Socket path limits are a Unix concept.");
        var host = new SessionHost(new HostFiles(LongDirectory), TimeSpan.FromMinutes(1), "1.0.0");

        await Should.ThrowAsync<PathTooLongException>(() => host.RunAsync(Token));
    }

    private sealed class CountingLauncher : IHostLauncher
    {
        public int Launches { get; private set; }

        public void Launch(HostFiles files) => Launches++;
    }
}
