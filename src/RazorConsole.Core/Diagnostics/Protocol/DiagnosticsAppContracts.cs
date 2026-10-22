// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Diagnostics.Protocol;

/// <summary>
/// Methods served by the app diagnostics endpoint. All of them read state except <see cref="Input"/>, which injects
/// input the way a user at the terminal would.
/// </summary>
internal static class DiagnosticsAppMethods
{
    public const string Tree = "tree";
    public const string Find = "find";
    public const string Inspect = "inspect";
    public const string InspectAt = "inspectAt";
    public const string Focus = "focus";
    public const string Route = "route";
    public const string Errors = "errors";
    public const string Logs = "logs";
    public const string Stats = "stats";
    public const string Screen = "screen";
    public const string Input = "input";

    /// <summary>
    /// Reported as features in the hello result, so clients can tell what this app supports. <see cref="Input"/> is
    /// added when the app's input loop accepts injected input.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Tree, Find, Inspect, InspectAt, Focus, Route, Errors, Logs, Stats, Screen];

    public static readonly IReadOnlyList<string> AllWithInput = [.. All, Input];
}

/// <summary>
/// Values of <see cref="DiagnosticsUnavailableData.Reason"/>.
/// </summary>
internal static class DiagnosticsUnavailableReasons
{
    /// <summary>The app's input loop has ended or never started (for example, redirected stdin).</summary>
    public const string InputStopped = "input_stopped";

    /// <summary>The app does not handle mouse input.</summary>
    public const string MouseDisabled = "mouse_disabled";

    /// <summary>The app renders with the legacy pipeline, which has no canvas.</summary>
    public const string LegacyPipeline = "legacy_pipeline";

    /// <summary>Nothing has been painted yet.</summary>
    public const string NotPainted = "not_painted";
}

/// <summary>
/// The <c>data</c> of an <see cref="DiagnosticsErrorCodes.InputUnavailable"/> or
/// <see cref="DiagnosticsErrorCodes.ScreenUnavailable"/> error.
/// </summary>
internal sealed record DiagnosticsUnavailableData(string Reason);

/// <summary>
/// The discovery file <c>apps/&lt;pid&gt;.json</c> an app with an endpoint writes into the state directory.
/// </summary>
/// <param name="Pid">The app's process id.</param>
/// <param name="ProcessStartTime">When the process started, in UTC. Tells a live app from a reused pid.</param>
/// <param name="Protocol">The protocol version the endpoint speaks.</param>
/// <param name="Endpoint">The pipe name or socket path to connect to.</param>
/// <param name="AppName">The entry assembly's name, when known.</param>
/// <param name="CoreVersion">The RazorConsole.Core version the app runs.</param>
/// <param name="SessionToken">The CLI session token from the app's environment, used to pair it with its PTY.</param>
internal sealed record DiagnosticsDiscoveryInfo(
    int Pid,
    DateTimeOffset ProcessStartTime,
    int Protocol,
    string Endpoint,
    string? AppName,
    string CoreVersion,
    string? SessionToken = null);

/// <summary>
/// A layout rectangle in screen cells.
/// </summary>
internal sealed record DiagnosticsRect(int Left, int Top, int Width, int Height, int ZIndex);

/// <summary>
/// One node of the rendered tree. Element nodes carry their stable <see cref="Id"/>; every node carries its
/// child-index <see cref="Path"/> from the root.
/// </summary>
internal sealed record DiagnosticsNode
{
    public string? Id { get; init; }

    public required string Path { get; init; }

    public required string Kind { get; init; }

    public string? Tag { get; init; }

    public string? Key { get; init; }

    public string? Hook { get; init; }

    public string? Text { get; init; }

    public IReadOnlyDictionary<string, string?>? Attributes { get; init; }

    public IReadOnlyList<string>? Events { get; init; }

    public DiagnosticsRect? Rect { get; init; }

    public string? FocusKey { get; init; }

    public bool? Focused { get; init; }

    /// <summary>
    /// Set when the depth limit cut the children off: how many there are.
    /// </summary>
    public int? ChildCount { get; init; }

    public IReadOnlyList<DiagnosticsNode>? Children { get; init; }
}

/// <summary>
/// A node found by a selector or a hit test, without its subtree.
/// </summary>
internal sealed record DiagnosticsMatch(
    string? Id,
    string Path,
    string Kind,
    string? Tag = null,
    string? Text = null,
    DiagnosticsRect? Rect = null,
    string? FocusKey = null);

internal sealed record TreeParams(string? Selector = null, int? Depth = null, bool? IncludeText = null, bool? IncludeLayout = null);

internal sealed record TreeResult(DiagnosticsNode? Root, long Frame);

internal sealed record FindParams(string Selector);

internal sealed record FindResult(IReadOnlyList<DiagnosticsMatch> Matches, long Frame);

internal sealed record InspectParams(string Selector, bool? First = null);

/// <summary>
/// One node with its own children, and its element ancestors nearest first.
/// </summary>
internal sealed record InspectResult(DiagnosticsNode Node, IReadOnlyList<DiagnosticsMatch> Ancestors, long Frame);

internal sealed record InspectAtParams(int X, int Y);

/// <summary>
/// The topmost element under a cell, followed by its element ancestors, read from the frame on screen.
/// </summary>
/// <param name="X">The requested column.</param>
/// <param name="Y">The requested row.</param>
/// <param name="Cell">The painted cell, when the frame was painted and the point lies on its canvas.</param>
/// <param name="Stack">The topmost element first, then its element ancestors.</param>
/// <param name="Frame">The frame the stack was read from.</param>
internal sealed record InspectAtResult(int X, int Y, ScreenCell? Cell, IReadOnlyList<DiagnosticsMatch> Stack, long Frame);

