// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Host;

/// <summary>
/// One connection from the session host to an app's diagnostics endpoint, after a successful hello.
/// </summary>
/// <remarks>
/// The endpoint answers the requests of a connection in order and ignores <c>cancel</c>, so requests run one at a
/// time, and a request that times out ends the link: its late answer could otherwise be taken for the next one's.
/// </remarks>
internal sealed class AppLink : IAsyncDisposable
{
    public const string ClientPrefix = "razorconsole-host/";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);

    private readonly DiagnosticsConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _nextId = 1;
    private volatile bool _broken;

    private AppLink(DiagnosticsConnection connection, DiagnosticsDiscoveryInfo info, HelloResult hello)
    {
        _connection = connection;
        Info = info;
        Hello = hello;
    }

    public DiagnosticsDiscoveryInfo Info { get; }

    public HelloResult Hello { get; }

    /// <summary>
    /// Whether the connection failed or timed out, so a new one is needed.
    /// </summary>
    public bool IsBroken => _broken;

    public bool Supports(string feature) => Hello.Features?.Contains(feature, StringComparer.Ordinal) == true;

    /// <summary>
    /// Connects and says hello, or returns <see langword="null"/> when the endpoint is not there (yet) or is busy.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException">The app speaks another protocol version
    /// (<see cref="DiagnosticsErrorCodes.AppLayerUnavailable"/> with reason <c>incompatible_protocol</c>).</exception>
    public static async Task<AppLink?> TryConnectAsync(DiagnosticsDiscoveryInfo info, string clientVersion, CancellationToken cancellationToken)
    {
        Stream stream;
        try
        {
            stream = await DiagnosticsTransport.ConnectAsync(info.Endpoint, ConnectTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
        {
            return null;
        }

        var connection = new DiagnosticsConnection(stream);
        try
        {
            var hello = new HelloParams(DiagnosticsProtocol.Version, ClientPrefix + clientVersion);
            var request = DiagnosticsMessage.Request(0, DiagnosticsProtocol.HelloMethod, hello, DiagnosticsJsonContext.Default.HelloParams);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HelloTimeout);
            var response = await RoundTripAsync(connection, request, timeout.Token).ConfigureAwait(false);
            if (response?.Error is { } error)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return error.Code == DiagnosticsErrorCodes.ProtocolMismatch
                    ? throw Unavailable(DiagnosticsUnavailableReasons.IncompatibleProtocol, $"The app's diagnostics endpoint cannot talk to this tool: {error.Message}")
                    : null;
            }

            var result = response?.GetResult(DiagnosticsJsonContext.Default.HelloResult);
            if (result is null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new AppLink(connection, info, result);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or DiagnosticsProtocolException { Code: not DiagnosticsErrorCodes.AppLayerUnavailable }
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Sends one request and waits up to <paramref name="timeout"/> for its answer.
    /// </summary>
    /// <returns>The result, or an empty object for a request whose answer has no result.</returns>
    /// <exception cref="DiagnosticsProtocolException">The app answered with an error, did not answer in time, or went away.</exception>
    public async Task<JsonElement> CallAsync(string method, JsonElement? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_broken)
            {
                throw Gone(null);
            }

            var request = new DiagnosticsMessage { Id = _nextId++, Method = method, Params = parameters };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            DiagnosticsMessage? response;
            try
            {
                response = await RoundTripAsync(_connection, request, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The answer may still arrive, so this connection cannot be reused.
                _broken = true;
                await _connection.DisposeAsync().ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.Timeout, $"The app did not answer {method} within {timeout.TotalSeconds:0.###}s.");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _broken = true;
                throw Gone(ex);
            }

            if (response is null)
            {
                _broken = true;
                throw Gone(null);
            }

            if (response.Error is { } error)
            {
                throw new DiagnosticsProtocolException(error.Code, error.Message, errorData: error.Data);
            }

            return response.Result ?? default;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _broken = true;
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    internal static DiagnosticsProtocolException Unavailable(string reason, string message)
        => new(
            DiagnosticsErrorCodes.AppLayerUnavailable,
            message,
            errorData: JsonSerializer.SerializeToElement(new LayerUnavailableData(reason), HostJsonContext.Default.LayerUnavailableData));

    private static DiagnosticsProtocolException Gone(Exception? inner)
        => new(
            DiagnosticsErrorCodes.AppLayerUnavailable,
            "The app closed its diagnostics connection.",
            inner,
            JsonSerializer.SerializeToElement(new LayerUnavailableData(DiagnosticsUnavailableReasons.NotConnectedYet), HostJsonContext.Default.LayerUnavailableData));

    // Reads until the answer to this request; notifications are skipped. An error without an id (such as
    // too_many_connections) answers whatever is waiting.
    private static async Task<DiagnosticsMessage?> RoundTripAsync(DiagnosticsConnection connection, DiagnosticsMessage request, CancellationToken cancellationToken)
    {
        await connection.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var message = await connection.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return null;
            }

            if ((message.IsResponse && message.Id == request.Id) || message is { Id: null, Method: null, Error: not null })
            {
                return message;
            }
        }
    }
}
