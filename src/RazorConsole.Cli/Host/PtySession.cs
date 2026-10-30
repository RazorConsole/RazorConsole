// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RazorConsole.Cli.Operations;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Input;
using RazorConsole.Testing;
using RazorConsole.Testing.Vt;

namespace RazorConsole.Cli.Host;

/// <summary>
/// One application running in a pseudoterminal inside the session host.
/// </summary>
/// <remarks>
/// Input and screen reads hold the session's operation lock, so they are serialized and a screen read sees the
/// effect of earlier input as far as the app has drawn it. Nothing else takes the lock; later waits run concurrently.
/// The raw output is kept in an <see cref="OutputRing"/> fed by the terminal's raw output observer.
/// </remarks>
internal sealed class PtySession : HostSession
{
    public const string SessionKind = "pty";

    private const int TerminalRawOutputCapacity = 64 * 1024;
    private const int OutputTailLines = 40;
    private const string CliHomeVariable = "RAZORCONSOLE_CLI_HOME";
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly string _ledgerFile;
    private readonly string _stateDirectory;
    private readonly AppLayer _app;
    private PtyTerminal _terminal;
    private OutputRing _output;
    private DateTimeOffset _startedAt;
    private string _token;
    private long _firstFrameAt;
    private int _disposed;

    private PtySession(string name, StartParams spec, string ledgerFile, string stateDirectory, string token, PtyTerminal terminal, OutputRing output, string command)
        : base(name)
    {
        Spec = spec;
        Command = command;
        _ledgerFile = ledgerFile;
        _stateDirectory = stateDirectory;
        _token = token;
        _terminal = terminal;
        _output = output;
        _startedAt = DateTimeOffset.UtcNow;
        _app = new AppLayer(
            () => AppPairing.Locate(_stateDirectory, _token, _terminal.ProcessId),
            GraceElapsed,
            () => _terminal.HasExited ? ProcessExited() : null);
    }

    public override string Kind => SessionKind;

    public override AppLayer App => _app;

    public override bool HasExited => _terminal.HasExited;

    public override int? ExitCode => _terminal.ExitCode;

    public override long Activity => _output.End;

    /// <summary>
    /// What was started, kept for <c>restart</c>.
    /// </summary>
    public StartParams Spec { get; }

    public string Command { get; }

    public PtyTerminal Terminal => _terminal;

    public override SessionInfo Info => new(
        Name,
        SessionKind,
        _terminal.ProcessId,
        _terminal.Width,
        _terminal.Height,
        Command,
        _startedAt,
        _terminal.HasExited,
        _terminal.ExitCode,
        _app.Describe());

    public override PtySession RequirePty(string verb) => this;

    public override JsonElement AdjustAppResult(string method, JsonElement result)
    {
        if (method != DiagnosticsAppMethods.InspectAt
            || JsonSerializer.Deserialize(result, DiagnosticsJsonContext.Default.InspectAtResult) is not { } at)
        {
            return result;
        }

        var frame = _terminal.CurrentFrame;
        var cell = at.X >= 0 && at.Y >= 0 && at.X < frame.Width && at.Y < frame.Height
            ? DiagnosticsScreenReader.ToScreenCell(frame[at.X, at.Y])
            : null;
        return JsonSerializer.SerializeToElement(at with { Cell = cell }, DiagnosticsJsonContext.Default.InspectAtResult);
    }