/// <summary>
/// A rectangle of screen cells.
/// </summary>
internal sealed record DiagnosticsRegion(int X, int Y, int Width, int Height);

/// <summary>
/// One screen cell. Colors are <c>#rrggbb</c>; a missing color is the terminal default.
/// </summary>
/// <param name="Text">The cell's text element; empty for the second cell of a wide character.</param>
/// <param name="Fg">Foreground color.</param>
/// <param name="Bg">Background color.</param>
/// <param name="Decoration">Decoration names such as <c>bold</c>, <c>underline</c>.</param>
/// <param name="Link">The cell's hyperlink.</param>
internal sealed record ScreenCell(string Text, string? Fg = null, string? Bg = null, IReadOnlyList<string>? Decoration = null, string? Link = null);

/// <param name="IncludeCells">Also return every cell with its style.</param>
/// <param name="Region">Only this part of the screen, clipped to it.</param>
internal sealed record ScreenParams(bool? IncludeCells = null, DiagnosticsRegion? Region = null);

/// <summary>
/// The screen as the app painted it. <see cref="Source"/> is <c>renderer</c>: the app's own canvas, without cursor,
/// terminal modes or output written outside RazorConsole.
/// </summary>
/// <param name="Source">Where the screen was read; <c>renderer</c> here.</param>
/// <param name="Width">Width of the returned lines.</param>
/// <param name="Height">Number of returned lines.</param>
/// <param name="Lines">Each row's text, trailing spaces kept; the second column of a wide character adds nothing.</param>
/// <param name="Frame">The frame that was painted.</param>
/// <param name="Region">The clipped region, when one was requested.</param>
/// <param name="Cells">Rows of cells, when requested.</param>
internal sealed record ScreenResult(
    string Source,
    int Width,
    int Height,
    IReadOnlyList<string> Lines,
    long Frame,
    DiagnosticsRegion? Region = null,
    IReadOnlyList<IReadOnlyList<ScreenCell>>? Cells = null);

/// <summary>
/// A mouse action in zero-based cells.
/// </summary>
/// <param name="Kind"><c>down</c>, <c>up</c>, <c>move</c>, <c>wheel</c> or <c>click</c> (down then up).</param>
/// <param name="X">Column.</param>
/// <param name="Y">Row.</param>
/// <param name="Button"><c>left</c> (default), <c>middle</c> or <c>right</c>.</param>
/// <param name="DeltaY">Wheel steps; negative scrolls up.</param>
/// <param name="Modifiers">Any of <c>shift</c>, <c>ctrl</c>, <c>alt</c>.</param>
/// <param name="Count">For <c>click</c>: how many clicks, 1 to 3.</param>
internal sealed record DiagnosticsMouseInput(
    string Kind,
    int X,
    int Y,
    string? Button = null,
    int? DeltaY = null,
    IReadOnlyList<string>? Modifiers = null,
    int? Count = null);

/// <summary>
/// Injected input. <see cref="Data"/> is decoded like input from a VT terminal (keys, text, escape sequences);
/// <see cref="Mouse"/> goes to the mouse handlers. With both, the data goes first.
/// </summary>
/// <param name="Data">Terminal input, at most 64 Ki characters.</param>
/// <param name="Mouse">A mouse action; needs mouse events enabled in the app.</param>
/// <param name="TimeoutMs">How long to wait for the handlers, default 10 s.</param>
internal sealed record InputParams(string? Data = null, DiagnosticsMouseInput? Mouse = null, int? TimeoutMs = null);

/// <summary>
/// Returned after the input's handlers have run.
/// </summary>
/// <param name="Frame">The latest frame at that time.</param>
internal sealed record InputResult(long Frame);

/// <summary>
/// The <c>data</c> of a <see cref="DiagnosticsErrorCodes.Timeout"/> error from <c>input</c>.
/// </summary>
/// <param name="Dispatched">True when the handlers started but did not finish; false when the input was withdrawn unsent.</param>
internal sealed record InputTimeoutData(bool Dispatched);

internal sealed record FocusTargetResult(string Key, string Id);

internal sealed record FocusResult(string? Current, IReadOnlyList<FocusTargetResult> Targets);

internal sealed record RouteResult(string Uri, string Relative);

/// <summary>
/// Parameters of <c>errors</c> and <c>logs</c>. <see cref="Since"/> is inclusive: pass the previous result's
/// <c>next</c> to get only newer entries.
/// </summary>
internal sealed record ErrorsParams(long? Since = null);

internal sealed record ErrorResult(long Seq, DateTimeOffset Time, string Source, string Type, string Message, string Stack);

internal sealed record ErrorsResult(IReadOnlyList<ErrorResult> Errors, long Next);

/// <param name="Since">Inclusive cursor, see <see cref="ErrorsParams"/>.</param>
/// <param name="Level">Minimum level name, such as <c>Warning</c>.</param>
/// <param name="Category">Category prefix, compared ignoring case.</param>
internal sealed record LogsParams(long? Since = null, string? Level = null, string? Category = null);

internal sealed record LogResult(long Seq, DateTimeOffset Time, string Level, string Category, int EventId, string Message, string? Exception = null);

internal sealed record LogsResult(IReadOnlyList<LogResult> Entries, long Next);

internal sealed record RenderTimeResult(double Last, double Avg, double Max);

internal sealed record StatsResult(long Frames, RenderTimeResult RenderMs, long DroppedFrames, long UptimeMs, string Pipeline, long Frame);

/// <summary>
/// The <c>data</c> of an <see cref="DiagnosticsErrorCodes.AmbiguousSelector"/> error.
/// </summary>
internal sealed record AmbiguousSelectorData(IReadOnlyList<DiagnosticsMatch> Candidates);
