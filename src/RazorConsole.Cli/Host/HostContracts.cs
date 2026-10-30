// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json.Serialization;
using RazorConsole.Cli.Operations;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Input;

namespace RazorConsole.Cli.Host;

/// <summary>
/// Methods served by the session host, after <see cref="DiagnosticsProtocol.HelloMethod"/>. Every session method
/// names its session; the client resolves <c>--session</c>, <c>RAZORCONSOLE_SESSION</c> and <c>current</c> first.
/// </summary>
internal static class HostMethods
{
    public const string Status = "host.status";
    public const string Shutdown = "host.shutdown";
    public const string Start = "session.start";
    public const string Stop = "session.stop";
    public const string Restart = "session.restart";
    public const string Resize = "session.resize";
    public const string List = "session.list";
    public const string Keys = "session.keys";
    public const string Type = "session.type";
    public const string Paste = "session.paste";
    public const string Raw = "session.raw";
    public const string Click = "session.click";
    public const string Mouse = "session.mouse";
    public const string Screen = "session.screen";
    public const string Frames = "session.frames";
    public const string Diff = "session.diff";
    public const string Output = "session.output";
}

/// <summary>
/// The contents of <c>host.json</c>, written by a running host.
/// </summary>
/// <param name="Pid">The host's process id.</param>
/// <param name="ProcessStartTime">When the host process started, in UTC. Tells a live host from a reused pid.</param>
/// <param name="Protocol">The protocol version the host speaks.</param>
/// <param name="Version">The tool version of the host.</param>
/// <param name="Endpoint">The pipe name or socket path.</param>
internal sealed record HostInfo(int Pid, DateTimeOffset ProcessStartTime, int Protocol, string Version, string Endpoint);

/// <summary>
/// A child process recorded in <c>sessions/&lt;name&gt;.json</c>, so the next host can kill it if this host crashes.
/// </summary>
/// <param name="Pid">The process id the pseudoterminal reports.</param>
/// <param name="ProcessStartTime">When that process started, in UTC.</param>
internal sealed record SessionLedgerEntry(int Pid, DateTimeOffset ProcessStartTime);

/// <param name="Pid">The host's process id.</param>
/// <param name="Version">The host's tool version.</param>
/// <param name="Protocol">The protocol version.</param>
/// <param name="Sessions">How many sessions the host holds.</param>
/// <param name="Clients">How many clients are connected, including the caller.</param>
/// <param name="StartedAt">When the host started.</param>
/// <param name="IdleTimeoutMs">How long the host stays up with no sessions and no clients.</param>
internal sealed record HostStatusResult(int Pid, string Version, int Protocol, int Sessions, int Clients, DateTimeOffset StartedAt, long IdleTimeoutMs);

/// <param name="StoppedSessions">How many sessions were stopped by the shutdown.</param>
internal sealed record HostShutdownResult(int StoppedSessions);

/// <summary>
/// A session as reported by <c>start</c>, <c>restart</c> and <c>list</c>.
/// </summary>
/// <param name="Name">The session name.</param>
/// <param name="Kind"><c>pty</c> for a pseudoterminal session.</param>
/// <param name="Pid">The process id the pseudoterminal reports.</param>
/// <param name="Width">The terminal width.</param>
/// <param name="Height">The terminal height.</param>
/// <param name="Command">The command line that was started.</param>
/// <param name="StartedAt">When the session started.</param>
/// <param name="Exited">Whether the process has exited.</param>
/// <param name="ExitCode">The exit code, once known.</param>
internal sealed record SessionInfo(
    string Name,
    string Kind,
    int Pid,
    int Width,
    int Height,
    string Command,
    DateTimeOffset StartedAt,
    bool Exited,
    int? ExitCode = null);