    /// <summary>
    /// Starts the target of <paramref name="spec"/>, records it in the ledger and, unless
    /// <see cref="StartParams.NoWait"/>, waits for its first frame. The app is told the session's token and the
    /// host's state directory, so its diagnostics endpoint can be paired with the session.
    /// </summary>
    public static async Task<PtySession> StartAsync(string name, StartParams spec, string ledgerFile, string stateDirectory, CancellationToken cancellationToken)
    {
        var token = AppPairing.NewToken();
        var (options, command) = await CreateOptionsAsync(spec, stateDirectory, token, cancellationToken).ConfigureAwait(false);
        var output = new OutputRing();
        options.RawOutputObserver = output.Append;
        options.RawOutputCapacity = TerminalRawOutputCapacity;

        var terminal = await StartTerminalAsync(options, cancellationToken).ConfigureAwait(false);
        var session = new PtySession(name, spec, ledgerFile, stateDirectory, token, terminal, output, command);
        try
        {
            session.WriteLedger();
            await session.WaitForFirstFrameAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public override async Task<StopResult> StopAsync(TimeSpan timeout)
    {
        var killed = false;
        if (!_terminal.HasExited)
        {
            try
            {
                await _terminal.SendTerminalInputAsync("\u0003").ConfigureAwait(false);
                await _terminal.WaitForExitAsync(timeout).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TerminalTimeoutException or IOException or InvalidOperationException or ObjectDisposedException)
            {
                killed = !_terminal.HasExited;
            }
        }

        var exitCode = _terminal.ExitCode;
        await DisposeAsync().ConfigureAwait(false);
        if (killed)
        {
            exitCode = _terminal.ExitCode;
        }

        return new StopResult(exitCode, killed);
    }

    /// <summary>
    /// Kills the process and starts the same target again under the same name.
    /// </summary>
    public async Task RestartAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _terminal.DisposeAsync().ConfigureAwait(false);
            await _app.ResetAsync().ConfigureAwait(false);
            var token = AppPairing.NewToken();
            var (options, _) = await CreateOptionsAsync(Spec with { TimeoutMs = (long)timeout.TotalMilliseconds }, _stateDirectory, token, cancellationToken).ConfigureAwait(false);
            var output = new OutputRing();
            options.RawOutputObserver = output.Append;
            options.RawOutputCapacity = TerminalRawOutputCapacity;
            options.Width = _terminal.Width;
            options.Height = _terminal.Height;
            _terminal = await StartTerminalAsync(options, cancellationToken).ConfigureAwait(false);
            _output = output;
            _startedAt = DateTimeOffset.UtcNow;
            _token = token;
            Interlocked.Exchange(ref _firstFrameAt, 0);
            WriteLedger();
        }
        finally
        {
            _operations.Release();
        }

        await WaitForFirstFrameAsync(cancellationToken, timeout).ConfigureAwait(false);
    }

    public Task<ResizeResult> ResizeAsync(int width, int height, CancellationToken cancellationToken)
        => WithLockAsync(
            () =>
            {
                _terminal.Resize(width, height);
                return Task.FromResult(new ResizeResult(width, height));
            },
            cancellationToken);

    public override Task<SentResult> KeysAsync(IReadOnlyList<KeyPress> keys, CancellationToken cancellationToken)
        => InputAsync(
            async () =>
            {
                foreach (var key in keys)
                {
                    await _terminal.SendKeyAsync(key.ToConsoleKeyInfo(), cancellationToken).ConfigureAwait(false);
                }

                return new SentResult(keys.Count);
            },
            cancellationToken);

    public override Task<SentResult> TypeAsync(string text, CancellationToken cancellationToken)
        => InputAsync(
            async () =>
            {
                if (text.Contains('\u001b', StringComparison.Ordinal))
                {
                    throw InvalidParams("type text must not contain ESC; use raw for escape sequences.");
                }

                await _terminal.TypeAsync(text, cancellationToken).ConfigureAwait(false);
                return new SentResult(text.Length);
            },
            cancellationToken);

    public override Task<PasteResult> PasteAsync(string text, CancellationToken cancellationToken)
        => InputAsync(
            async () =>
            {
                var bracketed = _terminal.CurrentFrame.BracketedPaste;
                await _terminal.PasteAsync(text, cancellationToken).ConfigureAwait(false);
                return new PasteResult(bracketed);
            },
            cancellationToken);

