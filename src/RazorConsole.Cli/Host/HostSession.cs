// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Host;

/// <summary>
/// What a wait or assertion reads from a session: the screen as lines, and the frame it was read at.
/// </summary>
internal sealed record ScreenSnapshot(IReadOnlyList<string> Lines, long Frame)
{
    public string Text => string.Join('\n', Lines.Select(static line => line.TrimEnd()));
}

/// <summary>
/// A session the host holds: a process in a pseudoterminal, or a running app the host is attached to.
/// </summary>
/// <remarks>
/// Both have an app layer (the connection to the app's diagnostics endpoint) and answer the input and screen verbs.
/// Only a PTY session has a terminal: <c>resize</c>, <c>restart</c>, <c>frames</c>, <c>diff</c> and <c>output</c> on an
/// attached one fail with <c>terminal_layer_unavailable</c>.
/// </remarks>
internal abstract class HostSession : IAsyncDisposable
{
    /// <summary>
    /// The timeout for the calls to the app layer that input verbs make to resolve an element.
    /// </summary>
    protected static readonly TimeSpan DefaultAppTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ElementPollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MinimumAttemptTimeout = TimeSpan.FromSeconds(1);

    protected HostSession(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public abstract string Kind { get; }

    public abstract SessionInfo Info { get; }

    public abstract AppLayer App { get; }

    public abstract bool HasExited { get; }

    public abstract int? ExitCode { get; }

    /// <summary>
    /// Grows whenever the app writes output or draws, so a wait can tell when it went quiet.
    /// </summary>
    public abstract long Activity { get; }

    public abstract Task<StopResult> StopAsync(TimeSpan timeout);

    public abstract Task<ScreenSnapshot> SnapshotAsync(CancellationToken cancellationToken);

    public abstract Task<SentResult> KeysAsync(IReadOnlyList<KeyPress> keys, CancellationToken cancellationToken);

    public abstract Task<SentResult> TypeAsync(string text, CancellationToken cancellationToken);

    public abstract Task<PasteResult> PasteAsync(string text, CancellationToken cancellationToken);

    public abstract Task<RawResult> RawAsync(string data, CancellationToken cancellationToken);

    public abstract Task<MouseResult> MouseAsync(MouseParams mouse, CancellationToken cancellationToken);

    public abstract Task<PtyScreenResult> ScreenAsync(PtyScreenParams parameters, CancellationToken cancellationToken);

    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// The error for a process that has exited, with its exit code.
    /// </summary>
    public abstract DiagnosticsProtocolException ProcessExited();

    /// <summary>
    /// Lets the session adjust the app's answer to <paramref name="method"/>. A PTY session replaces the <c>cell</c> of
    /// <c>inspectAt</c> with the one on its terminal screen, which is what the user sees; an attached session keeps the
    /// renderer's.
    /// </summary>
    public virtual JsonElement AdjustAppResult(string method, JsonElement result) => result;

    /// <summary>
    /// The session as a PTY session, for the verbs that need a terminal.
    /// </summary>
    public virtual PtySession RequirePty(string verb)
        => throw new DiagnosticsProtocolException(
            DiagnosticsErrorCodes.TerminalLayerUnavailable,
            $"'{Name}' is attached to a running app and has no terminal; {verb} needs a session started with 'start'.",
            errorData: JsonSerializer.SerializeToElement(new LayerUnavailableData(DiagnosticsUnavailableReasons.Attached), HostJsonContext.Default.LayerUnavailableData));

    /// <summary>
    /// Clicks a cell, or the middle of an element. The element is looked up in the app layer first.
    /// </summary>
    public async Task<ClickResult> ClickAsync(ClickParams click, CancellationToken cancellationToken)
    {
        if (click.Element is { } selector)
        {
            var timeout = click.TimeoutMs is { } ms ? TimeSpan.FromMilliseconds(ms) : DefaultAppTimeout;
            var (x, y) = await WaitForElementAsync(
                callTimeout => CenterOfAsync(selector, callTimeout, cancellationToken),
                timeout,
                cancellationToken).ConfigureAwait(false);
            click = click with { X = x, Y = y, Text = null, Element = null };
        }

        return await ClickCoreAsync(click, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends keys, after moving focus to an element when one is given. The keys are real input: they go through the
    /// terminal, or the app's input queue, not a state change like <c>focus --next</c>.
    /// </summary>
    public async Task<SentResult> KeysAsync(KeysParams keys, CancellationToken cancellationToken)
    {
        if (keys.Element is { } selector)
        {
            var timeout = keys.TimeoutMs is { } ms ? TimeSpan.FromMilliseconds(ms) : DefaultAppTimeout;
            await WaitForElementAsync(
                async callTimeout =>
                {
                    var inspected = await App.CallAsync(
                        DiagnosticsAppMethods.Inspect,
                        new InspectParams(selector, First: false),
                        DiagnosticsJsonContext.Default.InspectParams,
                        DiagnosticsJsonContext.Default.InspectResult,
                        callTimeout,
                        cancellationToken).ConfigureAwait(false);
                    var key = inspected.Node.FocusKey ?? throw new DiagnosticsProtocolException(
                        DiagnosticsErrorCodes.ElementNotFocusable,
                        $"The element '{selector}' cannot take focus: it has no focus key.");
                    return await App.CallAsync(
                        DiagnosticsAppMethods.FocusSet,
                        new FocusSetParams(key),
                        DiagnosticsJsonContext.Default.FocusSetParams,
                        DiagnosticsJsonContext.Default.FocusChangeResult,
                        callTimeout,
                        cancellationToken).ConfigureAwait(false);
                },
                timeout,
                cancellationToken).ConfigureAwait(false);
        }

        return await KeysAsync(keys.Keys, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> until it stops failing with an element that is not there yet: not found, not
    /// focusable or not visible. An app draws and registers its focus targets a moment after it starts, so an element
    /// can be missing for a few frames. After <paramref name="timeout"/> the last such error is thrown as it is; any
    /// other error (an ambiguous selector, an exited process) is thrown at once.
    /// </summary>
    internal static async Task<T> WaitForElementAsync<T>(Func<TimeSpan, Task<T>> attempt, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = timeout - Stopwatch.GetElapsedTime(started);
            try
            {
                return await attempt(remaining < MinimumAttemptTimeout ? MinimumAttemptTimeout : remaining).ConfigureAwait(false);
            }
            catch (DiagnosticsProtocolException ex) when (IsElementNotReady(ex.Code) && remaining > ElementPollInterval)
            {
                await Task.Delay(ElementPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsElementNotReady(string code)
        => code is DiagnosticsErrorCodes.ElementNotFound or DiagnosticsErrorCodes.ElementNotFocusable or DiagnosticsErrorCodes.ElementNotVisible;

    protected abstract Task<ClickResult> ClickCoreAsync(ClickParams click, CancellationToken cancellationToken);

    /// <summary>
    /// The size of the screen that mouse coordinates refer to.
    /// </summary>
    protected abstract Task<(int Width, int Height)> ScreenSizeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds the <paramref name="nth"/> occurrence of <paramref name="text"/>, row by row, and returns the middle cell
    /// of it and its width in cells. <paramref name="cellText"/> returns the text of one cell; a wide character's
    /// second cell has none.
    /// </summary>
    internal static (int X, int Y, int Width) FindText(int width, int height, Func<int, int, string> cellText, string text, int nth)
    {
        if (text.Length == 0 || nth < 1)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "click text must not be empty, and nth starts at 1.");
        }

        var seen = 0;
        for (var y = 0; y < height; y++)
        {
            // Each character's column; wide characters span two cells and their second cell has no text.
            var line = new System.Text.StringBuilder();
            var columns = new List<int>();
            for (var x = 0; x < width; x++)
            {
                var cell = cellText(x, y);
                line.Append(cell);
                for (var i = 0; i < cell.Length; i++)
                {
                    columns.Add(x);
                }
            }

            var lineText = line.ToString();
            var index = lineText.IndexOf(text, StringComparison.Ordinal);
            while (index >= 0)
            {
                if (++seen == nth)
                {
                    var start = columns[index];
                    var end = index + text.Length < columns.Count ? columns[index + text.Length] : width;
                    var span = Math.Max(1, end - start);
                    return (start + ((span - 1) / 2), y, span);
                }

                index = lineText.IndexOf(text, index + 1, StringComparison.Ordinal);
            }
        }

        var message = seen == 0
            ? $"'{text}' is not on the screen."
            : $"'{text}' is on the screen {seen} time(s), not {nth}.";
        throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.ElementNotFound, message);
    }

    // The middle cell of the element's layout rectangle, clipped to the screen.
    private async Task<(int X, int Y)> CenterOfAsync(string selector, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var inspected = await App.CallAsync(
            DiagnosticsAppMethods.Inspect,
            new InspectParams(selector, First: false),
            DiagnosticsJsonContext.Default.InspectParams,
            DiagnosticsJsonContext.Default.InspectResult,
            timeout,
            cancellationToken).ConfigureAwait(false);
        var (width, height) = await ScreenSizeAsync(cancellationToken).ConfigureAwait(false);

        var rect = inspected.Node.Rect;
        var left = rect is null ? 0 : Math.Max(rect.Left, 0);
        var top = rect is null ? 0 : Math.Max(rect.Top, 0);
        var right = rect is null ? 0 : Math.Min(rect.Left + rect.Width, width);
        var bottom = rect is null ? 0 : Math.Min(rect.Top + rect.Height, height);
        if (right <= left || bottom <= top)
        {
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.ElementNotVisible,
                rect is null
                    ? $"The element '{selector}' has no layout rectangle."
                    : $"The element '{selector}' is outside the {width}x{height} screen.");
        }

        return (left + ((right - left - 1) / 2), top + ((bottom - top - 1) / 2));
    }
}
