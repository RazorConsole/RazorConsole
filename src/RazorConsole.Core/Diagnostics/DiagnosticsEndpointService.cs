// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// The app's diagnostics endpoint: listens on the per-process, current-user-only endpoint, answers the methods in
/// <see cref="DiagnosticsAppMethods"/>, and advertises itself with a discovery file in the state directory.
/// </summary>
/// <remarks>
/// <para>
/// It is registered only when <see cref="DiagnosticsActivation"/> allows diagnostics, and does nothing when the
/// options opt out (then <see cref="DiagnosticsState"/> is <see langword="null"/>): no socket, thread or file.
/// </para>
/// <para>
/// At most <see cref="MaxConnections"/> connections are served at once. Each one buffers at most
/// <see cref="DiagnosticsProtocol.MaxQueuedBytes"/> of unread input, so the app holds at most
/// <see cref="MaxConnections"/> times that for diagnostics. A connection over the limit gets a
/// <see cref="DiagnosticsErrorCodes.TooManyConnections"/> error and is closed without reading from it.
/// </para>
/// <para>
/// A failure to listen, such as another live process on the endpoint, is logged and leaves the app running without
/// an endpoint. Requests on one connection are answered in order; every request must follow a successful hello.
/// </para>
/// <para>
/// Injected input goes through a <see cref="DiagnosticsInputQueue"/> that the app's input loop drains, and is
/// answered after its handlers have run.
/// </para>
/// </remarks>
internal sealed partial class DiagnosticsEndpointService : IHostedService, IAsyncDisposable
{
    public const int MaxConnections = 4;

    /// <summary>
    /// Set by the CLI session host in the app's environment; echoed in the discovery file to pair app and PTY.
    /// </summary>
    public const string SessionTokenEnvironmentVariableName = "RAZORCONSOLE_DIAGNOSTICS_SESSION";

    /// <summary>
    /// Longest accepted <see cref="InputParams.Data"/>, in characters. Each key runs the app's handlers in turn.
    /// </summary>
    public const int MaxInputDataLength = 64 * 1024;

    private const int MaxSessionTokenLength = 128;
    private const int MaxClickCount = 3;
    private static readonly TimeSpan RejectTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultInputTimeout = TimeSpan.FromSeconds(10);

    private readonly DiagnosticsState? _state;
    private readonly ConsoleAppOptions _options;
    private readonly DiagnosticsAppQueries? _queries;
    private readonly DiagnosticsInputQueue? _input;
    private readonly FocusManager _focusManager;
    private readonly NavigationManager _navigationManager;
    private readonly ConsoleRenderer? _renderer;
    private readonly ILogger<DiagnosticsEndpointService> _logger;
    private readonly DiagnosticsEndpointSettings _settings;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _connections = [];
    private DiagnosticsListener? _listener;
    private Task? _acceptLoop;
    private string? _discoveryFile;
    private int _activeConnections;
    private int _stopped;

    public DiagnosticsEndpointService(
        DiagnosticsState? state,
        FocusManager focusManager,
        NavigationManager navigationManager,
        ConsoleAppOptions options,
        ILogger<DiagnosticsEndpointService> logger,
        DiagnosticsEndpointSettings? settings = null,
        KeyboardEventManager? keyboard = null,
        ConsoleRenderer? renderer = null)
    {
        _state = state;
        _options = options;
        _focusManager = focusManager;
        _navigationManager = navigationManager;
        _renderer = renderer;
        _queries = state is null ? null : new DiagnosticsAppQueries(state, focusManager, navigationManager, options);
        _logger = logger;
        _settings = settings ?? new DiagnosticsEndpointSettings();

        // Hosted services are all created before any starts, so the input loop sees the queue from its first pass.
        if (state is not null && keyboard is not null)
        {
            _input = new DiagnosticsInputQueue();
            keyboard.InjectedInput = _input;
        }
    }

    /// <summary>
    /// The pipe name or socket path while listening; otherwise <see langword="null"/>.
    /// </summary>
    public string? Endpoint => _listener?.Endpoint;