/// <summary>
/// Parameters of <see cref="HostMethods.Start"/>. Paths are full paths, because the host runs elsewhere.
/// </summary>
/// <param name="Name">The session name, or <see langword="null"/> for the first free <c>s&lt;n&gt;</c>.</param>
/// <param name="TargetKind">What <paramref name="TargetPath"/> is.</param>
/// <param name="TargetPath">The program, assembly or project directory.</param>
/// <param name="Arguments">The application's arguments.</param>
/// <param name="Width">The terminal width.</param>
/// <param name="Height">The terminal height.</param>
/// <param name="Environment">Environment variable overrides.</param>
/// <param name="WorkingDirectory">The working directory, or <see langword="null"/> for the default.</param>
/// <param name="TerminalType">The <c>TERM</c> value on Linux and macOS.</param>
/// <param name="NoWait">Whether to return before the first frame.</param>
/// <param name="TimeoutMs">How long to wait for the first frame.</param>
internal sealed record StartParams(
    string? Name,
    StartTargetKind TargetKind,
    string TargetPath,
    IReadOnlyList<string> Arguments,
    int Width,
    int Height,
    IReadOnlyDictionary<string, string> Environment,
    string? WorkingDirectory,
    string TerminalType,
    bool NoWait,
    long TimeoutMs);

/// <summary>
/// The parameters of methods that take none.
/// </summary>
internal sealed record EmptyParams;

/// <summary>
/// The <c>data</c> of a <see cref="DiagnosticsErrorCodes.HostVersionMismatch"/> error.
/// </summary>
/// <param name="HostVersion">The running host's tool version.</param>
/// <param name="ClientVersion">This tool's version.</param>
/// <param name="Sessions">How many sessions the host holds.</param>
internal sealed record HostVersionMismatchData(string HostVersion, string ClientVersion, int Sessions);

internal sealed record SessionParams(string Session);

/// <param name="Current">The session that is now current.</param>
internal sealed record UseResult(string Current);

/// <summary>
/// The result of <c>host status</c>: <see cref="Running"/> alone when no host runs.
/// </summary>
/// <param name="Running">Whether a host answered.</param>
/// <param name="Pid">The host's process id.</param>
/// <param name="Version">The host's tool version.</param>
/// <param name="Protocol">The protocol version.</param>
/// <param name="Sessions">How many sessions the host holds.</param>
/// <param name="Clients">How many clients are connected, including the caller.</param>
/// <param name="StartedAt">When the host started.</param>
/// <param name="IdleTimeoutMs">How long the host stays up with no sessions and no clients.</param>
internal sealed record HostStatusView(
    bool Running,
    int? Pid = null,
    string? Version = null,
    int? Protocol = null,
    int? Sessions = null,
    int? Clients = null,
    DateTimeOffset? StartedAt = null,
    long? IdleTimeoutMs = null);

/// <param name="Stopped">Whether a running host was stopped.</param>
/// <param name="StoppedSessions">How many sessions it stopped.</param>
internal sealed record HostStopView(bool Stopped, int StoppedSessions);

internal sealed record StopParams(string Session, long TimeoutMs);

/// <param name="ExitCode">The exit code, when it is known.</param>
/// <param name="Killed">Whether the process had to be killed after Ctrl+C.</param>
internal sealed record StopResult(int? ExitCode, bool Killed);

internal sealed record RestartParams(string Session, long TimeoutMs);

internal sealed record ResizeParams(string Session, int Width, int Height);

internal sealed record ResizeResult(int Width, int Height);

internal sealed record ListResult(IReadOnlyList<SessionInfo> Sessions);

/// <summary>
/// One key press on the wire.
/// </summary>
/// <param name="Char">The character the key produces; empty for none.</param>
/// <param name="Key">The key.</param>
/// <param name="Modifiers">The modifiers held down.</param>
internal sealed record KeyPress(string Char, ConsoleKey Key, ConsoleModifiers Modifiers)
{
    public static KeyPress From(ConsoleKeyInfo key)
        => new(key.KeyChar == '\0' ? string.Empty : key.KeyChar.ToString(), key.Key, key.Modifiers);

