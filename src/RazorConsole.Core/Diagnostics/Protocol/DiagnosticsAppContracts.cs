// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Diagnostics.Protocol;

/// <summary>
/// Methods served by the app diagnostics endpoint. All of them read state; none changes the app.
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

    /// <summary>
    /// Reported as features in the hello result, so clients can tell what this app supports.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Tree, Find, Inspect, InspectAt, Focus, Route, Errors, Logs, Stats];
}

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
/// The topmost element under a cell, followed by its element ancestors.
/// </summary>
internal sealed record InspectAtResult(int X, int Y, IReadOnlyList<DiagnosticsMatch> Stack, long Frame);

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