    /// <summary>
    /// The discovery file while listening; otherwise <see langword="null"/>.
    /// </summary>
    public string? DiscoveryFile => _discoveryFile;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_queries is null || _listener is not null)
        {
            return Task.CompletedTask;
        }

        var processId = Environment.ProcessId;
        try
        {
            var stateDirectory = _settings.StateDirectory ?? DiagnosticsPaths.ResolveStateDirectory();
            DiagnosticsPaths.EnsureUserOnlyDirectory(stateDirectory);
            DiagnosticsPaths.EnsureUserOnlyDirectory(DiagnosticsPaths.AppsDirectory(stateDirectory));
            _listener = (_settings.Listen ?? DiagnosticsTransport.Listen)(DiagnosticsPaths.AppEndpoint(stateDirectory, processId));
            _discoveryFile = WriteDiscoveryFile(stateDirectory, processId, _listener.Endpoint);
        }
        catch (Exception ex)
        {
            // Diagnostics never stop the app: a bad state directory, a permission or transport error or a failed
            // discovery write all leave it running without an endpoint.
            LogListenFailed(_logger, ex);
            DisposeListenerAsync().AsTask().GetAwaiter().GetResult();
            _listener = null;
            return Task.CompletedTask;
        }

        LogListening(_logger, _listener.Endpoint);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_listener, _stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_acceptLoop is null || Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        try
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
            await DisposeListenerAsync().ConfigureAwait(false);

            // The accept loop and the connections handle their own errors, so these never fault.
            await _acceptLoop.ConfigureAwait(false);

            Task[] connections;
            lock (_connections)
            {
                connections = [.. _connections];
            }

            // Each connection ends when the stopping token cancels its read.
            await Task.WhenAll(connections).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host stopped waiting; the remaining connections still end on the stopping token.
        }
        finally
        {
            DeleteDiscoveryFile();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task AcceptLoopAsync(DiagnosticsListener listener, CancellationToken cancellationToken)
    {
        try
        {
            await AcceptConnectionsAsync(listener, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // An unexpected transport error ends the endpoint, never the app.
            LogAcceptLoopFailed(_logger, ex);
        }
        catch (Exception)
        {
        }
    }

    private async Task AcceptConnectionsAsync(DiagnosticsListener listener, CancellationToken cancellationToken)
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
                // A client that vanished during the handshake; keep listening.
                LogAcceptFailed(_logger, ex);
                continue;
            }

            // Only this loop increments, so the count never exceeds the limit.
            if (Volatile.Read(ref _activeConnections) >= MaxConnections)
            {
                await RejectAsync(stream).ConfigureAwait(false);
                continue;
            }

            Interlocked.Increment(ref _activeConnections);
            var connection = ServeAsync(stream, cancellationToken);
            lock (_connections)
            {
                _connections.RemoveAll(task => task.IsCompleted);
                _connections.Add(connection);
            }
        }
    }

    private async Task ServeAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new DiagnosticsConnection(stream);
            await ServeConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            LogConnectionClosed(_logger, ex);
        }
        catch (Exception ex)
        {
            // Ends this connection only.
            LogConnectionFailed(_logger, ex);
        }
        finally
        {
            // After the connection and its read queue are gone, so the limit bounds what the app holds.
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    private async Task ServeConnectionAsync(DiagnosticsConnection connection, CancellationToken cancellationToken)
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
                // Bad framing or a flooding peer: say why, then end the connection.
                await TryWriteAsync(connection, DiagnosticsMessage.Failure(null, ex.ToError()), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (message is null)
            {
                return;
            }

            // Notifications (such as cancel) have nothing to act on while every method answers at once.
            if (!message.IsRequest)
            {
                continue;
            }

            var id = message.Id!.Value;
            if (!helloDone)
            {
                var (response, accepted) = Hello(message, id, _input is not null, _renderer is not null);
                await WriteResponseAsync(connection, id, response, cancellationToken).ConfigureAwait(false);
                if (accepted is null)
                {
                    continue;
                }

                if (accepted == false)
                {
                    return;
                }

                helloDone = true;
                continue;
            }

            var result = message.Method switch
            {
                DiagnosticsAppMethods.Input => await InputAsync(message, id, cancellationToken).ConfigureAwait(false),
                DiagnosticsAppMethods.FocusSet or DiagnosticsAppMethods.FocusNext or DiagnosticsAppMethods.FocusPrevious
                    or DiagnosticsAppMethods.Navigate when _renderer is not null
                    => await ChangeAsync(message, id, cancellationToken).ConfigureAwait(false),
                _ => Dispatch(message, id),
            };
            await WriteResponseAsync(connection, id, result, cancellationToken).ConfigureAwait(false);
        }
    }

    // accepted: true on success, false when the connection must close, null when the client may retry.
    private static (DiagnosticsMessage Response, bool? Accepted) Hello(DiagnosticsMessage message, long id, bool acceptsInput, bool canChangeState)
    {
        if (message.Method != DiagnosticsProtocol.HelloMethod)
        {
            return (Failure(id, DiagnosticsErrorCodes.InvalidRequest, "The first request must be hello."), null);
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
            return (Failure(id, DiagnosticsErrorCodes.InvalidParams, "hello needs parameters."), null);
        }

        if (hello.Protocol != DiagnosticsProtocol.Version)
        {
            return (Failure(
                id,
                DiagnosticsErrorCodes.ProtocolMismatch,
                $"This app speaks diagnostics protocol {DiagnosticsProtocol.Version}, not {hello.Protocol}."), false);
        }

        var features = DiagnosticsAppMethods.Features(acceptsInput, canChangeState);
        var result = new HelloResult(DiagnosticsProtocol.Version, ServerName, Environment.ProcessId, features);
        return (DiagnosticsMessage.Success(id, result, DiagnosticsJsonContext.Default.HelloResult), true);
    }

    private DiagnosticsMessage Dispatch(DiagnosticsMessage message, long id)
    {
        var queries = _queries!;
        var context = DiagnosticsJsonContext.Default;
        try
        {
            return message.Method switch
            {
                DiagnosticsAppMethods.Tree => Success(id, queries.Tree(message.GetParams(context.TreeParams) ?? new TreeParams()), context.TreeResult),
                DiagnosticsAppMethods.Find => Success(id, queries.Find(Required(message, context.FindParams)), context.FindResult),
                DiagnosticsAppMethods.Inspect => Success(id, queries.Inspect(Required(message, context.InspectParams)), context.InspectResult),
                DiagnosticsAppMethods.InspectAt => Success(id, queries.InspectAt(Required(message, context.InspectAtParams)), context.InspectAtResult),
                DiagnosticsAppMethods.Focus => Success(id, queries.Focus(), context.FocusResult),
                DiagnosticsAppMethods.Route => Success(id, queries.Route(), context.RouteResult),
                DiagnosticsAppMethods.Errors => Success(id, queries.Errors(message.GetParams(context.ErrorsParams) ?? new ErrorsParams()), context.ErrorsResult),
                DiagnosticsAppMethods.Logs => Success(id, queries.Logs(message.GetParams(context.LogsParams) ?? new LogsParams()), context.LogsResult),
                DiagnosticsAppMethods.Stats => Success(id, queries.Stats(), context.StatsResult),
                DiagnosticsAppMethods.Screen => Success(id, queries.Screen(message.GetParams(context.ScreenParams) ?? new ScreenParams()), context.ScreenResult),
                DiagnosticsProtocol.HelloMethod => Failure(id, DiagnosticsErrorCodes.InvalidRequest, "hello was already sent."),
                _ => Failure(id, DiagnosticsErrorCodes.MethodNotFound, $"Unknown method '{message.Method}'."),
            };
        }
        catch (DiagnosticsProtocolException ex)
        {
            return DiagnosticsMessage.Failure(id, ex.ToError());
        }
        catch (RegexMatchTimeoutException)
        {
            return Failure(id, DiagnosticsErrorCodes.InvalidParams, "The selector's regular expression took too long.");
        }
        catch (Exception ex)
        {
            LogRequestFailed(_logger, message.Method, ex);
            return Failure(id, DiagnosticsErrorCodes.Internal, ex.Message);
        }
    }

    // Changes focus or the route in-process and answers after the change ran, or with a timeout error. Focus changes
    // call FocusManager directly: it dispatches the focus events to the latest render itself, and may wait for a newer
    // render, which would deadlock if this ran on the dispatcher. Navigation runs on the dispatcher like any event.
    private async Task<DiagnosticsMessage> ChangeAsync(DiagnosticsMessage message, long id, CancellationToken cancellationToken)
    {
        var context = DiagnosticsJsonContext.Default;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DefaultInputTimeout);
            try
            {
                switch (message.Method)
                {
                    case DiagnosticsAppMethods.FocusSet:
                        var key = Required(message, context.FocusSetParams).Key;
                        if (string.IsNullOrWhiteSpace(key)
                            || !_focusManager.GetFocusTargets().Any(target => string.Equals(target.Key, key, StringComparison.Ordinal)))
                        {
                            // FocusAsync would keep an unknown key pending until a target with it renders.
                            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.ElementNotFound, $"No focus target has the key '{key}'.");
                        }

                        return FocusChange(id, await _focusManager.FocusAsync(key, timeout.Token).ConfigureAwait(false));
                    case DiagnosticsAppMethods.FocusNext:
                        return FocusChange(id, await _focusManager.FocusNextAsync(timeout.Token).ConfigureAwait(false));
                    case DiagnosticsAppMethods.FocusPrevious:
                        return FocusChange(id, await _focusManager.FocusPreviousAsync(timeout.Token).ConfigureAwait(false));
                    default:
                        var uri = Required(message, context.NavigateParams).Uri;
                        if (string.IsNullOrWhiteSpace(uri))
                        {
                            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "navigate needs a uri.");
                        }

                        await _renderer!.Dispatcher.InvokeAsync(() => _navigationManager.NavigateTo(uri)).WaitAsync(timeout.Token).ConfigureAwait(false);
                        var current = _navigationManager.Uri;
                        return Success(
                            id,
                            new NavigateResult(current, _navigationManager.ToBaseRelativePath(current), _state!.LatestFrame?.Sequence ?? 0),
                            context.NavigateResult);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.Timeout, $"{message.Method} did not finish in time.");
            }
        }
        catch (DiagnosticsProtocolException ex)
        {
            return DiagnosticsMessage.Failure(id, ex.ToError());
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(_logger, message.Method, ex);
            return Failure(id, DiagnosticsErrorCodes.Internal, ex.Message);
        }
    }

    private DiagnosticsMessage FocusChange(long id, bool changed)
        => Success(
            id,
            new FocusChangeResult(changed, _queries!.Focus().Current, _state!.LatestFrame?.Sequence ?? 0),
            DiagnosticsJsonContext.Default.FocusChangeResult);

    // Answers after the input's handlers have run, or with a timeout error.
    private async Task<DiagnosticsMessage> InputAsync(DiagnosticsMessage message, long id, CancellationToken cancellationToken)
    {
        try
        {
            if (_input is null)
            {
                throw DiagnosticsInputQueue.Unavailable();
            }

            var parameters = Required(message, DiagnosticsJsonContext.Default.InputParams);
            if (parameters.Data is null && parameters.Mouse is null)
            {
                throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "input needs data or mouse.");
            }

            if (parameters.Data?.Length > MaxInputDataLength)
            {
                throw new DiagnosticsProtocolException(
                    DiagnosticsErrorCodes.InvalidParams,
                    $"input data is limited to {MaxInputDataLength} characters.");
            }

            if (parameters.TimeoutMs <= 0)
            {
                throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "timeoutMs must be positive.");
            }

            var mouse = parameters.Mouse is null ? null : ToMouseEvents(parameters.Mouse);
            if (mouse is not null && !_options.ConsoleLiveDisplayOptions.EnableMouseEvents)
            {
                throw DiagnosticsInputQueue.Unavailable(DiagnosticsUnavailableReasons.MouseDisabled);
            }

            var item = _input.Enqueue(parameters.Data, mouse);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(parameters.TimeoutMs is { } ms ? TimeSpan.FromMilliseconds(ms) : DefaultInputTimeout);
            try
            {
                await item.Completion.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !item.Completion.IsCompleted)
            {
                var dispatched = !item.TryCancel();
                throw new DiagnosticsProtocolException(
                    DiagnosticsErrorCodes.Timeout,
                    dispatched ? "The app's input handlers did not finish in time." : "The app did not read the input in time.",
                    errorData: JsonSerializer.SerializeToElement(new InputTimeoutData(dispatched), DiagnosticsJsonContext.Default.InputTimeoutData));
            }

            return Success(id, new InputResult(_state!.LatestFrame?.Sequence ?? 0), DiagnosticsJsonContext.Default.InputResult);
        }
        catch (DiagnosticsProtocolException ex)
        {
            return DiagnosticsMessage.Failure(id, ex.ToError());
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(_logger, message.Method, ex);
            return Failure(id, DiagnosticsErrorCodes.Internal, ex.Message);
        }
    }

    private static TerminalMouseEvent[] ToMouseEvents(DiagnosticsMouseInput input)
    {
        if (input.X < 0 || input.Y < 0)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "Mouse coordinates must not be negative.");
        }

        var button = input.Button switch
        {
            null or "left" => 0,
            "middle" => 1,
            "right" => 2,
            _ => throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"Unknown mouse button '{input.Button}'."),
        };

        ConsoleModifiers modifiers = 0;
        foreach (var modifier in input.Modifiers ?? [])
        {
            modifiers |= modifier switch
            {
                "shift" => ConsoleModifiers.Shift,
                "ctrl" => ConsoleModifiers.Control,
                "alt" => ConsoleModifiers.Alt,
                _ => throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"Unknown modifier '{modifier}'."),
            };
        }

        TerminalMouseEvent Event(TerminalMouseKind kind) => new(kind, input.X, input.Y, button, input.DeltaY ?? 0, modifiers);

        switch (input.Kind)
        {
            case "down":
                return [Event(TerminalMouseKind.Down)];
            case "up":
                return [Event(TerminalMouseKind.Up)];
            case "move":
                return [Event(TerminalMouseKind.Move)];
            case "wheel":
                return [Event(TerminalMouseKind.Wheel)];
            case "click":
                var count = input.Count ?? 1;
                if (count is < 1 or > MaxClickCount)
                {
                    throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"count must be 1 to {MaxClickCount}.");
                }

                var events = new TerminalMouseEvent[count * 2];
                for (var i = 0; i < count; i++)
                {
                    events[2 * i] = Event(TerminalMouseKind.Down);
                    events[(2 * i) + 1] = Event(TerminalMouseKind.Up);
                }

                return events;
            default:
                throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"Unknown mouse kind '{input.Kind}'.");
        }
    }

    private static T Required<T>(DiagnosticsMessage message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => message.GetParams(typeInfo)
            ?? throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"{message.Method} needs parameters.");

    private static DiagnosticsMessage Success<T>(long id, T result, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => DiagnosticsMessage.Success(id, result, typeInfo);

    private static DiagnosticsMessage Failure(long? id, string code, string message)
        => DiagnosticsMessage.Failure(id, new DiagnosticsError(code, message));

    // A result over the message limit (a huge tree) becomes an error for that request instead of ending the connection.
    private static async Task WriteResponseAsync(DiagnosticsConnection connection, long id, DiagnosticsMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await connection.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (DiagnosticsProtocolException ex) when (ex.Code == DiagnosticsErrorCodes.MessageTooLarge)
        {
            await connection.WriteAsync(DiagnosticsMessage.Failure(id, ex.ToError()), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task TryWriteAsync(DiagnosticsConnection connection, DiagnosticsMessage message, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RejectTimeout);
        try
        {
            await connection.WriteAsync(message, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Best effort: the peer may be gone or not reading.
        }
    }

    // Answers a connection over the limit without reading from it, so it costs no buffers.
    private static async Task RejectAsync(Stream stream)
    {
        var rejection = DiagnosticsMessage.Failure(null, new DiagnosticsError(
            DiagnosticsErrorCodes.TooManyConnections,
            $"This app already serves {MaxConnections} diagnostics connections."));
        var payload = JsonSerializer.SerializeToUtf8Bytes(rejection, DiagnosticsJsonContext.Default.DiagnosticsMessage);

        using var timeout = new CancellationTokenSource(RejectTimeout);
        try
        {
            await stream.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Best effort: the client may already be gone.
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string WriteDiscoveryFile(string stateDirectory, int processId, string endpoint)
    {
        DateTimeOffset startTime;
        using (var process = Process.GetCurrentProcess())
        {
            startTime = process.StartTime.ToUniversalTime();
        }

        var info = new DiagnosticsDiscoveryInfo(
            processId,
            startTime,
            DiagnosticsProtocol.Version,
            endpoint,
            Assembly.GetEntryAssembly()?.GetName().Name,
            CoreVersion,
            ValidSessionToken(_settings.SessionToken ?? Environment.GetEnvironmentVariable(SessionTokenEnvironmentVariableName)));
        var payload = JsonSerializer.SerializeToUtf8Bytes(info, DiagnosticsJsonContext.Default.DiagnosticsDiscoveryInfo);

        // Written aside and moved into place, so readers never see a partial file.
        var path = DiagnosticsPaths.AppDiscoveryFile(stateDirectory, processId);
        var temporary = path + ".tmp";
        var fileOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            using (var file = new FileStream(temporary, fileOptions))
            {
                file.Write(payload);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async ValueTask DisposeListenerAsync()
    {
        if (_listener is null)
        {
            return;
        }

        try
        {
            await _listener.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogListenerDisposeFailed(_logger, ex);
        }
    }

    private void DeleteDiscoveryFile()
    {
        if (_discoveryFile is null)
        {
            return;
        }

        try
        {
            File.Delete(_discoveryFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The CLI sweeps stale discovery files.
            LogDiscoveryFileNotDeleted(_logger, _discoveryFile, ex);
        }
    }

    private static string? ValidSessionToken(string? token)
        => !string.IsNullOrEmpty(token)
            && token.Length <= MaxSessionTokenLength
            && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                ? token
                : null;

    private static string CoreVersion
        => typeof(DiagnosticsEndpointService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(DiagnosticsEndpointService).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

    private static string ServerName => "RazorConsole.Core/" + CoreVersion;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Diagnostics endpoint listening on {Endpoint}.")]
    private static partial void LogListening(ILogger logger, string endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Diagnostics endpoint could not start; the app runs without it.")]
    private static partial void LogListenFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Diagnostics endpoint failed to accept a connection.")]
    private static partial void LogAcceptFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Diagnostics endpoint stopped accepting connections; the app keeps running.")]
    private static partial void LogAcceptLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Diagnostics endpoint could not be closed cleanly.")]
    private static partial void LogListenerDisposeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Diagnostics connection failed and was closed.")]
    private static partial void LogConnectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Diagnostics connection closed.")]
    private static partial void LogConnectionClosed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Diagnostics request {Method} failed.")]
    private static partial void LogRequestFailed(ILogger logger, string? method, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Diagnostics discovery file {Path} could not be deleted.")]
    private static partial void LogDiscoveryFileNotDeleted(ILogger logger, string path, Exception exception);
}

/// <summary>
/// Test seams for <see cref="DiagnosticsEndpointService"/>. Apps never register it.
/// </summary>
internal sealed class DiagnosticsEndpointSettings
{
    /// <summary>
    /// Overrides <see cref="DiagnosticsPaths.ResolveStateDirectory()"/>.
    /// </summary>
    public string? StateDirectory { get; init; }

    /// <summary>
    /// Overrides the <see cref="DiagnosticsEndpointService.SessionTokenEnvironmentVariableName"/> environment variable.
    /// </summary>
    public string? SessionToken { get; init; }

    /// <summary>
    /// Overrides <see cref="DiagnosticsTransport.Listen(string)"/>.
    /// </summary>
    public Func<string, DiagnosticsListener>? Listen { get; init; }
}