    public ConsoleKeyInfo ToConsoleKeyInfo()
        => new(
            Char.Length == 0 ? '\0' : Char[0],
            Key,
            (Modifiers & ConsoleModifiers.Shift) != 0,
            (Modifiers & ConsoleModifiers.Alt) != 0,
            (Modifiers & ConsoleModifiers.Control) != 0);
}

internal sealed record KeysParams(string Session, IReadOnlyList<KeyPress> Keys);

internal sealed record TextParams(string Session, string Text);

/// <param name="Sent">How many keys or characters were sent.</param>
internal sealed record SentResult(int Sent);

/// <param name="Bracketed">Whether the paste was wrapped in bracketed-paste markers.</param>
internal sealed record PasteResult(bool Bracketed);

/// <param name="Bytes">How many UTF-8 bytes were written.</param>
internal sealed record RawResult(int Bytes);

/// <summary>
/// Parameters of <see cref="HostMethods.Click"/>: a cell, or the <paramref name="Nth"/> occurrence of <paramref name="Text"/>.
/// </summary>
/// <param name="Session">The session.</param>
/// <param name="X">The column, with <paramref name="Y"/>.</param>
/// <param name="Y">The row, with <paramref name="X"/>.</param>
/// <param name="Text">Text to find on the screen.</param>
/// <param name="Nth">Which occurrence of <paramref name="Text"/>, from 1.</param>
/// <param name="Button">The button in DOM numbering.</param>
/// <param name="Double">Whether to click twice.</param>
internal sealed record ClickParams(string Session, int? X, int? Y, string? Text, int Nth, int Button, bool Double);

/// <param name="X">The clicked column.</param>
/// <param name="Y">The clicked row.</param>
/// <param name="Target">What was clicked, when it was found by text.</param>
internal sealed record ClickResult(int X, int Y, ClickTarget? Target = null);

/// <param name="Text">The text that was found.</param>
/// <param name="Nth">Which occurrence was clicked.</param>
/// <param name="Width">The text's width in cells.</param>
internal sealed record ClickTarget(string Text, int Nth, int Width);

internal sealed record MouseParams(string Session, TerminalMouseKind Kind, int X, int Y, int Button, int Delta);

internal sealed record MouseResult(int X, int Y);

/// <param name="Session">The session.</param>
/// <param name="IncludeCells">Also return every cell with its style.</param>
/// <param name="IncludeAnsi">Also return the screen as text with ANSI styles.</param>
/// <param name="Region">Only this part of the screen, clipped to it.</param>
internal sealed record PtyScreenParams(string Session, bool IncludeCells, bool IncludeAnsi, DiagnosticsRegion? Region);

/// <param name="X">The cursor column.</param>
/// <param name="Y">The cursor row.</param>
/// <param name="Visible">Whether the cursor is shown.</param>
internal sealed record ScreenCursor(int X, int Y, bool Visible);

/// <summary>
/// The screen of a PTY session: what a real terminal shows (§3.7). <see cref="Source"/> is <c>pty</c>.
/// </summary>
/// <param name="Source">Where the screen was read; <c>pty</c> here.</param>
/// <param name="Width">Width of the returned lines.</param>
/// <param name="Height">Number of returned lines.</param>
/// <param name="Cursor">The cursor, in full-screen coordinates.</param>
/// <param name="ActiveBuffer"><c>normal</c> or <c>alternate</c>.</param>
/// <param name="MouseTracking">The mouse reporting modes the app enabled.</param>
/// <param name="BracketedPaste">Whether the app enabled bracketed paste.</param>
/// <param name="Lines">Each row's text, trailing spaces kept.</param>
/// <param name="Frame">The frame number.</param>
/// <param name="Region">The clipped region, when one was requested.</param>
/// <param name="Cells">Rows of cells, when requested.</param>
/// <param name="Ansi">The screen with ANSI styles, when requested.</param>
internal sealed record PtyScreenResult(
    string Source,
    int Width,
    int Height,
    ScreenCursor Cursor,
    string ActiveBuffer,
    IReadOnlyList<string> MouseTracking,
    bool BracketedPaste,
    IReadOnlyList<string> Lines,
    long Frame,
    DiagnosticsRegion? Region = null,
    IReadOnlyList<IReadOnlyList<ScreenCell>>? Cells = null,
    string? Ansi = null);

