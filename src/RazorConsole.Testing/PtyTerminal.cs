// Copyright (c) RazorConsole. All rights reserved.

using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using RazorConsole.Core.Input;
using RazorConsole.Testing.Pty;
using RazorConsole.Testing.Vt;

namespace RazorConsole.Testing;

/// <summary>
/// Runs a console application in a real pseudoterminal (ConPTY on Windows, a POSIX PTY on Linux and macOS)
/// and emulates the terminal: input is sent as the bytes a terminal would send, and the application's output
/// is interpreted into frames of cells.
/// </summary>
/// <remarks>
/// <para>
/// Several instances can run concurrently in one process; each has its own pseudoterminal, process, emulator and frames.
/// </para>
/// <para>
/// One instance is thread-safe. Input methods and <see cref="Resize(int, int)"/> run one at a time, in call order.
/// Input methods complete when the bytes are written to the terminal, not when the application has handled them:
/// follow them with <see cref="WaitUntilAsync"/> or <see cref="WaitForTextAsync"/>.
/// <see cref="CurrentFrame"/> and <see cref="Frames"/> return immutable snapshots and can be read from any thread.
/// </para>
/// <para>
/// On Windows the application's output passes through ConPTY, which renders it again: <see cref="RawOutput"/> is
/// ConPTY's output, not the application's own bytes. Prefer assertions on frame state.
/// </para>
/// </remarks>
public sealed class PtyTerminal : IAsyncDisposable
{
    private const int MaximumFrameHistory = 50;
    private const int ReadBufferSize = 16 * 1024;
    private static readonly TimeSpan ExitDrainQuietPeriod = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ExitDrainLimit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitStatusWaitLimit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DisposeLimit = TimeSpan.FromSeconds(5);

    // End a synchronized update the app left open, so a crashed or stalled app cannot freeze frame capture.
    internal static readonly TimeSpan SynchronizedUpdateTimeout = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly object _responseSync = new();
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly PtyConnection _connection;
    private readonly VtEmulator _emulator;
    private readonly Decoder _rawDecoder = new UTF8Encoding(false).GetDecoder();
    private readonly StringBuilder _rawOutput = new();
    private readonly List<string> _pendingResponses = [];
    private readonly TaskCompletionSource _readerCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _defaultTimeout;
    private readonly string _commandDescription;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _synchronizedUpdateTimer;

    private volatile PtyTerminalFrame _currentFrame;
    private volatile PtyTerminalFrame[] _frames;
    private volatile Exception? _readerError;
    private Task _responses = Task.CompletedTask;
    private long _frameNumber;
    private long _lastOutputTimestamp;
    private volatile int _width;
    private volatile int _height;
    private int _disposed;

    // Guarded by _sync: the synchronized update being held back, and when it started.
    private long _heldUpdate;
    private long _heldUpdateStart;

