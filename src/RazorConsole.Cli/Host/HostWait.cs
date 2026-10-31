// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RazorConsole.Cli.Operations;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Host;

/// <summary>
/// Evaluates <c>wait</c> and <c>assert</c> conditions inside the host, where the screen and the app layer are close,
/// instead of a client polling over the wire.
/// </summary>
/// <remarks>
/// A wait polls every <see cref="PollInterval"/>. The app's endpoint answers in order and ignores <c>cancel</c>, so
/// polling its state (<c>find</c>, <c>focus</c>, <c>route</c>) needs nothing from the endpoint beyond its read methods.
/// A cancelled request (Ctrl+C, a closed connection) stops the loop at the next poll.
/// </remarks>
internal static class HostWait
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AppCallLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FailureDetailTimeout = TimeSpan.FromSeconds(2);
    private const int FailureTreeDepth = 3;

    public static string Describe(WaitSpec condition)
    {
        var value = condition.Value ?? string.Empty;
        return condition.Kind switch
        {
            WaitConditionKind.Text => $"the screen to contain \"{value}\"",
            WaitConditionKind.Regex => $"the screen to match /{value}/",
            WaitConditionKind.Gone => $"\"{value}\" to leave the screen",
            WaitConditionKind.Idle => $"{(condition.IdleMs ?? 0).ToString(CultureInfo.InvariantCulture)} ms without output",
            WaitConditionKind.Exit => "the process to exit",
            WaitConditionKind.Element => $"an element matching {value}",
            WaitConditionKind.Focus => $"focus on '{value}'",
            _ => $"the route to be '{value}'",
        };
    }

    /// <summary>
    /// Polls until the condition holds.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="condition">What to wait for.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="progress">Called about every 250 ms with the elapsed time and the frame number; its task is awaited.</param>
    /// <param name="cancellationToken">Cancelled by Ctrl+C or a closed connection.</param>
    /// <exception cref="DiagnosticsProtocolException"><c>timeout</c> with the last screen, <c>process_exited</c> when the
    /// process ended without the condition holding, or the app layer's errors.</exception>
    public static async Task<WaitResult> WaitAsync(
        HostSession session,
        WaitSpec condition,
        TimeSpan timeout,
        Func<long, long, Task>? progress,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var lastProgress = started;
        var state = new ScreenState();
        long activity = session.Activity;
        var quietSince = started;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = timeout - Stopwatch.GetElapsedTime(started);

            bool met;
            if (condition.Kind == WaitConditionKind.Idle)
            {
                if (UsesSnapshotForActivity(session))
                {
                    await state.ReadAsync(session, cancellationToken).ConfigureAwait(false);
                }

                var now = session.Activity;
                if (now != activity)
                {
                    activity = now;
                    quietSince = Stopwatch.GetTimestamp();
                }

                met = Stopwatch.GetElapsedTime(quietSince) >= TimeSpan.FromMilliseconds(condition.IdleMs ?? 0);
            }
            else
            {
                met = await CheckAsync(session, condition, Clamp(remaining), state, cancellationToken).ConfigureAwait(false);
            }

            if (met)
            {
                return new WaitResult(
                    (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    await state.FrameAsync(session, cancellationToken).ConfigureAwait(false),
                    condition.Kind == WaitConditionKind.Exit ? session.ExitCode : null);
            }

            // An exited process draws nothing more, so only the checks above can still succeed.
            if (session.HasExited && condition.Kind != WaitConditionKind.Exit)
            {
                throw session.ProcessExited();
            }

            if (remaining <= TimeSpan.Zero)
            {
                var screen = await state.TextAsync(session, cancellationToken).ConfigureAwait(false);
                throw new DiagnosticsProtocolException(
                    DiagnosticsErrorCodes.Timeout,
                    $"Timed out after {timeout.TotalSeconds:0.###}s waiting for {Describe(condition)}.",
                    errorData: JsonSerializer.SerializeToElement(new WaitTimeoutData(screen), HostJsonContext.Default.WaitTimeoutData));
            }

            if (progress is not null && Stopwatch.GetElapsedTime(lastProgress) >= ProgressInterval)
            {
                lastProgress = Stopwatch.GetTimestamp();
                await progress((long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, state.Frame).ConfigureAwait(false);
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Checks the condition once. A failure carries what a person needs to see why: the screen, and a short tree when
    /// the app layer is connected.
    /// </summary>
    /// <exception cref="DiagnosticsProtocolException"><c>assertion_failed</c> when the condition does not hold (or holds,
    /// with <paramref name="not"/>).</exception>
    public static async Task AssertAsync(HostSession session, WaitSpec condition, bool not, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var state = new ScreenState();
        bool met;
        if (condition.Kind == WaitConditionKind.Idle)
        {
            // Quiet means nothing happened for the whole period, so look twice, a period apart.
            await state.ReadAsync(session, cancellationToken).ConfigureAwait(false);
            var before = session.Activity;
            await Task.Delay(TimeSpan.FromMilliseconds(condition.IdleMs ?? 0), cancellationToken).ConfigureAwait(false);
            await state.ReadAsync(session, cancellationToken).ConfigureAwait(false);
            met = session.Activity == before;
        }
        else
        {
            met = await CheckAsync(session, condition, timeout, state, cancellationToken).ConfigureAwait(false);
        }

        if (met != not)
        {
            return;
        }

        var screen = await state.TextAsync(session, cancellationToken).ConfigureAwait(false);
        var description = Describe(condition);
        var data = new AssertionData(description, not, screen, await ShortTreeAsync(session, cancellationToken).ConfigureAwait(false));
        throw new DiagnosticsProtocolException(
            DiagnosticsErrorCodes.AssertionFailed,
            not ? $"Expected {description} not to hold, but it does." : $"Expected {description}, but it does not hold.",
            errorData: JsonSerializer.SerializeToElement(data, HostJsonContext.Default.AssertionData));
    }

    // Attached apps have no output counter; their frame number says whether they drew.
    private static bool UsesSnapshotForActivity(HostSession session) => session is AttachSession;

    private static TimeSpan Clamp(TimeSpan remaining)
        => remaining < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining > AppCallLimit ? AppCallLimit : remaining;

    private static async Task<bool> CheckAsync(HostSession session, WaitSpec condition, TimeSpan appTimeout, ScreenState state, CancellationToken cancellationToken)
    {
        var value = condition.Value ?? string.Empty;
        var json = DiagnosticsJsonContext.Default;
        switch (condition.Kind)
        {
            case WaitConditionKind.Text:
                return (await state.ReadAsync(session, cancellationToken).ConfigureAwait(false)).Text.Contains(value, StringComparison.Ordinal);

            case WaitConditionKind.Gone:
                return !(await state.ReadAsync(session, cancellationToken).ConfigureAwait(false)).Text.Contains(value, StringComparison.Ordinal);

            case WaitConditionKind.Regex:
                return IsMatch((await state.ReadAsync(session, cancellationToken).ConfigureAwait(false)).Text, value);

            case WaitConditionKind.Exit:
                return session.HasExited;

            case WaitConditionKind.Element:
            {
                var found = await session.App.CallAsync(DiagnosticsAppMethods.Find, new FindParams(value), json.FindParams, json.FindResult, appTimeout, cancellationToken).ConfigureAwait(false);
                return found.Matches.Count > 0;
            }

            case WaitConditionKind.Focus:
            {
                var focus = await session.App.CallAsync(DiagnosticsAppMethods.Focus, json.FocusResult, appTimeout, cancellationToken).ConfigureAwait(false);
                return string.Equals(focus.Current, value, StringComparison.Ordinal);
            }

            case WaitConditionKind.Route:
            {
                var route = await session.App.CallAsync(DiagnosticsAppMethods.Route, json.RouteResult, appTimeout, cancellationToken).ConfigureAwait(false);
                return string.Equals(route.Relative.Trim('/'), value.Trim('/'), StringComparison.Ordinal);
            }

            default:
                throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"'{condition.Kind}' is not a condition that can be checked once.");
        }
    }

    private static bool IsMatch(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"'{pattern}' is not a valid regular expression: {ex.Message}");
        }
        catch (RegexMatchTimeoutException)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "The regular expression took too long to match the screen.");
        }
    }

    // The top of the app's tree, when the app layer is already connected; never worth waiting for on a failure.
    private static async Task<JsonElement?> ShortTreeAsync(HostSession session, CancellationToken cancellationToken)
    {
        if (!session.App.Describe().Connected)
        {
            return null;
        }

        try
        {
            var tree = await session.App.CallAsync(
                DiagnosticsAppMethods.Tree,
                new TreeParams(Depth: FailureTreeDepth, IncludeText: true),
                DiagnosticsJsonContext.Default.TreeParams,
                DiagnosticsJsonContext.Default.TreeResult,
                FailureDetailTimeout,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.SerializeToElement(tree, DiagnosticsJsonContext.Default.TreeResult);
        }
        catch (DiagnosticsProtocolException)
        {
            return null;
        }
    }

    // The latest screen a wait has read, for its result and for what it reports when it gives up.
    private sealed class ScreenState
    {
        private ScreenSnapshot? _last;

        public long Frame => _last?.Frame ?? 0;

        public async Task<ScreenSnapshot> ReadAsync(HostSession session, CancellationToken cancellationToken)
            => _last = await session.SnapshotAsync(cancellationToken).ConfigureAwait(false);

        public async Task<string> TextAsync(HostSession session, CancellationToken cancellationToken)
        {
            try
            {
                return (await ReadAsync(session, cancellationToken).ConfigureAwait(false)).Text;
            }
            catch (DiagnosticsProtocolException) when (_last is not null)
            {
                return _last.Text;
            }
            catch (DiagnosticsProtocolException)
            {
                return string.Empty;
            }
        }

        public async Task<long> FrameAsync(HostSession session, CancellationToken cancellationToken)
        {
            if (_last is null)
            {
                try
                {
                    await ReadAsync(session, cancellationToken).ConfigureAwait(false);
                }
                catch (DiagnosticsProtocolException)
                {
                }
            }

            return Frame;
        }
    }
}
