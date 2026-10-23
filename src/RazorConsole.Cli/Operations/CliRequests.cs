// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Input;

namespace RazorConsole.Cli.Operations;

/// <summary>
/// What one command asks for. The verbs only build these; the operations run them, so a future MCP server can map
/// its tools onto the same records without System.CommandLine.
/// </summary>
internal abstract record CliRequest;

/// <summary>
/// Options shared by every command. <see cref="Session"/> is the name from <c>--session</c> or
/// <c>RAZORCONSOLE_SESSION</c>; the operations fall back to the current session when it is <see langword="null"/>.
/// </summary>
/// <param name="Json">Whether the result is written as one JSON object instead of text.</param>
/// <param name="Session">The session the command targets, or <see langword="null"/> for the current one.</param>
/// <param name="Timeout">How long waits and other blocking operations may take.</param>
internal sealed record CliGlobalOptions(bool Json, string? Session, TimeSpan Timeout)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
}

internal enum StartTargetKind
{
    Exe,
    Dll,
    Project,
}

/// <param name="Name">The session name, or <see langword="null"/> to pick one.</param>
/// <param name="TargetKind">Whether <paramref name="TargetPath"/> is an executable, an assembly or a project directory.</param>
/// <param name="TargetPath">The full path of the program, assembly or project.</param>
/// <param name="Arguments">The application's arguments (everything after <c>--</c>).</param>
/// <param name="Width">The terminal width in columns.</param>
/// <param name="Height">The terminal height in rows.</param>
/// <param name="Environment">Environment variable overrides.</param>
/// <param name="WorkingDirectory">The full path of the working directory, or <see langword="null"/> for the default.</param>
/// <param name="TerminalType">The <c>TERM</c> value on Linux and macOS.</param>
/// <param name="NoWait">Whether to return without waiting for the first frame.</param>
internal sealed record StartRequest(
    string? Name,
    StartTargetKind TargetKind,
    string TargetPath,
    IReadOnlyList<string> Arguments,
    int Width,
    int Height,
    IReadOnlyDictionary<string, string> Environment,
    string? WorkingDirectory,
    string TerminalType,
    bool NoWait) : CliRequest
{
    public const int DefaultWidth = 120;
    public const int DefaultHeight = 40;
    public const string DefaultTerminalType = "xterm-256color";
}

internal sealed record StopRequest(string? Name, TimeSpan Timeout) : CliRequest
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
}

internal sealed record RestartRequest(string? Name) : CliRequest;

internal sealed record ResizeRequest(int Width, int Height) : CliRequest;

internal sealed record ListRequest : CliRequest;

internal sealed record UseRequest(string Name) : CliRequest;

internal sealed record AttachRequest(int ProcessId, string? Name) : CliRequest;

internal sealed record DetachRequest(string? Name) : CliRequest;

/// <param name="Keys">The key presses, in order.</param>
/// <param name="Element">A selector to focus first, or <see langword="null"/> to send to whatever has focus.</param>
internal sealed record KeysRequest(IReadOnlyList<ConsoleKeyInfo> Keys, string? Element) : CliRequest;

internal sealed record TypeRequest(string Text) : CliRequest;

internal sealed record PasteRequest(string Text) : CliRequest;

/// <param name="Data">The terminal input with its escapes already decoded.</param>
internal sealed record RawRequest(string Data) : CliRequest;

/// <summary>
/// A click on exactly one target: a cell (<see cref="X"/>, <see cref="Y"/>), the <see cref="Nth"/> occurrence of
/// <see cref="Text"/> on the screen, or the element matched by <see cref="Element"/>.
/// </summary>
/// <param name="X">The zero-based column, with <paramref name="Y"/>.</param>
/// <param name="Y">The zero-based row, with <paramref name="X"/>.</param>
/// <param name="Text">Text to find on the screen.</param>
/// <param name="Nth">Which occurrence of <paramref name="Text"/> to click, from 1.</param>
/// <param name="Element">A selector resolved through the app layer.</param>
/// <param name="Button">The button in DOM numbering: 0 left, 1 middle, 2 right.</param>
/// <param name="Double">Whether to click twice.</param>
internal sealed record ClickRequest(int? X, int? Y, string? Text, int Nth, string? Element, int Button, bool Double) : CliRequest;

/// <param name="Kind">What the mouse does.</param>
/// <param name="X">The zero-based column.</param>
/// <param name="Y">The zero-based row.</param>
/// <param name="Button">The button in DOM numbering: 0 left, 1 middle, 2 right.</param>
/// <param name="Delta">For a wheel event, the rows to scroll: positive down, negative up.</param>
internal sealed record MouseRequest(TerminalMouseKind Kind, int X, int Y, int Button, int Delta) : CliRequest;

internal enum ScreenFormat
{
    Text,
    Ansi,
    Json,
}

internal sealed record ScreenRequest(ScreenFormat Format, DiagnosticsRegion? Region) : CliRequest;

internal sealed record FramesRequest(int Last) : CliRequest
{
    /// <summary>
    /// How many frames a PTY session keeps (<c>PtyTerminal.Frames</c>).
    /// </summary>
    public const int MaxFrames = 50;
}

internal sealed record DiffRequest(long? From, long? To) : CliRequest;

internal sealed record OutputRequest(long? Since, int? MaxBytes) : CliRequest;

internal enum WaitConditionKind
{
    Text,
    Regex,
    Gone,
    Idle,
    Exit,
    Element,
    Focus,
    Route,
}

/// <summary>
/// One condition for <c>wait</c> and <c>assert</c>.
/// </summary>
/// <param name="Kind">What is checked.</param>
/// <param name="Value">The text, pattern, selector, focus key or route; <see langword="null"/> for <c>idle</c> and <c>exit</c>.</param>
/// <param name="Idle">For <c>idle</c>, how long the output must stay quiet.</param>
internal sealed record WaitCondition(WaitConditionKind Kind, string? Value = null, TimeSpan? Idle = null);

internal sealed record WaitRequest(WaitCondition Condition) : CliRequest;

internal sealed record AssertRequest(WaitCondition Condition, bool Not) : CliRequest;

internal sealed record TreeRequest(string? Selector, int? Depth, bool IncludeText, bool IncludeLayout) : CliRequest;

internal sealed record FindRequest(string Selector, bool FirstOnly) : CliRequest;

internal sealed record InspectRequest(string Selector) : CliRequest;

internal sealed record InspectAtRequest(int X, int Y) : CliRequest;

internal enum FocusAction
{
    List,
    Set,
    Next,
    Previous,
}

/// <param name="Action">What to do with focus.</param>
/// <param name="Key">The focus key for <see cref="FocusAction.Set"/>.</param>
internal sealed record FocusRequest(FocusAction Action, string? Key) : CliRequest;

internal sealed record RouteRequest(string? Go) : CliRequest;

internal sealed record ErrorsRequest(long? Since, bool Follow) : CliRequest;

internal sealed record LogsRequest(long? Since, string? Level, string? Category, bool Follow) : CliRequest;

internal sealed record StatsRequest : CliRequest;

internal sealed record DoctorRequest(bool Fix) : CliRequest;

internal sealed record HostStatusRequest : CliRequest;

internal sealed record HostStopRequest : CliRequest;

/// <summary>
/// Runs the session host in this process (the hidden <c>__host</c> verb).
/// </summary>
/// <param name="IdleTimeout">How long the host stays up with no sessions and no clients.</param>
internal sealed record HostRunRequest(TimeSpan IdleTimeout) : CliRequest
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);
}
