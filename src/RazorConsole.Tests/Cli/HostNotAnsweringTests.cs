// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using RazorConsole.Cli.Host;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// A host that holds its lock is alive, only slow to answer (busy, or still starting): session commands keep trying to
/// connect until their timeout, and only a missing host means there is no session.
/// </summary>
public sealed class HostNotAnsweringTests : IDisposable
{
    private const int TestTimeoutMs = 60_000;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rc-busy-" + Guid.NewGuid().ToString("N")[..8]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HostFiles Files => new(_directory);

    public void Dispose()
    {
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
    public async Task ASessionCommand_WhenTheHostHoldsItsLockAndNeverAnswers_FailsWithHostUnavailableAfterTheTimeout()
    {
        var files = Files;
        Directory.CreateDirectory(files.StateDirectory);
        using var hostLock = files.TryAcquireLock().ShouldNotBeNull();
        var operations = new HostOperations(new HostConnector(files, new InProcessLauncher(TimeSpan.FromMinutes(1), "1.0.0"), "1.0.0"));
        var options = new CliGlobalOptions(false, "s1", TimeSpan.FromSeconds(1));
        var watch = Stopwatch.StartNew();

        var result = await operations.ExecuteAsync(new TreeRequest(null, null, IncludeText: true, IncludeLayout: true), options, Token);

        var error = result.Error.ShouldNotBeNull();
        error.Code.ShouldBe(DiagnosticsErrorCodes.HostUnavailable);
        error.Message.ShouldContain("did not answer");
        CliExitCodes.For(error.Code).ShouldBe(CliExitCodes.HostUnavailable);

        // It kept trying for the whole timeout instead of giving up on the first failed probe.
        watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task TheConnector_WhenTheHostAnswersLateWithinTheTimeout_Connects()
    {
        var files = Files;
        Directory.CreateDirectory(files.StateDirectory);
        using var hostLock = files.TryAcquireLock().ShouldNotBeNull();
        var connector = new HostConnector(files, new InProcessLauncher(TimeSpan.FromMinutes(1), "1.0.0"), "1.0.0");
        using var done = new CancellationTokenSource();
        var host = AnswerHelloLateAsync(files, TimeSpan.FromMilliseconds(700), done.Token);
        var watch = Stopwatch.StartNew();

        try
        {
            var client = await connector.ConnectExistingAsync(TimeSpan.FromSeconds(30), Token);

            await using (client.ShouldNotBeNull())
            {
                client.HostVersion.ShouldBe("1.0.0");
            }

            watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(600));
        }
        finally
        {
            await done.CancelAsync();
            await host;
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ASessionCommand_WithoutAHost_IsSessionNotFound_AtOnce()
    {
        var files = Files;
        Directory.CreateDirectory(files.StateDirectory);
        var operations = new HostOperations(new HostConnector(files, new InProcessLauncher(TimeSpan.FromMinutes(1), "1.0.0"), "1.0.0"));
        var options = new CliGlobalOptions(false, "s1", TimeSpan.FromSeconds(30));
        var watch = Stopwatch.StartNew();

        var result = await operations.ExecuteAsync(new TreeRequest(null, null, IncludeText: true, IncludeLayout: true), options, Token);

        result.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.SessionNotFound);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
    }

    // Listens only after a delay, and then answers one hello like a host does.
    private static async Task AnswerHelloLateAsync(HostFiles files, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await using var listener = DiagnosticsTransport.Listen(files.Endpoint);
            var stream = await listener.AcceptAsync(cancellationToken);
            await using var connection = new DiagnosticsConnection(stream);
            var hello = await connection.ReadAsync(cancellationToken);
            var result = new HelloResult(DiagnosticsProtocol.Version, SessionHost.ServerPrefix + "1.0.0", Environment.ProcessId);
            await connection.WriteAsync(DiagnosticsMessage.Success(hello!.Id!.Value, result, DiagnosticsJsonContext.Default.HelloResult), cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
