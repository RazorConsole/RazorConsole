// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Host;

/// <summary>
/// The background process that owns PTY sessions, so they outlive the CLI invocation that started them. It also
/// holds attached sessions: connections to apps it did not start, which it never kills.
/// </summary>
/// <remarks>
/// <para>
/// One host runs per state directory: it holds <c>host.lock</c> for its whole life, and a host that cannot take the
/// lock exits at once, so any number of clients starting hosts at the same time end up with one. At start it kills the
/// processes a crashed host left behind (by pid and start time from the session ledger), listens on the user-only
/// endpoint and writes <c>host.json</c>.
/// </para>
/// <para>
/// Each request runs in its own task, so a long one never blocks others on the same connection. A <c>cancel</c>
/// notification or a closed connection cancels its requests. The host exits when it has had no sessions and no
/// clients for the idle timeout, on <c>host.shutdown</c>, or when its token is cancelled; it then kills every session
/// and removes <c>host.json</c> and the ledger.
/// </para>
/// </remarks>
internal sealed class SessionHost
{
    public const string ServerPrefix = "razorconsole/";

    private static readonly TimeSpan ShutdownLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ResponseLimit = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LockRetryLimit = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LockRetryInterval = TimeSpan.FromMilliseconds(25);

    private readonly HostFiles _files;
    private readonly TimeSpan _idleTimeout;
    private readonly string _version;
    private readonly Dictionary<string, HostSession> _sessions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _starting = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<Task> _connections = [];
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private int _clients;
    private long _idleSince = Stopwatch.GetTimestamp();

    public SessionHost(HostFiles files, TimeSpan idleTimeout, string? version = null)
    {
        _files = files;
        _idleTimeout = idleTimeout;
        _version = version ?? HostFiles.ToolVersion;
    }

    public string ServerName => ServerPrefix + _version;

    /// <summary>
    /// Runs the host until it is idle, shut down or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <returns><see langword="false"/> when another host already runs, so this one never started.</returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        DiagnosticsPaths.EnsureUserOnlyDirectory(_files.StateDirectory);
        using var hostLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        if (hostLock is null)
        {
            return false;
        }

        SweepOrphans();

        DiagnosticsListener listener;
        try
        {
            listener = DiagnosticsTransport.Listen(_files.Endpoint);
        }
        catch (IOException ex) when (ex is not PathTooLongException)
        {
            // A host from another state directory with the same endpoint, or one that just lost its lock.
            return false;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        try
        {
            WriteInfo(listener.Endpoint);
            var accepting = AcceptAsync(listener, stop.Token);
            var idle = WatchIdleAsync(stop.Token);
            await Task.WhenAny(accepting, idle, Task.Delay(Timeout.Infinite, stop.Token)).ConfigureAwait(false);
            await stop.CancelAsync().ConfigureAwait(false);
            await QuietlyAsync(accepting).ConfigureAwait(false);
            await QuietlyAsync(idle).ConfigureAwait(false);
        }
        finally
        {
            await listener.DisposeAsync().ConfigureAwait(false);
            Task[] connections;
            lock (_connections)
            {
                connections = [.. _connections];
            }

            await QuietlyAsync(Task.WhenAll(connections).WaitAsync(ShutdownLimit)).ConfigureAwait(false);
            await StopAllSessionsAsync().ConfigureAwait(false);
            HostFiles.TryDelete(_files.InfoFile);
        }

        return true;
    }

    // A client checks for a live host by opening the lock for a moment, so a free lock can look held briefly.
    private async Task<FileStream?> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(LockRetryLimit.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            if (_files.TryAcquireLock() is { } acquired)
            {
                return acquired;
            }

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                return null;
            }

