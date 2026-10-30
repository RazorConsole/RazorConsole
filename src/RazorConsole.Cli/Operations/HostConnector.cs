// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text.Json;
using RazorConsole.Cli.Host;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Operations;

/// <summary>
/// Finds the session host and, for commands that need one, starts it.
/// </summary>
/// <remarks>
/// Starting is safe to race: whoever finds <c>host.lock</c> free starts a host, every host but the one holding the
/// lock exits, and all clients connect to the survivor. A host of another tool version is replaced when it has no
/// sessions and this client is newer; otherwise the command fails with <c>host_version_mismatch</c>.
/// </remarks>
internal sealed class HostConnector(HostFiles files, IHostLauncher launcher, string? clientVersion = null)
{
    public static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Probe = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RelaunchAfter = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    public HostFiles Files { get; } = files;

    public string ClientVersion { get; } = clientVersion ?? HostFiles.ToolVersion;

    public TimeSpan StartTimeout { get; init; } = DefaultStartTimeout;

    /// <summary>
    /// Connects to a running host, or returns <see langword="null"/> when none runs. Never starts one.
    /// </summary>
    public async Task<HostClient?> ConnectExistingAsync(CancellationToken cancellationToken)
    {
        if (!Files.IsLocked())
        {
            return null;
        }

        return await HostClient.TryConnectAsync(Files.Endpoint, ClientVersion, Probe, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects to the host, starting it first when none runs.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException">
    /// <c>host_unavailable</c> when no host answered within <see cref="StartTimeout"/>, or
    /// <c>host_version_mismatch</c> when a host of another version holds sessions.
    /// </exception>
    public async Task<HostClient> ConnectOrStartAsync(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        DateTimeOffset? launchedAt = null;
        while (true)
        {
            var client = await HostClient.TryConnectAsync(Files.Endpoint, ClientVersion, Probe, cancellationToken).ConfigureAwait(false);
            if (client is not null)
            {
                if (string.Equals(client.HostVersion, ClientVersion, StringComparison.Ordinal))
                {
                    return client;
                }

                await ReplaceOlderHostAsync(client, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // A free lock means no host: whatever host.json says is stale. A held lock means a host is starting.
            var now = DateTimeOffset.UtcNow;
            if ((launchedAt is null || now - launchedAt >= RelaunchAfter) && !Files.IsLocked())
            {
                HostFiles.TryDelete(Files.InfoFile);
                Launch();
                launchedAt = now;
            }

            if (now - started >= StartTimeout)
            {
                throw new DiagnosticsProtocolException(
                    DiagnosticsErrorCodes.HostUnavailable,
                    $"The session host did not start within {StartTimeout.TotalSeconds:0.###}s. Run 'razorconsole doctor' to see why.");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    // A process that cannot be started is a host problem (exit 8), not an internal error.
    private void Launch()
    {
        try
        {
            launcher.Launch(Files);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.HostUnavailable,
                $"The session host could not be started: {ex.Message} Run 'razorconsole doctor' to see why.",
                ex);
        }
    }

    /// <summary>
    /// Compares tool versions: the numeric part first, then a release over any prerelease, then prerelease labels.
    /// </summary>
    internal static int CompareVersions(string left, string right)
    {
        var (leftNumbers, leftLabel) = Split(left);
        var (rightNumbers, rightLabel) = Split(right);
        var numbers = leftNumbers.CompareTo(rightNumbers);
        if (numbers != 0)
        {
            return numbers;
        }

        return (leftLabel, rightLabel) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => string.CompareOrdinal(leftLabel, rightLabel),
        };

        static (Version Numbers, string? Label) Split(string version)
        {
            var dash = version.IndexOf('-', StringComparison.Ordinal);
            var numbers = dash < 0 ? version : version[..dash];
            return (Version.TryParse(numbers, out var parsed) ? parsed : new Version(0, 0), dash < 0 ? null : version[(dash + 1)..]);
        }
    }

    private async Task ReplaceOlderHostAsync(HostClient client, CancellationToken cancellationToken)
    {
        await using (client.ConfigureAwait(false))
        {
            var status = await client.RequestAsync(
                HostMethods.Status,
                new EmptyParams(),
                HostJsonContext.Default.EmptyParams,
                HostJsonContext.Default.HostStatusResult,
                ShutdownTimeout,
                cancellationToken).ConfigureAwait(false);

            if (status.Sessions > 0 || CompareVersions(ClientVersion, client.HostVersion) <= 0)
            {
                var data = JsonSerializer.SerializeToElement(
                    new HostVersionMismatchData(client.HostVersion, ClientVersion, status.Sessions),
                    HostJsonContext.Default.HostVersionMismatchData);
                var sessions = status.Sessions.ToString(CultureInfo.InvariantCulture);
                throw new DiagnosticsProtocolException(
                    DiagnosticsErrorCodes.HostVersionMismatch,
                    $"The session host runs razorconsole {client.HostVersion} with {sessions} session(s), but this is {ClientVersion}. "
                        + "Stop it with 'razorconsole host stop' (this ends its sessions).",
                    errorData: data);
            }

            await client.RequestAsync(
                HostMethods.Shutdown,
                new EmptyParams(),
                HostJsonContext.Default.EmptyParams,
                HostJsonContext.Default.HostShutdownResult,
                ShutdownTimeout,
                cancellationToken).ConfigureAwait(false);
        }

        // Wait for the old host to let go of the lock, so the next round starts ours.
        var deadline = DateTimeOffset.UtcNow + ShutdownTimeout;
        while (Files.IsLocked() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
