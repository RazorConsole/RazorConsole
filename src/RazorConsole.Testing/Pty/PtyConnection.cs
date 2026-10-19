// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Testing.Pty;

/// <summary>
/// A child process attached to a pseudoterminal: ConPTY on Windows, a POSIX PTY elsewhere.
/// </summary>
/// <remarks>
/// <see cref="Read"/> is called from one reader thread and <see cref="Write"/> from one writer at a time.
/// Shutdown order: <see cref="Kill"/>, <see cref="StopReading"/>, wait for the reader to return 0, then <see cref="Dispose(bool)"/>.
/// </remarks>
internal abstract class PtyConnection : IDisposable
{
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public abstract int ProcessId { get; }

    /// <summary>
    /// Gets a task that completes with the exit code when the process exits.
    /// </summary>
    public Task<int> Exited => _exited.Task;

    public static PtyConnection Start(PtyStartInfo startInfo)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return WindowsPtyConnection.StartProcess(startInfo);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return UnixPtyConnection.StartProcess(startInfo);
        }

        throw PtyPlatform.CreateNotSupportedException();
    }

    /// <summary>
    /// Reads output into <paramref name="buffer"/>. Blocks until output is available;
    /// returns 0 when the output has ended or <see cref="StopReading"/> was called.
    /// </summary>
    public abstract int Read(Span<byte> buffer);

    /// <summary>
    /// Writes all of <paramref name="data"/> to the terminal input. Blocks while the input buffer is full.
    /// </summary>
    public abstract void Write(ReadOnlySpan<byte> data);

    public abstract void Resize(int width, int height);

    /// <summary>
    /// Kills the process and the processes it started.
    /// </summary>
    public abstract void Kill();

    /// <summary>
    /// Makes a pending or later <see cref="Read"/> return 0.
    /// </summary>
    public abstract void StopReading();

    /// <summary>
    /// Releases the terminal and process handles. When <paramref name="readerStopped"/> is false the read handle is
    /// leaked instead of closed, so a stuck reader can never read from a reused handle of another terminal.
    /// </summary>
    public abstract void Dispose(bool readerStopped);

    public void Dispose() => Dispose(readerStopped: true);

    protected void SetExited(int exitCode) => _exited.TrySetResult(exitCode);

    protected void SetExitUnknown(Exception exception) => _exited.TrySetException(exception);
}

/// <summary>
/// What to start in a pseudoterminal.
/// </summary>
internal sealed record PtyStartInfo(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int Width,
    int Height);