    private PtyTerminal(PtyConnection connection, PtyTerminalOptions options, string commandDescription, TimeProvider timeProvider)
    {
        _connection = connection;
        _defaultTimeout = options.DefaultTimeout;
        _commandDescription = commandDescription;
        _timeProvider = timeProvider;
        _synchronizedUpdateTimer = timeProvider.CreateTimer(
            static state => ((PtyTerminal)state!).OnSynchronizedUpdateTimeout(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _width = options.Width;
        _height = options.Height;
        _emulator = new VtEmulator(options.Width, options.Height, options.RespondToTerminalQueries ? _pendingResponses.Add : null);
        _currentFrame = _emulator.CreateFrame(0, DateTimeOffset.UtcNow);
        _frames = [_currentFrame];
        _lastOutputTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Gets a value indicating whether pseudoterminals are supported on this system:
    /// Windows 10 version 1809 or later, Linux, or macOS.
    /// </summary>
    public static bool IsSupported => PtyPlatform.IsSupported;

    /// <summary>
    /// Gets the process ID of the application.
    /// </summary>
    public int ProcessId => _connection.ProcessId;

    internal PtyConnection Connection => _connection;

    /// <summary>
    /// Gets the terminal width in columns.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Gets the terminal height in rows.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Gets the most recent frame. A frame is captured after each chunk of output, except inside a synchronized
    /// update (<c>CSI ? 2026 h</c> … <c>l</c>), where it is captured when the update ends. Like common terminals,
    /// an update left open for one second is ended and captured anyway, and a frame is always captured when the
    /// output ends.
    /// </summary>
    public PtyTerminalFrame CurrentFrame => _currentFrame;

    /// <summary>
    /// Gets up to the 50 most recent distinct frames, oldest first.
    /// </summary>
    public IReadOnlyList<PtyTerminalFrame> Frames => _frames;

    /// <summary>
    /// Gets all output read from the terminal so far, decoded as UTF-8.
    /// On Windows this is ConPTY's rendering of the application's output, not the application's own bytes.
    /// </summary>
    public string RawOutput
    {
        get
        {
            lock (_sync)
            {
                return _rawOutput.ToString();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the application has exited.
    /// </summary>
    public bool HasExited => _connection.Exited.IsCompleted;

    /// <summary>
    /// Gets the application's exit code, or <see langword="null"/> while it runs.
    /// On Linux and macOS a process killed by a signal reports 128 plus the signal number.
    /// </summary>
    public int? ExitCode => _connection.Exited.IsCompletedSuccessfully ? _connection.Exited.Result : null;

    /// <summary>
    /// Starts a process in a new pseudoterminal.
    /// </summary>
    /// <param name="options">What to start, and the terminal settings.</param>
    /// <param name="cancellationToken">A token that cancels the start.</param>
    /// <returns>The running terminal. Dispose it to kill the process and release the terminal.</returns>
    /// <exception cref="PlatformNotSupportedException">Pseudoterminals are not supported on this system (see <see cref="IsSupported"/>).</exception>
    public static Task<PtyTerminal> StartAsync(PtyTerminalOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.FileName, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Width, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Height, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Width, short.MaxValue, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Height, short.MaxValue, nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupported)
        {
            throw PtyPlatform.CreateNotSupportedException();
        }

        var startInfo = new PtyStartInfo(
            options.FileName,
            options.Arguments.ToArray(),
            options.WorkingDirectory,
            BuildEnvironment(options),
            options.Width,
            options.Height);

        var connection = PtyConnection.Start(startInfo);
        return Task.FromResult(Start(connection, options, TimeProvider.System));
    }

    internal static PtyTerminal Start(PtyConnection connection, PtyTerminalOptions options, TimeProvider timeProvider)
    {
        var terminal = new PtyTerminal(connection, options, string.Join(' ', options.Arguments.Prepend(options.FileName)), timeProvider);
        terminal.Run();
        return terminal;
    }

    /// <summary>
    /// Starts <c>dotnet <paramref name="assemblyPath"/></c> in a new pseudoterminal, with the working directory set to
    /// the assembly's directory. The host is the one running the tests: <c>DOTNET_HOST_PATH</c>, the current
    /// <c>dotnet</c> process, the <c>dotnet</c> next to the running runtime, or <c>dotnet</c> on <c>PATH</c>.
    /// </summary>
    /// <param name="assemblyPath">The application's <c>.dll</c>.</param>
    /// <param name="width">The terminal width in columns.</param>
    /// <param name="height">The terminal height in rows.</param>
    /// <param name="arguments">Arguments passed to the application.</param>
    /// <param name="cancellationToken">A token that cancels the start.</param>
    /// <returns>The running terminal. Dispose it to kill the process and release the terminal.</returns>
    /// <exception cref="PlatformNotSupportedException">Pseudoterminals are not supported on this system (see <see cref="IsSupported"/>).</exception>
    public static Task<PtyTerminal> StartDotnetAsync(
        string assemblyPath,
        int width = 80,
        int height = 24,
        IEnumerable<string>? arguments = null,
        CancellationToken cancellationToken = default)
        => StartAsync(CreateDotnetOptions(assemblyPath, width, height, arguments), cancellationToken);

    /// <summary>
    /// Sends one key press, encoded the way an xterm-compatible terminal sends it.
    /// </summary>
    /// <param name="key">The key, including its character and modifiers.</param>
    /// <param name="cancellationToken">A token that cancels waiting for the input to be written.</param>
    public Task SendKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken = default)
        => WriteAsync(() => VtInputEncoder.EncodeKey(key, ReadMode(static emulator => emulator.ApplicationCursorKeys)), cancellationToken);

    /// <summary>
    /// Sends one key press, encoded the way an xterm-compatible terminal sends it.
    /// For example, Ctrl+C is <c>SendKeyAsync(ConsoleKey.C, '\x03', ConsoleModifiers.Control)</c>.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="keyChar">The character the key produces, or <c>'\0'</c> for none.</param>
    /// <param name="modifiers">The modifier keys held down.</param>
    /// <param name="cancellationToken">A token that cancels waiting for the input to be written.</param>
    public Task SendKeyAsync(
        ConsoleKey key,
        char keyChar = '\0',
        ConsoleModifiers modifiers = 0,
        CancellationToken cancellationToken = default)
        => SendKeyAsync(
            new ConsoleKeyInfo(
                keyChar,
                key,
                (modifiers & ConsoleModifiers.Shift) != 0,
                (modifiers & ConsoleModifiers.Alt) != 0,
                (modifiers & ConsoleModifiers.Control) != 0),
            cancellationToken);

    /// <summary>
    /// Types <paramref name="text"/> as keyboard input. Line breaks are sent as Enter (<c>\r</c>);
    /// other characters, including control characters such as <c>\x03</c> (Ctrl+C) and <c>\x7f</c> (Backspace), are sent as-is.
    /// </summary>
    /// <param name="text">The text to type. Use <see cref="SendTerminalInputAsync"/> for escape sequences.</param>
    /// <param name="cancellationToken">A token that cancels waiting for the input to be written.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains an ESC character.</exception>
    public Task TypeAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\u001b', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The text contains an ESC character. Use {nameof(SendTerminalInputAsync)} to send escape sequences.",
                nameof(text));
        }

        return WriteAsync(text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r'), cancellationToken);
    }

    /// <summary>
    /// Pastes <paramref name="text"/> the way a terminal does: line breaks become <c>\r</c>, and the text is wrapped in
    /// <c>CSI 200 ~</c> … <c>CSI 201 ~</c> when the application has enabled bracketed paste.
    /// </summary>
    /// <param name="text">The text to paste.</param>
    /// <param name="cancellationToken">A token that cancels waiting for the input to be written.</param>
    public Task PasteAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WriteAsync(() => VtInputEncoder.EncodePaste(text, ReadMode(static emulator => emulator.BracketedPaste)), cancellationToken);
    }

    /// <summary>
    /// Sends raw terminal input, exactly as given.
    /// </summary>
    /// <param name="data">The raw input, for example <c>"\u001b[A"</c> for the Up arrow.</param>
    /// <param name="cancellationToken">A token that cancels waiting for the input to be written.</param>
    public Task SendTerminalInputAsync(string data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return WriteAsync(data, cancellationToken);
    }

    /// <summary>
    /// Sends one mouse event as an SGR (<c>CSI ? 1006</c>) mouse report.
    /// </summary>
    /// <param name="input">The mouse event, in zero-based terminal-cell coordinates.</param>
    /// <param name="cancellationToken">A token that cancels waiting for the input to be written.</param>
    public Task SendMouseAsync(TerminalMouseEvent input, CancellationToken cancellationToken = default)
        => WriteAsync(VtInputEncoder.EncodeMouse(input), cancellationToken);

    /// <summary>
    /// Clicks the left mouse button at a cell: an SGR press report followed by a release report, in one write.
    /// </summary>
    /// <param name="x">The zero-based column.</param>
    /// <param name="y">The zero-based row.</param>
    /// <param name="cancellationToken">A token that cancels waiting for the input to be written.</param>
    public Task ClickAsync(int x, int y, CancellationToken cancellationToken = default)
        => WriteAsync(
            VtInputEncoder.EncodeMouse(new TerminalMouseEvent(TerminalMouseKind.Down, x, y))
                + VtInputEncoder.EncodeMouse(new TerminalMouseEvent(TerminalMouseKind.Up, x, y)),
            cancellationToken);

    /// <summary>
    /// Resizes the terminal. The application is notified the way a real terminal notifies it
    /// (<c>SIGWINCH</c> on Linux and macOS, a buffer-size event through ConPTY on Windows).
    /// The application redraws asynchronously: wait for content that depends on the new size.
    /// </summary>
    /// <param name="width">The new width in columns.</param>
    /// <param name="height">The new height in rows.</param>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, short.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, short.MaxValue);
        ThrowIfDisposed();

        _inputGate.Wait();
        try
        {
            ThrowIfDisposed();
            lock (_sync)
            {
                _emulator.Resize(width, height);
                _width = width;
                _height = height;
                CaptureLocked();
            }

            // Not under _sync: the reader must keep draining output while the terminal resizes.
            _connection.Resize(width, height);
        }
        finally
        {
            _inputGate.Release();
        }
    }

