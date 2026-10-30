// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using RazorConsole.Cli.Host;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Operations;

/// <summary>
/// One connection to the session host, after a successful hello. Requests run one at a time.
/// </summary>
internal sealed class HostClient : IAsyncDisposable
{
    public const string ClientPrefix = "razorconsole-cli/";

    /// <summary>
    /// How much longer than the operation's own timeout the client waits for the host's answer.
    /// </summary>
    public static readonly TimeSpan ResponseGrace = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);

    private readonly DiagnosticsConnection _connection;
    private long _nextId = 1;

    private HostClient(DiagnosticsConnection connection, HelloResult hello)
    {
        _connection = connection;
        Hello = hello;
        HostVersion = hello.Server.StartsWith(SessionHost.ServerPrefix, StringComparison.Ordinal)
            ? hello.Server[SessionHost.ServerPrefix.Length..]
            : hello.Server;
    }

    public HelloResult Hello { get; }

    public string HostVersion { get; }

    /// <summary>
    /// Connects and says hello, or returns <see langword="null"/> when nothing answers on the endpoint in
    /// <paramref name="probe"/>.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException">The host speaks another protocol version (<c>host_version_mismatch</c>).</exception>
    public static async Task<HostClient?> TryConnectAsync(string endpoint, string clientVersion, TimeSpan probe, CancellationToken cancellationToken)
    {
        Stream stream;
        try
        {
            stream = await DiagnosticsTransport.ConnectAsync(endpoint, probe, cancellationToken).ConfigureAwait(false);
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
            var response = await RoundTripAsync(connection, request, HelloTimeout, cancellationToken).ConfigureAwait(false);
            if (response?.Error is { } error)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                if (error.Code == DiagnosticsErrorCodes.ProtocolMismatch)
                {
                    throw new DiagnosticsProtocolException(
                        DiagnosticsErrorCodes.HostVersionMismatch,
                        $"{error.Message} Stop it with 'razorconsole host stop'.");
                }

                return null;
            }

            var result = response?.GetResult(DiagnosticsJsonContext.Default.HelloResult);
            if (result is null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new HostClient(connection, result);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException
            or (DiagnosticsProtocolException and not { Code: DiagnosticsErrorCodes.HostVersionMismatch }))
        {
            // A host that is shutting down, or something else on the endpoint.
            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Sends one request and waits for its answer for up to <paramref name="timeout"/>; then sends <c>cancel</c>.
    /// <paramref name="onNotification"/> sees the notifications that arrive meanwhile, such as a wait's <c>progress</c>;
    /// it runs on the read loop and must not block.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException">The host answered with an error, timed out, or went away.</exception>
    public async Task<TResult> RequestAsync<TParams, TResult>(
        string method,
        TParams parameters,
        JsonTypeInfo<TParams> parametersType,
        JsonTypeInfo<TResult> resultType,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<DiagnosticsMessage>? onNotification = null)
    {
        var id = _nextId++;
        var request = DiagnosticsMessage.Request(id, method, parameters, parametersType);
        DiagnosticsMessage? response;
        try
        {
            response = await RoundTripAsync(_connection, request, timeout, cancellationToken, onNotification).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            await TryCancelAsync(id).ConfigureAwait(false);
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.Timeout,
                $"The session host did not answer {method} within {timeout.TotalSeconds:0.###}s.");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw HostGone(ex);
        }

        if (response is null)
        {
            throw HostGone(null);
        }

        if (response.Error is { } error)
        {
            throw new DiagnosticsProtocolException(error.Code, error.Message, errorData: error.Data);
        }

        return response.GetResult(resultType)
            ?? throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.Internal, $"The session host answered {method} without a result.");
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static DiagnosticsProtocolException HostGone(Exception? inner)
        => new(DiagnosticsErrorCodes.HostUnavailable, "The session host closed the connection.", inner);

    // Reads until the answer to this request; anything else (notifications, stray answers) is skipped.
    private static async Task<DiagnosticsMessage?> RoundTripAsync(
        DiagnosticsConnection connection,
        DiagnosticsMessage request,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<DiagnosticsMessage>? onNotification = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await connection.WriteAsync(request, deadline.Token).ConfigureAwait(false);
            while (true)
            {
                var message = await connection.ReadAsync(deadline.Token).ConfigureAwait(false);
                if (message is null)
                {
                    return null;
                }

                if (message.IsResponse && message.Id == request.Id || message is { Id: null, Method: null, Error: not null })
                {
                    return message;
                }

                if (message.IsNotification)
                {
                    onNotification?.Invoke(message);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    private async Task TryCancelAsync(long id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            var cancel = DiagnosticsMessage.Notification(DiagnosticsProtocol.CancelMethod, new CancelParams(id), DiagnosticsJsonContext.Default.CancelParams);
            await _connection.WriteAsync(cancel, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or DiagnosticsProtocolException)
        {
            // Closing the connection cancels it too.
        }
    }
}
