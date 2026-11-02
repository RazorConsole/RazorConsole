// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using System.Text.Json;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Input;
using RazorConsole.Testing.Vt;

namespace RazorConsole.Cli.Host;

/// <summary>
/// A running RazorConsole app the host is attached to through its diagnostics endpoint. The host does not own the
/// process: it is not in the ledger, and stopping the session only disconnects.
/// </summary>
/// <remarks>
/// Input goes through the endpoint's input queue, and the screen is the renderer's own canvas
/// (<c>source: "renderer"</c>). There is no terminal, so PTY-only verbs fail with <c>terminal_layer_unavailable</c>.
/// </remarks>
internal sealed class AttachSession : HostSession
{
    public const string SessionKind = "attach";

    // The endpoint answers input after the app's handlers ran, for up to 10 s.
    private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly int _pid;
    private readonly string _command;
    private readonly AppLayer _app;
    private readonly DateTimeOffset _attachedAt = DateTimeOffset.UtcNow;
    private int _width;
    private int _height;
    private long _frame;

    private AttachSession(string name, int pid, string stateDirectory)
        : base(name)
    {
        _pid = pid;
        _command = $"attached to pid {pid}";
        _app = new AppLayer(
            () => AppPairing.ForPid(stateDirectory, pid),
            static () => true,
            () => AppPairing.IsRunning(pid) ? null : ProcessExited());
    }

    public override string Kind => SessionKind;

    public override AppLayer App => _app;

    public override bool HasExited => !AppPairing.IsRunning(_pid);

    public override int? ExitCode => null;

    public override long Activity => Interlocked.Read(ref _frame);

    public override SessionInfo Info => new(
        Name,
        SessionKind,
        _pid,
        Volatile.Read(ref _width),
        Volatile.Read(ref _height),
        _command,
        _attachedAt,
        HasExited,
        null,
        _app.Describe());

    /// <summary>
    /// Attaches to process <paramref name="pid"/>, waiting up to <paramref name="timeout"/> for its endpoint.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException"><c>process_exited</c> when no such process runs;
    /// <c>app_layer_unavailable</c> when it has no usable endpoint.</exception>
    public static async Task<AttachSession> AttachAsync(string name, int pid, string stateDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var session = new AttachSession(name, pid, stateDirectory);
        try
        {
            await session._app.GetLinkAsync(timeout, cancellationToken).ConfigureAwait(false);
            try
            {
                // The size, for click coordinates and list; an app that has not painted yet has none.
                await session.ScreenAsync(new PtyScreenParams(name, false, false, null), cancellationToken).ConfigureAwait(false);
            }
            catch (DiagnosticsProtocolException)
            {
            }

            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Stopping an attached session only disconnects: the app is not ours to end.
    public override async Task<StopResult> StopAsync(TimeSpan timeout)
    {
        await DisposeAsync().ConfigureAwait(false);
        return new StopResult(null, false);
    }

    public override async Task<ScreenSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        var screen = await ReadScreenAsync(new ScreenParams(), cancellationToken).ConfigureAwait(false);
        return new ScreenSnapshot(screen.Lines, screen.Frame);
    }

    public override async Task<PtyScreenResult> ScreenAsync(PtyScreenParams parameters, CancellationToken cancellationToken)
    {
        var screen = await ReadScreenAsync(new ScreenParams(parameters.IncludeCells ? true : null, parameters.Region), cancellationToken).ConfigureAwait(false);
        return new PtyScreenResult(
            screen.Source,
            screen.Width,
            screen.Height,
            Cursor: null,
            ActiveBuffer: null,
            MouseTracking: null,
            BracketedPaste: null,
            screen.Lines,
            screen.Frame,
            screen.Region,
            screen.Cells,
            Ansi: null);
    }

    public override async Task<SentResult> KeysAsync(IReadOnlyList<KeyPress> keys, CancellationToken cancellationToken)
    {
        var data = string.Concat(keys.Select(static key => VtInputEncoder.EncodeKey(key.ToConsoleKeyInfo(), applicationCursorKeys: false)));
        await SendAsync(new InputParams(Data: data), cancellationToken).ConfigureAwait(false);
        return new SentResult(keys.Count);
    }

    public override async Task<SentResult> TypeAsync(string text, CancellationToken cancellationToken)
    {
        if (text.Contains('\u001b', StringComparison.Ordinal))
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "type text must not contain ESC; use raw for escape sequences.");
        }

