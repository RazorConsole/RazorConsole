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
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WatchTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitCheckLimit = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ExitCheckInterval = TimeSpan.FromMilliseconds(50);

    // What a process_exited error carries of the app's last errors: the newest few, each trimmed.
    private const int RecentErrorCount = 5;
    private const int MaxErrorMessage = 1000;
    private const int MaxErrorStack = 1500;

    private readonly Func<DiagnosticsDiscoveryInfo?> _locate;
    private readonly Func<bool> _graceElapsed;
    private readonly Func<DiagnosticsProtocolException?> _exited;
    private readonly SemaphoreSlim _connect = new(1, 1);
    private volatile AppLink? _link;
    private volatile string? _pipeline;
    private volatile string? _incompatible;
    private readonly object _errorsSync = new();
    private readonly List<ErrorResult> _recentErrors = [];
    private long _errorsNext;
    private CancellationTokenSource? _watching;

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
    /// The last few errors the app reported before now, oldest first. They outlive the connection, so a session whose
    /// app died can still say what went wrong; <see cref="ResetAsync"/> forgets them.
    /// </summary>
    public IReadOnlyList<JsonElement> RecentErrors
    {
        get
        {
            lock (_errorsSync)
            {
                return [.. _recentErrors.Select(static error => JsonSerializer.SerializeToElement(error, DiagnosticsJsonContext.Default.ErrorResult))];
            }
        }
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
            StopWatching();
            lock (_errorsSync)
            {
                _recentErrors.Clear();
                _errorsNext = 0;
            }

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
        try
        {
            var result = await link.CallAsync(method, parameters, remaining < MinimumCall ? MinimumCall : remaining, cancellationToken).ConfigureAwait(false);
            if (method == DiagnosticsAppMethods.Errors)
            {
                RecordErrors(result);
            }

            return result;
        }
        catch (DiagnosticsProtocolException ex) when (ex.Code == DiagnosticsErrorCodes.AppLayerUnavailable && link.IsBroken)
        {
            // A connection that closed because the app died: the process's exit is noticed a moment later than the
            // closed pipe, so give it that moment before blaming the app layer.
            if (await WaitForExitAsync(cancellationToken).ConfigureAwait(false) is { } exited)
            {
                throw exited;
            }

            throw;
        }
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
            StartWatching(link);
            _pipeline = await ReadPipelineAsync(link, cancellationToken).ConfigureAwait(false);
            return (link, true);
        }
        finally
        {
            _connect.Release();
        }
    }

    private async Task<DiagnosticsProtocolException?> WaitForExitAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (_exited() is { } exited)
            {
                return exited;
            }

            if (Stopwatch.GetElapsedTime(started) >= ExitCheckLimit)
            {
                return null;
            }

            await Task.Delay(ExitCheckInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    // Reads the app's errors in the background, so the newest ones are known even when nobody asked for them before the
    // app died. It uses the connection the session already has, so it takes none of the endpoint's few connections.
    private void StartWatching(AppLink link)
    {
        StopWatching();
        var watching = new CancellationTokenSource();
        _watching = watching;
        _ = WatchErrorsAsync(link, watching.Token);
    }

    private void StopWatching()
    {
        var watching = _watching;
        _watching = null;
        if (watching is not null)
        {
            watching.Cancel();
            watching.Dispose();
        }
    }

    private async Task WatchErrorsAsync(AppLink link, CancellationToken cancellationToken)
    {
        try
        {
            while (!link.IsBroken)
            {
                await Task.Delay(WatchInterval, cancellationToken).ConfigureAwait(false);
                long since;
                lock (_errorsSync)
                {
                    since = _errorsNext;
                }

                var parameters = JsonSerializer.SerializeToElement(new ErrorsParams(since), DiagnosticsJsonContext.Default.ErrorsParams);
                RecordErrors(await link.CallAsync(DiagnosticsAppMethods.Errors, parameters, WatchTimeout, cancellationToken).ConfigureAwait(false));
            }
        }
        catch (Exception ex) when (ex is DiagnosticsProtocolException or OperationCanceledException or ObjectDisposedException or JsonException)
        {
            // The link is gone or the layer was reset; what was read stays.
        }
    }

    private void RecordErrors(JsonElement result)
    {
        ErrorsResult? errors;
        try
        {
            errors = result.Deserialize(DiagnosticsJsonContext.Default.ErrorsResult);
        }
        catch (JsonException)
        {
            return;
        }

        if (errors is null)
        {
            return;
        }

        lock (_errorsSync)
        {
            foreach (var error in errors.Errors)
            {
                if (_recentErrors.Exists(known => known.Seq == error.Seq))
                {
                    continue;
                }

                _recentErrors.Add(error with { Message = Trim(error.Message, MaxErrorMessage), Stack = Trim(error.Stack, MaxErrorStack) });
            }

            _recentErrors.Sort(static (left, right) => left.Seq.CompareTo(right.Seq));
            if (_recentErrors.Count > RecentErrorCount)
            {
                _recentErrors.RemoveRange(0, _recentErrors.Count - RecentErrorCount);
            }

            _errorsNext = Math.Max(_errorsNext, errors.Next);
        }
    }

    private static string Trim(string text, int limit) => text.Length <= limit ? text : text[..limit] + "...";

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