    /// <summary>
    /// Waits until the current frame satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <param name="predicate">The condition to wait for.</param>
    /// <param name="timeout">The maximum time to wait. Defaults to <see cref="PtyTerminalOptions.DefaultTimeout"/>.</param>
    /// <param name="description">A description of the awaited state, used in failure messages.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>The first frame that satisfied <paramref name="predicate"/>.</returns>
    /// <exception cref="TerminalTimeoutException">The condition was not met within the timeout.</exception>
    /// <exception cref="InvalidOperationException">The terminal output ended (the application exited) without the condition being met.</exception>
    public async Task<PtyTerminalFrame> WaitUntilAsync(
        Func<PtyTerminalFrame, bool> predicate,
        TimeSpan? timeout = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ThrowIfDisposed();

        var limit = timeout ?? _defaultTimeout;
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read the end-of-output state first: a frame read afterwards includes everything up to it.
            var ended = _readerCompleted.Task.IsCompleted;
            var frame = _currentFrame;
            if (predicate(frame))
            {
                return frame;
            }

            if (ended)
            {
                // The output normally ends after the exit is known; if not, give the exit status a moment to show up in the diagnostics.
                await WaitQuietlyAsync(_connection.Exited, ExitStatusWaitLimit).ConfigureAwait(false);
                var diagnostics = DumpDiagnostics();
                throw new InvalidOperationException(
                    $"The terminal output ended before {description ?? "the requested terminal state"} was reached.{Environment.NewLine}{diagnostics}");
            }

            if (stopwatch.Elapsed >= limit)
            {
                var diagnostics = DumpDiagnostics();
                throw new TerminalTimeoutException(
                    $"Timed out after {limit} waiting for {description ?? "the requested terminal state"}.{Environment.NewLine}{diagnostics}",
                    diagnostics);
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until the screen text contains <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text to wait for (ordinal comparison).</param>
    /// <param name="timeout">The maximum time to wait. Defaults to <see cref="PtyTerminalOptions.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>The first frame that contained <paramref name="text"/>.</returns>
    /// <exception cref="TerminalTimeoutException">The text did not appear within the timeout.</exception>
    /// <exception cref="InvalidOperationException">The terminal output ended (the application exited) without the text appearing.</exception>
    public Task<PtyTerminalFrame> WaitForTextAsync(string text, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WaitUntilAsync(frame => frame.ContainsText(text), timeout, $"text '{text}'", cancellationToken);
    }

    /// <summary>
    /// Waits until the terminal has produced no output for <paramref name="quietPeriod"/>, or the output has ended.
    /// </summary>
    /// <remarks>
    /// This only means that no output arrived for <paramref name="quietPeriod"/>. It does not mean the application has
    /// finished rendering or handling input: a slow application can pause longer than that and draw again. Prefer
    /// <see cref="WaitUntilAsync"/> with a condition on the expected content.
    /// </remarks>
    /// <param name="quietPeriod">How long the output must be quiet.</param>
    /// <param name="timeout">The maximum time to wait. Defaults to <see cref="PtyTerminalOptions.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>The current frame once the output is quiet.</returns>
    /// <exception cref="TerminalTimeoutException">The output did not stay quiet for <paramref name="quietPeriod"/> within the timeout.</exception>
    public async Task<PtyTerminalFrame> WaitForOutputIdleAsync(
        TimeSpan quietPeriod,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quietPeriod, TimeSpan.Zero);
        ThrowIfDisposed();

        var limit = timeout ?? _defaultTimeout;
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_readerCompleted.Task.IsCompleted)
            {
                return _currentFrame;
            }

            var quiet = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastOutputTimestamp));
            if (quiet >= quietPeriod)
            {
                return _currentFrame;
            }

            if (stopwatch.Elapsed >= limit)
            {
                var diagnostics = DumpDiagnostics();
                throw new TerminalTimeoutException(
                    $"Timed out after {limit} waiting for the output to be quiet for {quietPeriod}.{Environment.NewLine}{diagnostics}",
                    diagnostics);
            }

            var remaining = quietPeriod - quiet;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(10) ? TimeSpan.FromMilliseconds(10) : remaining, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until the application exits.
    /// </summary>
    /// <param name="timeout">The maximum time to wait. Defaults to <see cref="PtyTerminalOptions.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>The exit code. On Linux and macOS a process killed by a signal reports 128 plus the signal number.</returns>
    /// <exception cref="TerminalTimeoutException">The application did not exit within the timeout.</exception>
    /// <exception cref="InvalidOperationException">The exit code could not be determined.</exception>
    public async Task<int> WaitForExitAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? _defaultTimeout;
        try
        {
            return await _connection.Exited.WaitAsync(limit, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var diagnostics = DumpDiagnostics();
            throw new TerminalTimeoutException(
                $"Timed out after {limit} waiting for process {ProcessId} to exit.{Environment.NewLine}{diagnostics}",
                diagnostics);
        }
    }

    /// <summary>
    /// Formats the terminal state for failure messages: the command, process and exit state, terminal modes,
    /// the most recent frames, and the end of the raw output with control characters escaped.
    /// </summary>
    /// <param name="frameCount">How many of the most recent frames to include.</param>
    public string DumpDiagnostics(int frameCount = 3)
    {
        var frames = _frames;
        var exited = _connection.Exited;
        string rawTail;
        int rawLength;
        lock (_sync)
        {
            rawLength = _rawOutput.Length;
            var start = Math.Max(0, rawLength - 2000);
            rawTail = _rawOutput.ToString(start, rawLength - start);
        }

        var builder = new StringBuilder();
        builder.Append("Command: ").AppendLine(_commandDescription);
        builder.Append("Process ").Append(ProcessId).Append(": ").AppendLine(
            exited.IsCompletedSuccessfully ? $"exited with code {exited.Result}"
            : exited.IsFaulted ? $"exited, exit code unknown ({exited.Exception?.InnerException?.Message})"
            : "running");
        builder.Append("Output: ").Append(rawLength).Append(" chars, ")
            .Append(_readerCompleted.Task.IsCompleted ? "ended" : "open")
            .Append(", last output ").Append((int)Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastOutputTimestamp)).TotalMilliseconds)
            .AppendLine(" ms ago");
        builder.Append("Captured frames: ").Append(Interlocked.Read(ref _frameNumber) + 1).AppendLine();
        if (_readerError is { } readerError)
        {
            builder.Append("Reader error: ").AppendLine(readerError.ToString());
        }

        foreach (var frame in frames.TakeLast(Math.Max(1, frameCount)))
        {
            builder.AppendLine(frame.ToDiagnosticString());
            builder.AppendLine(new string('-', Math.Max(1, Math.Min(frame.Width, 80))));
        }

        builder.Append("Raw output tail: ").AppendLine(Escape(rawTail));
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Kills the application and every process it started, closes the terminal, and stops reading output.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _connection.Kill();
        _connection.StopReading();
        var readerStopped = await WaitQuietlyAsync(_readerCompleted.Task, DisposeLimit).ConfigureAwait(false);
        _synchronizedUpdateTimer.Dispose();
        await WaitQuietlyAsync(_connection.Exited, DisposeLimit).ConfigureAwait(false);

        // A write still in progress holds the gate; never close a descriptor it may still use.
        var inputIdle = await _inputGate.WaitAsync(DisposeLimit).ConfigureAwait(false);
        try
        {
            _connection.Dispose(readerStopped && inputIdle);
        }
        finally
        {
            if (inputIdle)
            {
                _inputGate.Release();
            }
        }
    }

    internal static PtyTerminalOptions CreateDotnetOptions(string assemblyPath, int width, int height, IEnumerable<string>? arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
        var fullPath = Path.GetFullPath(assemblyPath);
        var options = new PtyTerminalOptions
        {
            FileName = ResolveDotnetHost(),
            WorkingDirectory = Path.GetDirectoryName(fullPath),
            Width = width,
            Height = height,
        };
        options.Arguments.Add(fullPath);
        foreach (var argument in arguments ?? [])
        {
            options.Arguments.Add(argument);
        }

        return options;
    }

    internal static string ResolveDotnetHost()
    {
        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var hostPath = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }

        var processPath = System.Environment.ProcessPath;
        if (processPath is not null && string.Equals(Path.GetFileName(processPath), executable, StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        // <root>/shared/Microsoft.NETCore.App/<version>/
        var runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        if (!string.IsNullOrEmpty(runtimeDirectory))
        {
            var candidate = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..", executable));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }

    internal static Dictionary<string, string> BuildEnvironment(PtyTerminalOptions options)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var environment = new Dictionary<string, string>(comparer);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                environment[name] = value;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            environment["TERM"] = options.TerminalType;
        }

        foreach (var (name, value) in options.Environment)
        {
            if (value is null)
            {
                environment.Remove(name);
            }
            else
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    private static async Task<bool> WaitQuietlyAsync(Task task, TimeSpan limit)
    {
        try
        {
            await task.WaitAsync(limit).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // The exit code is unknown, but the wait itself is over.
            return true;
        }
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 64);
        foreach (var character in text)
        {
            switch (character)
            {
                case '\u001b':
                    builder.Append("\\e");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case < ' ' or '\u007f':
                    builder.Append("\\x").Append(((int)character).ToString("x2", CultureInfo.InvariantCulture));
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private void Run()
    {
        var reader = new Thread(ReadOutput) { IsBackground = true, Name = $"PtyTerminal reader {ProcessId}" };
        reader.Start();
        _ = _connection.Exited.ContinueWith(
            static (_, state) => ((PtyTerminal)state!).EndOutputAfterExitAsync(),
            this,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
    }

    private void ReadOutput()
    {
        var buffer = new byte[ReadBufferSize];
        var chars = new char[ReadBufferSize + 4];
        try
        {
            while (true)
            {
                var count = _connection.Read(buffer);
                if (count == 0)
                {
                    break;
                }

                string[]? responses = null;
                lock (_sync)
                {
                    var bytes = buffer.AsSpan(0, count);
                    _emulator.Feed(bytes);
                    var charCount = _rawDecoder.GetChars(bytes, chars, flush: false);
                    _rawOutput.Append(chars, 0, charCount);
                    CaptureAfterOutputLocked();
                    if (_pendingResponses.Count > 0)
                    {
                        responses = [.. _pendingResponses];
                        _pendingResponses.Clear();
                    }
                }

                Interlocked.Exchange(ref _lastOutputTimestamp, Stopwatch.GetTimestamp());
                if (responses is not null)
                {
                    QueueResponses(responses);
                }
            }
        }
        catch (Exception exception)
        {
            // An exception on this thread would end the test process; report it through the waits instead.
            _readerError = exception;
        }
        finally
        {
            lock (_sync)
            {
                var charCount = _rawDecoder.GetChars([], chars, flush: true);
                _rawOutput.Append(chars, 0, charCount);

                // The output has ended: capture what is on screen even inside an unfinished synchronized update.
                _heldUpdate = 0;
                _synchronizedUpdateTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                CaptureLocked();
            }

            _readerCompleted.TrySetResult();
        }
    }

    private void CaptureAfterOutputLocked()
    {
        if (!_emulator.SynchronizedOutput)
        {
            if (_heldUpdate != 0)
            {
                _heldUpdate = 0;
                _synchronizedUpdateTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            CaptureLocked();
            return;
        }

        if (_heldUpdate != _emulator.SynchronizedUpdateCount)
        {
            _heldUpdate = _emulator.SynchronizedUpdateCount;
            _heldUpdateStart = _timeProvider.GetTimestamp();
            _synchronizedUpdateTimer.Change(SynchronizedUpdateTimeout, Timeout.InfiniteTimeSpan);
        }
        else if (_timeProvider.GetElapsedTime(_heldUpdateStart) >= SynchronizedUpdateTimeout)
        {
            EndSynchronizedUpdateLocked();
        }
    }

    // Also fires when the application goes quiet with an update still open.
    private void OnSynchronizedUpdateTimeout()
    {
        lock (_sync)
        {
            if (_heldUpdate != 0
                && _heldUpdate == _emulator.SynchronizedUpdateCount
                && _emulator.SynchronizedOutput
                && _timeProvider.GetElapsedTime(_heldUpdateStart) >= SynchronizedUpdateTimeout)
            {
                EndSynchronizedUpdateLocked();
            }
        }
    }

    // End the update instead of only capturing once: later output is captured as usual until the application
    // starts a new synchronized update.
    private void EndSynchronizedUpdateLocked()
    {
        _emulator.EndSynchronizedOutput();
        _heldUpdate = 0;
        _synchronizedUpdateTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        CaptureLocked();
    }

    // ConPTY keeps its output open until the pseudoconsole is closed, even after the process exits;
    // a grandchild can keep a POSIX PTY open the same way. Drain what is left, then end the output.
    private async Task EndOutputAfterExitAsync()
    {
        // Quiet is counted from the exit at the earliest: output that has not been read yet is not silence.
        var exitTimestamp = Stopwatch.GetTimestamp();
        var stopwatch = Stopwatch.StartNew();
        while (!_readerCompleted.Task.IsCompleted
            && stopwatch.Elapsed < ExitDrainLimit
            && Stopwatch.GetElapsedTime(Math.Max(exitTimestamp, Interlocked.Read(ref _lastOutputTimestamp))) < ExitDrainQuietPeriod)
        {
            await Task.Delay(25).ConfigureAwait(false);
        }

        if (Volatile.Read(ref _disposed) == 0)
        {
            _connection.StopReading();
        }
    }

    private void CaptureLocked()
    {
        var frame = _emulator.CreateFrame(_frameNumber + 1, DateTimeOffset.UtcNow);
        if (frame.HasSameState(_currentFrame))
        {
            return;
        }

        Interlocked.Increment(ref _frameNumber);
        var frames = _frames;
        var next = frames.Length < MaximumFrameHistory ? new PtyTerminalFrame[frames.Length + 1] : new PtyTerminalFrame[MaximumFrameHistory];
        Array.Copy(frames, frames.Length - (next.Length - 1), next, 0, next.Length - 1);
        next[^1] = frame;
        _frames = next;
        _currentFrame = frame;
    }

    private void QueueResponses(string[] responses)
    {
        var data = string.Concat(responses);
        lock (_responseSync)
        {
            // Chained so responses reach the application in the order the queries arrived.
            _responses = _responses.ContinueWith(
                static (_, state) =>
                {
                    var (terminal, text) = ((PtyTerminal, string))state!;
                    return terminal.WriteResponseAsync(text);
                },
                (this, data),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    private async Task WriteResponseAsync(string data)
    {
        try
        {
            await WriteAsync(data, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The terminal is closing; the application no longer needs the answer.
        }
    }

    private Task WriteAsync(string data, CancellationToken cancellationToken)
        => WriteAsync(() => data, cancellationToken);

    private async Task WriteAsync(Func<string> encode, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task write;
        try
        {
            ThrowIfDisposed();

            // Encoded under the gate, so mode-dependent encodings see the state after earlier input.
            var bytes = Encoding.UTF8.GetBytes(encode());
            write = Task.Factory.StartNew(
                static state =>
                {
                    var (connection, data) = ((PtyConnection, byte[]))state!;
                    connection.Write(data);
                },
                (_connection, bytes),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }
        catch
        {
            _inputGate.Release();
            throw;
        }

        // The gate opens when the write has finished, even if the caller stops waiting for it,
        // so writes never interleave.
        _ = write.ContinueWith(
            static (_, gate) => ((SemaphoreSlim)gate!).Release(),
            _inputGate,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await write.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool ReadMode(Func<VtEmulator, bool> mode)
    {
        lock (_sync)
        {
            return mode(_emulator);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