internal sealed record FramesParams(string Session, int Last);

/// <param name="Number">The frame number.</param>
/// <param name="ObservedAt">When the frame was captured.</param>
/// <param name="Lines">Each row's text.</param>
internal sealed record FrameInfo(long Number, DateTimeOffset ObservedAt, IReadOnlyList<string> Lines);

internal sealed record FramesResult(IReadOnlyList<FrameInfo> Frames);

internal sealed record DiffParams(string Session, long? From, long? To);

/// <param name="Line">The zero-based row.</param>
/// <param name="Before">The row in the older frame; <see langword="null"/> when that frame had fewer rows.</param>
/// <param name="After">The row in the newer frame; <see langword="null"/> when that frame has fewer rows.</param>
internal sealed record LineChange(int Line, string? Before, string? After);

internal sealed record DiffResult(long From, long To, IReadOnlyList<LineChange> Changes);

internal sealed record OutputParams(string Session, long? Since, int? MaxBytes);

/// <summary>
/// A slice of the session's raw output. Offsets count UTF-8 bytes since the session started; pass
/// <see cref="Next"/> as the next <c>since</c>. <see cref="Offset"/> is later than the requested <c>since</c> when
/// that output was already dropped from the buffer.
/// </summary>
/// <param name="Offset">Where <paramref name="Data"/> starts.</param>
/// <param name="Next">Where the next read continues.</param>
/// <param name="Data">The decoded output.</param>
internal sealed record OutputResult(long Offset, long Next, string Data);

/// <summary>
/// The <c>data</c> of a <see cref="DiagnosticsErrorCodes.ProcessExited"/> error.
/// </summary>
internal sealed record ProcessExitedData(int? ExitCode);

/// <summary>
/// Source-generated JSON metadata for the session host protocol, so the tool stays trim and AOT safe.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(HostInfo))]
[JsonSerializable(typeof(SessionLedgerEntry))]
[JsonSerializable(typeof(HostStatusResult))]
[JsonSerializable(typeof(HostShutdownResult))]
[JsonSerializable(typeof(SessionInfo))]
[JsonSerializable(typeof(StartParams))]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(HostVersionMismatchData))]
[JsonSerializable(typeof(SessionParams))]
[JsonSerializable(typeof(UseResult))]
[JsonSerializable(typeof(HostStatusView))]
[JsonSerializable(typeof(HostStopView))]
[JsonSerializable(typeof(StopParams))]
[JsonSerializable(typeof(StopResult))]
[JsonSerializable(typeof(RestartParams))]
[JsonSerializable(typeof(ResizeParams))]
[JsonSerializable(typeof(ResizeResult))]
[JsonSerializable(typeof(ListResult))]
[JsonSerializable(typeof(KeysParams))]
[JsonSerializable(typeof(TextParams))]
[JsonSerializable(typeof(SentResult))]
[JsonSerializable(typeof(PasteResult))]
[JsonSerializable(typeof(RawResult))]
[JsonSerializable(typeof(ClickParams))]
[JsonSerializable(typeof(ClickResult))]
[JsonSerializable(typeof(MouseParams))]
[JsonSerializable(typeof(MouseResult))]
[JsonSerializable(typeof(PtyScreenParams))]
[JsonSerializable(typeof(PtyScreenResult))]
[JsonSerializable(typeof(FramesParams))]
[JsonSerializable(typeof(FramesResult))]
[JsonSerializable(typeof(DiffParams))]
[JsonSerializable(typeof(DiffResult))]
[JsonSerializable(typeof(OutputParams))]
[JsonSerializable(typeof(OutputResult))]
[JsonSerializable(typeof(ProcessExitedData))]
internal sealed partial class HostJsonContext : JsonSerializerContext
{
}