    public override Task<RawResult> RawAsync(string data, CancellationToken cancellationToken)
        => InputAsync(
            async () =>
            {
                await _terminal.SendTerminalInputAsync(data, cancellationToken).ConfigureAwait(false);
                return new RawResult(Encoding.UTF8.GetByteCount(data));
            },
            cancellationToken);

    protected override Task<ClickResult> ClickCoreAsync(ClickParams click, CancellationToken cancellationToken)
        => InputAsync(
            async () =>
            {
                ClickTarget? target = null;
                int x, y;
                if (click.Text is { } text)
                {
                    (x, y, var width) = FindText(_terminal.CurrentFrame, text, click.Nth);
                    target = new ClickTarget(text, click.Nth, width);
                }
                else
                {
                    x = click.X ?? throw InvalidParams("click needs x and y, or text.");
                    y = click.Y ?? throw InvalidParams("click needs x and y, or text.");
                    CheckCell(x, y);
                }

                var once = VtInputEncoder.EncodeMouse(new TerminalMouseEvent(TerminalMouseKind.Down, x, y, click.Button))
                    + VtInputEncoder.EncodeMouse(new TerminalMouseEvent(TerminalMouseKind.Up, x, y, click.Button));
                await _terminal.SendTerminalInputAsync(click.Double ? once + once : once, cancellationToken).ConfigureAwait(false);
                return new ClickResult(x, y, target);
            },
            cancellationToken);

    public override Task<MouseResult> MouseAsync(MouseParams mouse, CancellationToken cancellationToken)
        => InputAsync(
            async () =>
            {
                CheckCell(mouse.X, mouse.Y);
                var input = new TerminalMouseEvent(mouse.Kind, mouse.X, mouse.Y, mouse.Button, mouse.Delta);
                await _terminal.SendMouseAsync(input, cancellationToken).ConfigureAwait(false);
                return new MouseResult(mouse.X, mouse.Y);
            },
            cancellationToken);

    public override Task<PtyScreenResult> ScreenAsync(PtyScreenParams parameters, CancellationToken cancellationToken)
        => WithLockAsync(() => Task.FromResult(ReadScreen(_terminal.CurrentFrame, parameters)), cancellationToken);