            await Task.Delay(LockRetryInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks the host to stop; <see cref="RunAsync"/> then cleans up and returns.
    /// </summary>
    public void RequestShutdown() => _shutdown.Cancel();

    private static async Task QuietlyAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Shutting down: what failed no longer matters.
        }
    }

    private static DiagnosticsMessage Success<T>(long id, T result, JsonTypeInfo<T> typeInfo)
        => DiagnosticsMessage.Success(id, result, typeInfo);

    private static T Required<T>(DiagnosticsMessage message, JsonTypeInfo<T> typeInfo)
        => message.GetParams(typeInfo)
            ?? throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"{message.Method} needs parameters.");

    private static async Task WriteAsync(DiagnosticsConnection connection, long? id, DiagnosticsMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await connection.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (DiagnosticsProtocolException ex) when (ex.Code == DiagnosticsErrorCodes.MessageTooLarge && id is not null)
        {
            await connection.WriteAsync(DiagnosticsMessage.Failure(id, ex.ToError()), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task TryWriteAsync(DiagnosticsConnection connection, long? id, DiagnosticsMessage response)
    {
        // The request's own token may already be cancelled; give the answer a moment of its own.
        using var timeout = new CancellationTokenSource(ResponseLimit);
        try
        {
            await WriteAsync(connection, id, response, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or DiagnosticsProtocolException)
        {
            // The client is gone or not reading.
        }
    }

    internal static bool SameProcess(int pid, DateTimeOffset startTime, out Process? process)
    {
        process = null;
        try
        {
            process = Process.GetProcessById(pid);
            if ((process.StartTime.ToUniversalTime() - startTime.UtcDateTime).Duration() <= StartTimeTolerance)
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        process?.Dispose();
        process = null;
        return false;
    }

    // Kills what a crashed host left running. A pid whose start time differs was reused by an unrelated process.
    private void SweepOrphans()
    {
        DiagnosticsPaths.EnsureUserOnlyDirectory(_files.SessionsDirectory);
        foreach (var path in Directory.EnumerateFiles(_files.SessionsDirectory))
        {
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize(File.ReadAllBytes(path), HostJsonContext.Default.SessionLedgerEntry);
                    if (entry is not null && SameProcess(entry.Pid, entry.ProcessStartTime, out var process))
                    {
                        using (process)
                        {
                            process!.Kill(entireProcessTree: true);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                    or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Unreadable or already gone: the entry is removed either way.
                }
            }

            HostFiles.TryDelete(path);
        }
    }

    private void WriteInfo(string endpoint)
    {
        DateTimeOffset startTime;
        using (var process = Process.GetCurrentProcess())
        {
            startTime = process.StartTime.ToUniversalTime();
        }

        var info = new HostInfo(Environment.ProcessId, startTime, DiagnosticsProtocol.Version, _version, endpoint);
        HostFiles.WriteAtomically(_files.InfoFile, JsonSerializer.SerializeToUtf8Bytes(info, HostJsonContext.Default.HostInfo));
    }

    private async Task WatchIdleAsync(CancellationToken cancellationToken)
    {
        var period = TimeSpan.FromTicks(Math.Clamp(_idleTimeout.Ticks / 10, TimeSpan.FromMilliseconds(20).Ticks, TimeSpan.FromSeconds(1).Ticks));
        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (IsBusy())
            {
                Volatile.Write(ref _idleSince, Stopwatch.GetTimestamp());
            }
            else if (Stopwatch.GetElapsedTime(Volatile.Read(ref _idleSince)) >= _idleTimeout)
            {
                return;
            }
        }
    }

    private bool IsBusy()
    {
        if (Volatile.Read(ref _clients) > 0)
        {
            return true;
        }

        lock (_sessions)
        {
            return _sessions.Count > 0 || _starting.Count > 0;
        }
    }

    private async Task AcceptAsync(DiagnosticsListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                stream = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                // A client that vanished during the handshake.
                continue;
            }

            Interlocked.Increment(ref _clients);
            var connection = ServeAsync(stream, cancellationToken);
            lock (_connections)
            {
                _connections.RemoveAll(static task => task.IsCompleted);
                _connections.Add(connection);
            }
        }
    }

    private async Task ServeAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var requests = new ConcurrentDictionary<long, (CancellationTokenSource Cancellation, Task Task)>();
        try
        {
            await using var connection = new DiagnosticsConnection(stream);
            await ServeConnectionAsync(connection, requests, closed.Token).ConfigureAwait(false);

            // A closed connection cancels whatever it still waits for.
            await closed.CancelAsync().ConfigureAwait(false);
            await QuietlyAsync(Task.WhenAll(requests.Values.Select(static request => request.Task))).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Ends this connection only; requests see the cancellation.
            await closed.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _idleSince, Stopwatch.GetTimestamp());
            Interlocked.Decrement(ref _clients);
        }
    }

    private async Task ServeConnectionAsync(
        DiagnosticsConnection connection,
        ConcurrentDictionary<long, (CancellationTokenSource Cancellation, Task Task)> requests,
        CancellationToken cancellationToken)
    {
        var helloDone = false;
        while (true)
        {
            DiagnosticsMessage? message;
            try
            {
                message = await connection.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DiagnosticsProtocolException ex)
            {
                await TryWriteAsync(connection, null, DiagnosticsMessage.Failure(null, ex.ToError())).ConfigureAwait(false);
                return;
            }

            if (message is null)
            {
                return;
            }

            if (message.IsNotification)
            {
                if (message.Method == DiagnosticsProtocol.CancelMethod
                    && TryCancelParams(message) is { } cancelled
                    && requests.TryGetValue(cancelled.Id, out var request))
                {
                    await request.Cancellation.CancelAsync().ConfigureAwait(false);
                }

                continue;
            }

            if (!message.IsRequest)
            {
                continue;
            }

            var id = message.Id!.Value;
            if (!helloDone)
            {
                var (response, accepted) = Hello(message, id);
                await WriteAsync(connection, id, response, cancellationToken).ConfigureAwait(false);
                if (accepted == false)
                {
                    return;
                }

                helloDone = accepted == true;
                continue;
            }

            // Registered before it runs, so a cancel that follows at once finds it; a reused id is refused.
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var placeholder = (cancellation, Task.CompletedTask);
            if (!requests.TryAdd(id, placeholder))
            {
                cancellation.Dispose();
                await WriteAsync(connection, id, DiagnosticsMessage.Failure(id, new DiagnosticsError(
                    DiagnosticsErrorCodes.InvalidRequest, $"Request {id} is still running.")), cancellationToken).ConfigureAwait(false);
                continue;
            }

            var task = Task.Run(() => HandleAsync(connection, message, id, requests, cancellation), CancellationToken.None);
            requests.TryUpdate(id, (cancellation, task), placeholder);
        }
    }

    private static CancelParams? TryCancelParams(DiagnosticsMessage message)
    {
        try
        {
            return message.GetParams(DiagnosticsJsonContext.Default.CancelParams);
        }
        catch (DiagnosticsProtocolException)
        {
            return null;
        }
    }

    private (DiagnosticsMessage Response, bool? Accepted) Hello(DiagnosticsMessage message, long id)
    {
        if (message.Method != DiagnosticsProtocol.HelloMethod)
        {
            return (DiagnosticsMessage.Failure(id, new DiagnosticsError(DiagnosticsErrorCodes.InvalidRequest, "The first request must be hello.")), null);
        }

        HelloParams? hello;
        try
        {
            hello = message.GetParams(DiagnosticsJsonContext.Default.HelloParams);
        }
        catch (DiagnosticsProtocolException ex)
        {
            return (DiagnosticsMessage.Failure(id, ex.ToError()), null);
        }

        if (hello is null)
        {
            return (DiagnosticsMessage.Failure(id, new DiagnosticsError(DiagnosticsErrorCodes.InvalidParams, "hello needs parameters.")), null);
        }

        if (hello.Protocol != DiagnosticsProtocol.Version)
        {
            return (DiagnosticsMessage.Failure(id, new DiagnosticsError(
                DiagnosticsErrorCodes.ProtocolMismatch,
                $"This session host ({ServerName}) speaks protocol {DiagnosticsProtocol.Version}, not {hello.Protocol}.")), false);
        }

        var result = new HelloResult(DiagnosticsProtocol.Version, ServerName, Environment.ProcessId);
        return (DiagnosticsMessage.Success(id, result, DiagnosticsJsonContext.Default.HelloResult), true);
    }

    private async Task HandleAsync(
        DiagnosticsConnection connection,
        DiagnosticsMessage message,
        long id,
        ConcurrentDictionary<long, (CancellationTokenSource Cancellation, Task Task)> requests,
        CancellationTokenSource cancellation)
    {
        DiagnosticsMessage response;
        try
        {
            response = await DispatchAsync(connection, message, id, cancellation.Token).ConfigureAwait(false);
        }
        catch (DiagnosticsProtocolException ex)
        {
            response = DiagnosticsMessage.Failure(id, ex.ToError());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            response = DiagnosticsMessage.Failure(id, new DiagnosticsError(DiagnosticsErrorCodes.Internal, $"{message.Method} was cancelled."));
        }
        catch (Exception ex)
        {
            response = DiagnosticsMessage.Failure(id, new DiagnosticsError(DiagnosticsErrorCodes.Internal, ex.Message));
        }
        finally
        {
            requests.TryRemove(id, out _);
        }

        await TryWriteAsync(connection, id, response).ConfigureAwait(false);
        cancellation.Dispose();
        if (message.Method == HostMethods.Shutdown && response.Error is null)
        {
            RequestShutdown();
        }
    }

    // The endpoint methods a client may call through session.app.
    private static readonly HashSet<string> AppMethods = new(StringComparer.Ordinal)
    {
        DiagnosticsAppMethods.Tree,
        DiagnosticsAppMethods.Find,
        DiagnosticsAppMethods.Inspect,
        DiagnosticsAppMethods.InspectAt,
        DiagnosticsAppMethods.Focus,
        DiagnosticsAppMethods.FocusSet,
        DiagnosticsAppMethods.FocusNext,
        DiagnosticsAppMethods.FocusPrevious,
        DiagnosticsAppMethods.Navigate,
        DiagnosticsAppMethods.Route,
        DiagnosticsAppMethods.Errors,
        DiagnosticsAppMethods.Logs,
        DiagnosticsAppMethods.Stats,
    };

    private static readonly TimeSpan DefaultAppTimeout = TimeSpan.FromSeconds(10);

    private async Task<DiagnosticsMessage> DispatchAsync(DiagnosticsConnection connection, DiagnosticsMessage message, long id, CancellationToken cancellationToken)
    {
        var json = HostJsonContext.Default;
        switch (message.Method)
        {
            case HostMethods.Status:
                return Success(id, Status(), json.HostStatusResult);

            case HostMethods.Shutdown:
                return Success(id, new HostShutdownResult(await StopAllSessionsAsync().ConfigureAwait(false)), json.HostShutdownResult);

            case HostMethods.List:
                return Success(id, await ListAsync(cancellationToken).ConfigureAwait(false), json.ListResult);

            case HostMethods.Start:
                return Success(id, await StartAsync(Required(message, json.StartParams), cancellationToken).ConfigureAwait(false), json.SessionInfo);

            case HostMethods.Stop:
            {
                var parameters = Required(message, json.StopParams);
                var session = Take(parameters.Session);
                return Success(id, await session.StopAsync(TimeSpan.FromMilliseconds(parameters.TimeoutMs)).ConfigureAwait(false), json.StopResult);
            }

            case HostMethods.Restart:
            {
                var parameters = Required(message, json.RestartParams);
                var session = Find(parameters.Session).RequirePty("restart");
                await session.RestartAsync(TimeSpan.FromMilliseconds(parameters.TimeoutMs), cancellationToken).ConfigureAwait(false);
                return Success(id, session.Info, json.SessionInfo);
            }

            case HostMethods.Resize:
            {
                var parameters = Required(message, json.ResizeParams);
                if (parameters.Width is < 1 or > short.MaxValue || parameters.Height is < 1 or > short.MaxValue)
                {
                    throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "width and height must be between 1 and 32767.");
                }

                return Success(id, await Find(parameters.Session).RequirePty("resize").ResizeAsync(parameters.Width, parameters.Height, cancellationToken).ConfigureAwait(false), json.ResizeResult);
            }

            case HostMethods.Keys:
            {
                var parameters = Required(message, json.KeysParams);
                return Success(id, await Find(parameters.Session).KeysAsync(parameters, cancellationToken).ConfigureAwait(false), json.SentResult);
            }

            case HostMethods.Type:
            {
                var parameters = Required(message, json.TextParams);
                return Success(id, await Find(parameters.Session).TypeAsync(parameters.Text, cancellationToken).ConfigureAwait(false), json.SentResult);
            }

            case HostMethods.Paste:
            {
                var parameters = Required(message, json.TextParams);
                return Success(id, await Find(parameters.Session).PasteAsync(parameters.Text, cancellationToken).ConfigureAwait(false), json.PasteResult);
            }

            case HostMethods.Raw:
            {
                var parameters = Required(message, json.TextParams);
                return Success(id, await Find(parameters.Session).RawAsync(parameters.Text, cancellationToken).ConfigureAwait(false), json.RawResult);
            }

            case HostMethods.Click:
            {
                var parameters = Required(message, json.ClickParams);
                return Success(id, await Find(parameters.Session).ClickAsync(parameters, cancellationToken).ConfigureAwait(false), json.ClickResult);
            }

            case HostMethods.Mouse:
            {
                var parameters = Required(message, json.MouseParams);
                return Success(id, await Find(parameters.Session).MouseAsync(parameters, cancellationToken).ConfigureAwait(false), json.MouseResult);
            }

            case HostMethods.Screen:
            {
                var parameters = Required(message, json.PtyScreenParams);
                return Success(id, await Find(parameters.Session).ScreenAsync(parameters, cancellationToken).ConfigureAwait(false), json.PtyScreenResult);
            }

            case HostMethods.Frames:
            {
                var parameters = Required(message, json.FramesParams);
                return Success(id, Find(parameters.Session).RequirePty("frames").Frames(parameters.Last), json.FramesResult);
            }

            case HostMethods.Diff:
            {
                var parameters = Required(message, json.DiffParams);
                return Success(id, Find(parameters.Session).RequirePty("diff").Diff(parameters.From, parameters.To), json.DiffResult);
            }

            case HostMethods.Output:
            {
                var parameters = Required(message, json.OutputParams);
                return Success(id, Find(parameters.Session).RequirePty("output").Output(parameters.Since, parameters.MaxBytes), json.OutputResult);
            }

            case HostMethods.Attach:
                return Success(id, await AttachAsync(Required(message, json.AttachParams), cancellationToken).ConfigureAwait(false), json.SessionInfo);

            case HostMethods.Detach:
            {
                var session = Take(Required(message, json.SessionParams).Session);
                await session.DisposeAsync().ConfigureAwait(false);
                return Success(id, new DetachResult(true), json.DetachResult);
            }

            case HostMethods.App:
            {
                var parameters = Required(message, json.AppParams);
                if (!AppMethods.Contains(parameters.Method))
                {
                    throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"'{parameters.Method}' is not an app method.");
                }

                var session = Find(parameters.Session);
                var timeout = parameters.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(parameters.TimeoutMs) : DefaultAppTimeout;
                var result = await session.App.CallAsync(parameters.Method, parameters.Params, timeout, cancellationToken).ConfigureAwait(false);
                return Success(id, session.AdjustAppResult(parameters.Method, result), json.JsonElement);
            }

            case HostMethods.Wait:
            {
                var parameters = Required(message, json.WaitParams);
                var session = Find(parameters.Session);
                var result = await HostWait.WaitAsync(
                    session,
                    parameters.Condition,
                    TimeSpan.FromMilliseconds(parameters.TimeoutMs),
                    (elapsed, frame) => SendProgressAsync(connection, id, elapsed, frame, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                return Success(id, result, json.WaitResult);
            }

            case HostMethods.Assert:
            {
                var parameters = Required(message, json.AssertParams);
                var timeout = parameters.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(parameters.TimeoutMs) : DefaultAppTimeout;
                await HostWait.AssertAsync(Find(parameters.Session), parameters.Condition, parameters.Not, timeout, cancellationToken).ConfigureAwait(false);
                return Success(id, new AssertResult(true), json.AssertResult);
            }

            default:
                return DiagnosticsMessage.Failure(id, new DiagnosticsError(DiagnosticsErrorCodes.MethodNotFound, $"Unknown method '{message.Method}'."));
        }
    }

    private HostStatusResult Status()
    {
        int sessions;
        lock (_sessions)
        {
            sessions = _sessions.Count;
        }

        return new HostStatusResult(
            Environment.ProcessId,
            _version,
            DiagnosticsProtocol.Version,
            sessions,
            Volatile.Read(ref _clients),
            _startedAt,
            (long)_idleTimeout.TotalMilliseconds);
    }

    private async Task<ListResult> ListAsync(CancellationToken cancellationToken)
    {
        HostSession[] sessions;
        lock (_sessions)
        {
            sessions = [.. _sessions.Values];
        }

        // One try each to pair with the app, so the list says where the app layer stands now.
        await Task.WhenAll(sessions.Select(session => session.App.RefreshAsync(cancellationToken))).ConfigureAwait(false);
        return new ListResult([.. sessions.Select(static session => session.Info).OrderBy(static info => info.Name, StringComparer.Ordinal)]);
    }

    // Progress is best effort: the client may be gone, and then the request is cancelled anyway.
    private static async Task SendProgressAsync(DiagnosticsConnection connection, long id, long elapsedMs, long frame, CancellationToken cancellationToken)
    {
        try
        {
            var notification = DiagnosticsMessage.Notification(DiagnosticsProtocol.ProgressMethod, new WaitProgress(id, elapsedMs, frame), HostJsonContext.Default.WaitProgress);
            await connection.WriteAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or DiagnosticsProtocolException)
        {
        }
    }

    private async Task<SessionInfo> StartAsync(StartParams parameters, CancellationToken cancellationToken)
    {
        var name = Reserve(parameters.Name, "s");
        HostSession? session = null;
        try
        {
            DiagnosticsPaths.EnsureUserOnlyDirectory(_files.SessionsDirectory);
            session = await PtySession.StartAsync(name, parameters, _files.LedgerFile(name), _files.StateDirectory, cancellationToken).ConfigureAwait(false);
            return session.Info;
        }
        finally
        {
            Publish(name, session);
        }
    }

    private async Task<SessionInfo> AttachAsync(AttachParams parameters, CancellationToken cancellationToken)
    {
        var name = Reserve(parameters.Name, "a");
        HostSession? session = null;
        try
        {
            var timeout = parameters.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(parameters.TimeoutMs) : DefaultAppTimeout;
            session = await AttachSession.AttachAsync(name, parameters.Pid, _files.StateDirectory, timeout, cancellationToken).ConfigureAwait(false);
            return session.Info;
        }
        finally
        {
            Publish(name, session);
        }
    }

    // Takes a name for a session that is being created, so two starts cannot pick the same one.
    private string Reserve(string? requested, string prefix)
    {
        lock (_sessions)
        {
            var name = requested ?? NextName(prefix);
            if (_sessions.ContainsKey(name) || !_starting.Add(name))
            {
                throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"A session named '{name}' already exists.");
            }

            return name;
        }
    }

    private void Publish(string name, HostSession? session)
    {
        lock (_sessions)
        {
            _starting.Remove(name);
            if (session is not null)
            {
                _sessions[name] = session;
            }
        }
    }

    // The first free s1, s2, … (or a1, a2, …); called under the sessions lock.
    private string NextName(string prefix)
    {
        for (var number = 1; ; number++)
        {
            var name = prefix + number.ToString(CultureInfo.InvariantCulture);
            if (!_sessions.ContainsKey(name) && !_starting.Contains(name))
            {
                return name;
            }
        }
    }

    private HostSession Find(string name)
    {
        lock (_sessions)
        {
            return _sessions.TryGetValue(name, out var session) ? session : throw NotFound(name);
        }
    }

    private HostSession Take(string name)
    {
        lock (_sessions)
        {
            return _sessions.Remove(name, out var session) ? session : throw NotFound(name);
        }
    }

    private static DiagnosticsProtocolException NotFound(string name)
        => new(DiagnosticsErrorCodes.SessionNotFound, $"There is no session named '{name}'.");

    private async Task<int> StopAllSessionsAsync()
    {
        HostSession[] sessions;
        lock (_sessions)
        {
            sessions = [.. _sessions.Values];
            _sessions.Clear();
        }

        foreach (var session in sessions)
        {
            await QuietlyAsync(session.DisposeAsync().AsTask()).ConfigureAwait(false);
        }

        return sessions.Length;
    }
}
