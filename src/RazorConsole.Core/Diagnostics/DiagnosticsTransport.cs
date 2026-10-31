// Copyright (c) RazorConsole. All rights reserved.

using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Local, current-user-only byte streams for diagnostics: named pipes with <see cref="PipeOptions.CurrentUserOnly"/>
/// on Windows, and Unix domain sockets (mode 0600, inside a 0700 directory) elsewhere. Never TCP.
/// </summary>
/// <remarks>
/// On Windows <see cref="PipeOptions.CurrentUserOnly"/> also compares elevation, so an elevated client cannot reach a
/// non-elevated endpoint and vice versa.
/// </remarks>
internal static class DiagnosticsTransport
{
    private const UnixFileMode UserOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Starts listening on <paramref name="endpoint"/> (see <see cref="DiagnosticsPaths"/>).
    /// </summary>
    /// <exception cref="IOException">Another live process already listens on the endpoint.</exception>
    /// <exception cref="PathTooLongException">A Unix socket path is longer than the platform allows.</exception>
    public static DiagnosticsListener Listen(string endpoint)
        => OperatingSystem.IsWindows()
            ? new NamedPipeListener(endpoint)
            : new UnixSocketListener(endpoint);

    /// <summary>
    /// Connects to <paramref name="endpoint"/>, waiting up to <paramref name="timeout"/> for it to appear.
    /// </summary>
    /// <exception cref="TimeoutException">Nothing accepted the connection in time.</exception>
    public static Task<Stream> ConnectAsync(string endpoint, TimeSpan timeout, CancellationToken cancellationToken = default)
        => OperatingSystem.IsWindows()
            ? ConnectPipeAsync(endpoint, timeout, cancellationToken)
            : ConnectSocketAsync(endpoint, timeout, cancellationToken);

    private static async Task<Stream> ConnectPipeAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(timeout, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static async Task<Stream> ConnectSocketAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        DiagnosticsPaths.EnsureSocketPathFits(path);

        // A missing or stale socket refuses at once, so retry until the deadline like a pipe client waits.
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException($"No diagnostics endpoint is listening at '{path}'.", ex);
                }

                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private sealed class NamedPipeListener : DiagnosticsListener
    {
        // The overloads without buffer sizes create 0-byte buffers, where every write blocks until the peer reads it.
        private const int PipeBufferSize = 64 * 1024;

        private readonly string _pipeName;
        private NamedPipeServerStream _pending;

        public NamedPipeListener(string pipeName)
        {
            _pipeName = pipeName;

            // FirstPipeInstance: fail when any other process already owns the name, instead of sharing it.
            // Windows reports an owned name as access denied.
            try
            {
                _pending = Create(PipeOptions.FirstPipeInstance);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"Another process is already listening on pipe '{pipeName}'.", ex);
            }
        }

        public override string Endpoint => _pipeName;

        public override async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            var connected = _pending;
            await connected.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

            // Keep one instance waiting so clients always find the pipe.
            _pending = Create(PipeOptions.None);
            return connected;
        }

        public override ValueTask DisposeAsync() => _pending.DisposeAsync();

        private NamedPipeServerStream Create(PipeOptions extra)
            => new(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | extra,
                PipeBufferSize,
                PipeBufferSize);
    }

    [UnsupportedOSPlatform("windows")]
    private sealed class UnixSocketListener : DiagnosticsListener
    {
        private readonly string _path;
        private readonly Socket _socket;

        public UnixSocketListener(string path)
        {
            _path = path;
            DiagnosticsPaths.EnsureSocketPathFits(path);
            RemoveStaleSocket(path);

            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                _socket.Bind(new UnixDomainSocketEndPoint(path));
                File.SetUnixFileMode(path, UserOnlyFileMode);
                _socket.Listen(16);
            }
            catch
            {
                _socket.Dispose();
                throw;
            }
        }

        public override string Endpoint => _path;

        public override async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            var client = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            return new NetworkStream(client, ownsSocket: true);
        }

        public override ValueTask DisposeAsync()
        {
            _socket.Dispose();
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return ValueTask.CompletedTask;
        }

        // A socket file outlives a crashed owner. Remove it only when nobody answers on it.
        private static void RemoveStaleSocket(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            using (var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            {
                try
                {
                    probe.Connect(new UnixDomainSocketEndPoint(path));
                    throw new IOException($"Another process is already listening at '{path}'.");
                }
                catch (SocketException)
                {
                }
            }

            File.Delete(path);
        }
    }
}

/// <summary>
/// Accepts connections on a diagnostics endpoint until disposed.
/// </summary>
internal abstract class DiagnosticsListener : IAsyncDisposable
{
    public abstract string Endpoint { get; }

    public abstract Task<Stream> AcceptAsync(CancellationToken cancellationToken);

    public abstract ValueTask DisposeAsync();
}