    // Waits read the frame without the operation lock, so they run alongside input.
    public override Task<ScreenSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        var frame = _terminal.CurrentFrame;
        NoteFirstFrame(frame);
        return Task.FromResult(new ScreenSnapshot(Lines(frame), frame.FrameNumber));
    }

    protected override Task<(int Width, int Height)> ScreenSizeAsync(CancellationToken cancellationToken)
        => Task.FromResult((_terminal.Width, _terminal.Height));

    public FramesResult Frames(int last)
    {
        var frames = _terminal.Frames;
        var count = Math.Min(Math.Max(last, 1), frames.Count);
        var result = new List<FrameInfo>(count);
        for (var i = frames.Count - count; i < frames.Count; i++)
        {
            result.Add(new FrameInfo(frames[i].FrameNumber, frames[i].ObservedAt, Lines(frames[i])));
        }

        return new FramesResult(result);
    }

    /// <summary>
    /// Compares two kept frames line by line: by default the one before the latest with the latest.
    /// </summary>
    public DiffResult Diff(long? from, long? to)
    {
        var frames = _terminal.Frames;
        if (frames.Count == 0)
        {
            return new DiffResult(0, 0, []);
        }

        var after = to is { } toNumber ? FindFrame(frames, toNumber) : frames[^1];
        var before = from is { } fromNumber
            ? FindFrame(frames, fromNumber)
            : frames.LastOrDefault(frame => frame.FrameNumber < after.FrameNumber) ?? after;

        var beforeLines = Lines(before);
        var afterLines = Lines(after);
        var changes = new List<LineChange>();
        for (var line = 0; line < Math.Max(beforeLines.Count, afterLines.Count); line++)
        {
            var old = line < beforeLines.Count ? beforeLines[line] : null;
            var current = line < afterLines.Count ? afterLines[line] : null;
            if (!string.Equals(old, current, StringComparison.Ordinal))
            {
                changes.Add(new LineChange(line, old, current));
            }
        }

        return new DiffResult(before.FrameNumber, after.FrameNumber, changes);
    }

    public OutputResult Output(long? since, int? maxBytes)
    {
        var (offset, next, data) = _output.Read(since, maxBytes ?? OutputRing.MaxReadBytes);
        return new OutputResult(offset, next, data);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _app.DisposeAsync().ConfigureAwait(false);
        await _terminal.DisposeAsync().ConfigureAwait(false);
        try
        {
            File.Delete(_ledgerFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next host's sweep finds the process gone and removes the entry.
        }
    }

    internal static PtyScreenResult ReadScreen(PtyTerminalFrame frame, PtyScreenParams parameters)
    {
        var region = Clip(parameters.Region, frame.Width, frame.Height);
        var lines = new List<string>(region.Height);
        List<IReadOnlyList<ScreenCell>>? cells = parameters.IncludeCells ? new(region.Height) : null;
        for (var y = region.Y; y < region.Y + region.Height; y++)
        {
            var line = new StringBuilder();
            List<ScreenCell>? row = cells is null ? null : new(region.Width);
            for (var x = region.X; x < region.X + region.Width; x++)
            {
                var cell = frame[x, y];
                line.Append(cell.Text);
                row?.Add(DiagnosticsScreenReader.ToScreenCell(cell));
            }

            lines.Add(line.ToString());
            if (row is not null)
            {
                cells!.Add(row);
            }
        }

        return new PtyScreenResult(
            SessionKind,
            region.Width,
            region.Height,
            new ScreenCursor(frame.CursorX, frame.CursorY, frame.CursorVisible),
            frame.ActiveBuffer == TerminalScreenBuffer.Alternate ? "alternate" : "normal",
            MouseTrackingNames(frame.MouseTracking),
            frame.BracketedPaste,
            lines,
            frame.FrameNumber,
            parameters.Region is null ? null : region,
            cells,
            parameters.IncludeAnsi ? AnsiScreenWriter.Write(frame, region.X, region.Y, region.Width, region.Height) : null);
    }

    /// <summary>
    /// Finds the <paramref name="nth"/> occurrence of <paramref name="text"/>, row by row, and returns the middle cell
    /// of it and its width in cells.
    /// </summary>
    internal static (int X, int Y, int Width) FindText(TerminalFrame frame, string text, int nth)
        => FindText(frame.Width, frame.Height, (x, y) => frame[x, y].Text, text, nth);

    private static IReadOnlyList<string> Lines(TerminalFrame frame)
    {
        var lines = new string[frame.Height];
        for (var y = 0; y < frame.Height; y++)
        {
            lines[y] = frame.GetLine(y);
        }

        return lines;
    }

    private static PtyTerminalFrame FindFrame(IReadOnlyList<PtyTerminalFrame> frames, long number)
        => frames.FirstOrDefault(frame => frame.FrameNumber == number)
            ?? throw InvalidParams(
                $"Frame {number} is not kept; frames {frames[0].FrameNumber} to {frames[^1].FrameNumber} are.");

    private static IReadOnlyList<string> MouseTrackingNames(TerminalMouseTracking tracking)
    {
        var names = new List<string>();
        if ((tracking & TerminalMouseTracking.Normal) != 0)
        {
            names.Add("normal");
        }

        if ((tracking & TerminalMouseTracking.ButtonEvent) != 0)
        {
            names.Add("buttonEvent");
        }

        if ((tracking & TerminalMouseTracking.AnyEvent) != 0)
        {
            names.Add("anyEvent");
        }

        if ((tracking & TerminalMouseTracking.SgrEncoding) != 0)
        {
            names.Add("sgr");
        }

        return names;
    }

    private static DiagnosticsRegion Clip(DiagnosticsRegion? region, int width, int height)
    {
        if (region is null)
        {
            return new DiagnosticsRegion(0, 0, width, height);
        }

        if (region.X < 0 || region.Y < 0 || region.Width < 0 || region.Height < 0)
        {
            throw InvalidParams("region must not be negative.");
        }

        var x = Math.Min(region.X, width);
        var y = Math.Min(region.Y, height);
        return new DiagnosticsRegion(
            x,
            y,
            (int)Math.Min((long)region.Width, width - x),
            (int)Math.Min((long)region.Height, height - y));
    }

    private static DiagnosticsProtocolException InvalidParams(string message)
        => new(DiagnosticsErrorCodes.InvalidParams, message);

    private static async Task<(PtyTerminalOptions Options, string Command)> CreateOptionsAsync(
        StartParams spec,
        string stateDirectory,
        string token,
        CancellationToken cancellationToken)
    {
        var options = new PtyTerminalOptions
        {
            Width = spec.Width,
            Height = spec.Height,
            TerminalType = spec.TerminalType,
            WorkingDirectory = spec.WorkingDirectory,
        };
        // The app writes its discovery file where the host looks, tagged with the session's token. The user's own
        // environment overrides win, so an app can be pointed elsewhere on purpose.
        options.Environment[DiagnosticsEndpointService.SessionTokenEnvironmentVariableName] = token;
        options.Environment[CliHomeVariable] = stateDirectory;
        foreach (var (name, value) in spec.Environment)
        {
            options.Environment[name] = value;
        }

        if (spec.WorkingDirectory is { } workingDirectory && !Directory.Exists(workingDirectory))
        {
            throw InvalidParams($"The working directory '{workingDirectory}' does not exist.");
        }

        switch (spec.TargetKind)
        {
            case StartTargetKind.Exe:
                RequireFile(spec.TargetPath);
                options.FileName = spec.TargetPath;
                break;

            case StartTargetKind.Dll:
                RequireFile(spec.TargetPath);
                options.FileName = PtyTerminal.ResolveDotnetHost();
                options.Arguments.Add(spec.TargetPath);
                options.WorkingDirectory ??= Path.GetDirectoryName(spec.TargetPath);
                break;

            default:
                if (!Directory.Exists(spec.TargetPath) && !File.Exists(spec.TargetPath))
                {
                    throw InvalidParams($"The project '{spec.TargetPath}' does not exist.");
                }

                await BuildProjectAsync(spec.TargetPath, cancellationToken).ConfigureAwait(false);
                options.FileName = PtyTerminal.ResolveDotnetHost();
                options.Arguments.Add("run");
                options.Arguments.Add("--no-build");
                options.Arguments.Add("--project");
                options.Arguments.Add(spec.TargetPath);
                options.Arguments.Add("--");
                break;
        }

        foreach (var argument in spec.Arguments)
        {
            options.Arguments.Add(argument);
        }

        return (options, string.Join(' ', options.Arguments.Prepend(options.FileName).Select(Quote)));
    }

    private static string Quote(string argument)
        => argument.Length > 0 && !argument.Any(static c => char.IsWhiteSpace(c) || c == '"')
            ? argument
            : "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw InvalidParams($"'{path}' does not exist.");
        }
    }

    // Builds outside the terminal, so build output never shows up as the app's screen.
    private static async Task BuildProjectAsync(string project, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(PtyTerminal.ResolveDotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("-nologo");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet build did not start.");
        process.StandardInput.Close();
        var tail = new Queue<string>();
        void Keep(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                return;
            }

            lock (tail)
            {
                tail.Enqueue(e.Data);
                if (tail.Count > OutputTailLines)
                {
                    tail.Dequeue();
                }
            }
        }

        process.OutputDataReceived += Keep;
        process.ErrorDataReceived += Keep;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BuildTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.Timeout, $"dotnet build did not finish within {BuildTimeout.TotalMinutes} minutes.");
        }

        if (process.ExitCode != 0)
        {
            string output;
            lock (tail)
            {
                output = string.Join(Environment.NewLine, tail);
            }

            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.Internal,
                $"dotnet build failed with exit code {process.ExitCode}.{Environment.NewLine}{output}");
        }
    }

    private static async Task<PtyTerminal> StartTerminalAsync(PtyTerminalOptions options, CancellationToken cancellationToken)
    {
        try
        {
            return await PtyTerminal.StartAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (PlatformNotSupportedException ex)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.PtyUnsupported, ex.Message, ex);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.Internal, $"'{options.FileName}' could not be started: {ex.Message}", ex);
        }
    }

    private async Task WaitForFirstFrameAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (Spec.NoWait)
        {
            return;
        }

        var limit = timeout ?? TimeSpan.FromMilliseconds(Spec.TimeoutMs);
        try
        {
            await _terminal.WaitUntilAsync(static frame => frame.FrameNumber > 0, limit, "the first frame", cancellationToken).ConfigureAwait(false);
            NoteFirstFrame(_terminal.CurrentFrame);
        }
        catch (TerminalTimeoutException)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.Timeout, $"'{Name}' drew nothing within {limit.TotalSeconds:0.###}s.");
        }
        catch (InvalidOperationException)
        {
            throw ProcessExited();
        }
    }

    private void WriteLedger()
    {
        DateTimeOffset startTime;
        try
        {
            using var process = Process.GetProcessById(_terminal.ProcessId);
            startTime = process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone: nothing for a later sweep to kill.
            return;
        }

        HostFiles.WriteAtomically(
            _ledgerFile,
            JsonSerializer.SerializeToUtf8Bytes(new SessionLedgerEntry(_terminal.ProcessId, startTime), HostJsonContext.Default.SessionLedgerEntry));
    }

    private void CheckCell(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _terminal.Width || y >= _terminal.Height)
        {
            throw InvalidParams($"({x},{y}) is outside the {_terminal.Width}x{_terminal.Height} terminal.");
        }
    }

    // The app may register its endpoint until a moment after its first frame; before that, and during that moment,
    // a missing discovery file only means "not yet".
    private bool GraceElapsed()
    {
        NoteFirstFrame(_terminal.CurrentFrame);
        var at = Interlocked.Read(ref _firstFrameAt);
        return at != 0 && Stopwatch.GetElapsedTime(at) >= AppLayer.Grace;
    }

    private void NoteFirstFrame(TerminalFrame frame)
    {
        if (frame.FrameNumber > 0 && Interlocked.Read(ref _firstFrameAt) == 0)
        {
            Interlocked.CompareExchange(ref _firstFrameAt, Stopwatch.GetTimestamp(), 0);
        }
    }

    public override DiagnosticsProtocolException ProcessExited()
    {
        var lastErrors = _app.RecentErrors;
        var data = JsonSerializer.SerializeToElement(
            new ProcessExitedData(_terminal.ExitCode, OutputTail.From(_output), lastErrors.Count == 0 ? null : lastErrors),
            HostJsonContext.Default.ProcessExitedData);
        var code = _terminal.ExitCode is { } exitCode ? $" with exit code {exitCode}" : string.Empty;
        return new DiagnosticsProtocolException(DiagnosticsErrorCodes.ProcessExited, $"The process of '{Name}' has exited{code}.", errorData: data);
    }

    private Task<T> InputAsync<T>(Func<Task<T>> send, CancellationToken cancellationToken)
        => WithLockAsync(
            async () =>
            {
                if (_terminal.HasExited)
                {
                    throw ProcessExited();
                }

                try
                {
                    return await send().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException && _terminal.HasExited)
                {
                    throw ProcessExited();
                }
            },
            cancellationToken);

    private async Task<T> WithLockAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _operations.Release();
        }
    }
}
