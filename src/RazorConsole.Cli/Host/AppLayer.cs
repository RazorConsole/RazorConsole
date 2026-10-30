// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Host;

/// <summary>
/// A session's connection to its app's diagnostics endpoint: finds the app, connects when it can, and tells why it
/// cannot.
/// </summary>
/// <remarks>
/// <para>
/// A PTY session pairs by token (the app writes it into its discovery file), so apps started through
/// <c>dotnet run</c> pair too. The app registers its endpoint some time after the process starts, so a call made while
/// the session is <c>not_connected_yet</c> waits for the connection within its own timeout instead of failing at once.
/// </para>
/// <para>
/// The wait ends early with <c>disabled</c> once the app has drawn its first frame and
/// <see cref="Grace"/> has passed without a discovery file. The tool cannot know why an app has none, so the message
/// lists the possible causes.
/// </para>
/// </remarks>
internal sealed class AppLayer : IAsyncDisposable
{
    /// <summary>
    /// How long after the first frame an app may still register its endpoint.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StatsTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinimumCall = TimeSpan.FromSeconds(1);

    private readonly Func<DiagnosticsDiscoveryInfo?> _locate;
    private readonly Func<bool> _graceElapsed;
    private readonly Func<DiagnosticsProtocolException?> _exited;
    private readonly SemaphoreSlim _connect = new(1, 1);
    private volatile AppLink? _link;
    private volatile string? _pipeline;
    private volatile string? _incompatible;

    /// <param name="locate">Finds the app's discovery file, or <see langword="null"/> when there is none.</param>
    /// <param name="graceElapsed">Whether the app has had its chance to register: for a PTY, it drew a frame
    /// <see cref="Grace"/> ago. Attached apps always have.</param>
    /// <param name="exited">The error to report when the app's process is gone, otherwise <see langword="null"/>.</param>
    public AppLayer(Func<DiagnosticsDiscoveryInfo?> locate, Func<bool> graceElapsed, Func<DiagnosticsProtocolException?> exited)
    {
        _locate = locate;
        _graceElapsed = graceElapsed;
        _exited = exited;
    }

    /// <summary>
    /// The connection's state right now, without trying to connect.
    /// </summary>
    public AppLayerInfo Describe()
    {
        if (_link is { IsBroken: false } link)
        {
            return new AppLayerInfo(true, link.Hello.ProcessId, link.Hello.Server, _pipeline);
        }

        var reason = _incompatible is not null
            ? DiagnosticsUnavailableReasons.IncompatibleProtocol
            : _graceElapsed() ? DiagnosticsUnavailableReasons.Disabled : DiagnosticsUnavailableReasons.NotConnectedYet;
        return new AppLayerInfo(false, Reason: reason);
    }