        await SendAsync(new InputParams(Data: text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r')), cancellationToken).ConfigureAwait(false);
        return new SentResult(text.Length);
    }

    public override async Task<PasteResult> PasteAsync(string text, CancellationToken cancellationToken)
    {
        await SendAsync(new InputParams(Data: VtInputEncoder.EncodePaste(text, bracketedPaste: false)), cancellationToken).ConfigureAwait(false);
        return new PasteResult(false);
    }

    public override async Task<RawResult> RawAsync(string data, CancellationToken cancellationToken)
    {
        await SendAsync(new InputParams(Data: data), cancellationToken).ConfigureAwait(false);
        return new RawResult(Encoding.UTF8.GetByteCount(data));
    }

    public override async Task<MouseResult> MouseAsync(MouseParams mouse, CancellationToken cancellationToken)
    {
        var kind = mouse.Kind switch
        {
            TerminalMouseKind.Down => "down",
            TerminalMouseKind.Up => "up",
            TerminalMouseKind.Move => "move",
            _ => "wheel",
        };
        var input = new DiagnosticsMouseInput(kind, mouse.X, mouse.Y, ButtonName(mouse.Button), mouse.Kind == TerminalMouseKind.Wheel ? mouse.Delta : null);
        await SendAsync(new InputParams(Mouse: input), cancellationToken).ConfigureAwait(false);
        return new MouseResult(mouse.X, mouse.Y);
    }

    public override async ValueTask DisposeAsync() => await _app.DisposeAsync().ConfigureAwait(false);

    public override DiagnosticsProtocolException ProcessExited()
    {
        var lastErrors = _app.RecentErrors;
        return new(
            DiagnosticsErrorCodes.ProcessExited,
            $"The process {_pid} of '{Name}' has exited.",
            errorData: JsonSerializer.SerializeToElement(
                new ProcessExitedData(null, LastErrors: lastErrors.Count == 0 ? null : lastErrors),
                HostJsonContext.Default.ProcessExitedData));
    }

    protected override async Task<ClickResult> ClickCoreAsync(ClickParams click, CancellationToken cancellationToken)
    {
        ClickTarget? target = null;
        int x, y;
        if (click.Text is { } text)
        {
            var screen = await ReadScreenAsync(new ScreenParams(IncludeCells: true), cancellationToken).ConfigureAwait(false);
            var cells = screen.Cells!;
            (x, y, var width) = FindText(screen.Width, screen.Height, (column, row) => cells[row][column].Text, text, click.Nth);
            target = new ClickTarget(text, click.Nth, width);
        }
        else
        {
            x = click.X ?? throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "click needs x and y, or text.");
            y = click.Y ?? throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "click needs x and y, or text.");
            var (width, height) = await ScreenSizeAsync(cancellationToken).ConfigureAwait(false);
            if (x < 0 || y < 0 || (width > 0 && x >= width) || (height > 0 && y >= height))
            {
                throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"({x},{y}) is outside the {width}x{height} screen.");
            }
        }

        var input = new DiagnosticsMouseInput("click", x, y, ButtonName(click.Button), Count: click.Double ? 2 : 1);
        await SendAsync(new InputParams(Mouse: input), cancellationToken).ConfigureAwait(false);
        return new ClickResult(x, y, target);
    }

    protected override async Task<(int Width, int Height)> ScreenSizeAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _width) == 0)
        {
            await ReadScreenAsync(new ScreenParams(), cancellationToken).ConfigureAwait(false);
        }

        return (Volatile.Read(ref _width), Volatile.Read(ref _height));
    }

    private static string ButtonName(int button) => button switch
    {
        1 => "middle",
        2 => "right",
        _ => "left",
    };

    private async Task<ScreenResult> ReadScreenAsync(ScreenParams parameters, CancellationToken cancellationToken)
    {
        var screen = await _app.CallAsync(
            DiagnosticsAppMethods.Screen,
            parameters,
            DiagnosticsJsonContext.Default.ScreenParams,
            DiagnosticsJsonContext.Default.ScreenResult,
            ReadTimeout,
            cancellationToken).ConfigureAwait(false);
        if (parameters.Region is null)
        {
            Volatile.Write(ref _width, screen.Width);
            Volatile.Write(ref _height, screen.Height);
        }

        Interlocked.Exchange(ref _frame, screen.Frame);
        return screen;
    }

    private async Task SendAsync(InputParams input, CancellationToken cancellationToken)
    {
        if (HasExited)
        {
            throw ProcessExited();
        }

        var result = await _app.CallAsync(
            DiagnosticsAppMethods.Input,
            input,
            DiagnosticsJsonContext.Default.InputParams,
            DiagnosticsJsonContext.Default.InputResult,
            InputTimeout,
            cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _frame, result.Frame);
    }
}