    /// <summary>
    /// Tries once to connect, for <c>list</c>, and returns the state.
    /// </summary>
    public async Task<AppLayerInfo> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TryOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DiagnosticsProtocolException)
        {
            // Reported through Describe.
        }

        return Describe();
    }

    /// <summary>
    /// Drops the connection, for a session whose app was restarted.
    /// </summary>
    public async Task ResetAsync()
    {
        await _connect.WaitAsync().ConfigureAwait(false);
        try
        {
            var link = _link;
            _link = null;
            _pipeline = null;
            _incompatible = null;
            if (link is not null)
            {
                await link.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _connect.Release();
        }
    }

    /// <summary>
    /// Returns a live connection, waiting up to <paramref name="timeout"/> for the app to register while the session is
    /// <c>not_connected_yet</c>.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException"><see cref="DiagnosticsErrorCodes.AppLayerUnavailable"/> with the
    /// reason in its data, or <see cref="DiagnosticsErrorCodes.ProcessExited"/>.</exception>
    public async Task<AppLink> GetLinkAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            var (link, found) = await TryOnceAsync(cancellationToken).ConfigureAwait(false);
            if (link is not null)
            {
                return link;
            }

            if (_exited() is { } exited)
            {
                throw exited;
            }

            if (!found && _graceElapsed())
            {
                throw AppLink.Unavailable(
                    DiagnosticsUnavailableReasons.Disabled,
                    "The app has not registered a diagnostics endpoint. Possible causes: it is not a RazorConsole app; it is a "
                    + "Release or Production build (diagnostics run in Development or Debug builds); or diagnostics are turned "
                    + "off (ConsoleAppOptions.Diagnostics, or RAZORCONSOLE_DIAGNOSTICS=0).");
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed >= timeout)
            {
                throw AppLink.Unavailable(
                    DiagnosticsUnavailableReasons.NotConnectedYet,
                    found
                        ? $"The app's diagnostics endpoint did not accept a connection within {timeout.TotalSeconds:0.###}s (it serves a few connections at a time)."
                        : $"The app has not registered its diagnostics endpoint yet (waited {timeout.TotalSeconds:0.###}s). It may still be starting; try a longer --timeout.");
            }

            await Task.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Calls an endpoint method; the connection wait and the call share <paramref name="timeout"/>.
    /// </summary>
    public async Task<JsonElement> CallAsync(string method, JsonElement? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var link = await GetLinkAsync(timeout, cancellationToken).ConfigureAwait(false);
        var remaining = timeout - Stopwatch.GetElapsedTime(started);
        return await link.CallAsync(method, parameters, remaining < MinimumCall ? MinimumCall : remaining, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult> CallAsync<TParams, TResult>(
        string method,
        TParams parameters,
        JsonTypeInfo<TParams> parametersType,
        JsonTypeInfo<TResult> resultType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await CallAsync(method, JsonSerializer.SerializeToElement(parameters, parametersType), timeout, cancellationToken).ConfigureAwait(false);
        return Deserialize(result, resultType, method);
    }

    public Task<TResult> CallAsync<TResult>(string method, JsonTypeInfo<TResult> resultType, TimeSpan timeout, CancellationToken cancellationToken)
        => CallWithoutParamsAsync(method, resultType, timeout, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await ResetAsync().ConfigureAwait(false);
    }

    internal static TResult Deserialize<TResult>(JsonElement result, JsonTypeInfo<TResult> resultType, string method)
    {
        try
        {
            return result.Deserialize(resultType)
                ?? throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.Internal, $"The app answered {method} without a result.");
        }
        catch (JsonException ex)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.Internal, $"The app's answer to {method} could not be read: {ex.Message}", ex);
        }
    }

    private async Task<TResult> CallWithoutParamsAsync<TResult>(string method, JsonTypeInfo<TResult> resultType, TimeSpan timeout, CancellationToken cancellationToken)
        => Deserialize(await CallAsync(method, null, timeout, cancellationToken).ConfigureAwait(false), resultType, method);

    // One attempt to get a connection. found: whether a discovery file for the app exists.
    private async Task<(AppLink? Link, bool Found)> TryOnceAsync(CancellationToken cancellationToken)
    {
        await _connect.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_link is { IsBroken: false } existing)
            {
                return (existing, true);
            }

            if (_link is { } broken)
            {
                _link = null;
                await broken.DisposeAsync().ConfigureAwait(false);
            }

            if (_locate() is not { } info)
            {
                return (null, false);
            }

            AppLink? link;
            try
            {
                link = await AppLink.TryConnectAsync(info, HostFiles.ToolVersion, cancellationToken).ConfigureAwait(false);
            }
            catch (DiagnosticsProtocolException ex) when (ex.Code == DiagnosticsErrorCodes.AppLayerUnavailable)
            {
                _incompatible = ex.Message;
                throw;
            }

            if (link is null)
            {
                return (null, true);
            }

            _incompatible = null;
            _link = link;
            _pipeline = await ReadPipelineAsync(link, cancellationToken).ConfigureAwait(false);
            return (link, true);
        }
        finally
        {
            _connect.Release();
        }
    }

    private static async Task<string?> ReadPipelineAsync(AppLink link, CancellationToken cancellationToken)
    {
        try
        {
            var stats = await link.CallAsync(DiagnosticsAppMethods.Stats, null, StatsTimeout, cancellationToken).ConfigureAwait(false);
            return stats.Deserialize(DiagnosticsJsonContext.Default.StatsResult)?.Pipeline;
        }
        catch (Exception ex) when (ex is DiagnosticsProtocolException or JsonException)
        {
            return null;
        }
    }
}
