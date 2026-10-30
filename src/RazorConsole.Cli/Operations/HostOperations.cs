// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RazorConsole.Cli.Host;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Operations;

/// <summary>
/// Runs requests against the session host: PTY and attached sessions, input, the screen, the app layer and the host
/// itself.
/// </summary>
/// <remarks>
/// <c>start</c> and <c>attach</c> start the host. Other commands connect to a running host; with none, a session
/// command fails with <c>session_not_found</c> and <c>list</c> and <c>host status</c> report nothing running. The
/// session is <c>--session</c>, then <c>RAZORCONSOLE_SESSION</c> (both already in <see cref="CliGlobalOptions.Session"/>),
/// then the <c>current</c> file. <c>wait</c> and <c>assert</c> run in the host; the app-layer verbs pass through it to
/// the app's endpoint.
/// </remarks>
internal sealed class HostOperations(HostConnector connector) : ICliOperations
{
    private static readonly TimeSpan BuildAllowance = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FollowInterval = TimeSpan.FromMilliseconds(250);

    private HostFiles Files => connector.Files;

    public async Task<CliResult> ExecuteAsync(CliRequest request, CliGlobalOptions options, CancellationToken cancellationToken, ICliStream? stream = null)
    {
        try
        {
            return request switch
            {
                StartRequest start => await StartAsync(start, options, cancellationToken).ConfigureAwait(false),
                StopRequest stop => await StopAsync(stop, options, cancellationToken).ConfigureAwait(false),
                RestartRequest restart => await RestartAsync(restart, options, cancellationToken).ConfigureAwait(false),
                ListRequest => await ListAsync(options, cancellationToken).ConfigureAwait(false),
                UseRequest use => await UseAsync(use, options, cancellationToken).ConfigureAwait(false),
                HostStatusRequest => await HostStatusAsync(options, cancellationToken).ConfigureAwait(false),
                HostStopRequest => await HostStopAsync(options, cancellationToken).ConfigureAwait(false),
                HostRunRequest run => await HostRunAsync(run, cancellationToken).ConfigureAwait(false),
                AttachRequest attach => await AttachAsync(attach, options, cancellationToken).ConfigureAwait(false),
                DetachRequest detach => await DetachAsync(detach, options, cancellationToken).ConfigureAwait(false),
                WaitRequest wait => await WaitAsync(wait, options, cancellationToken).ConfigureAwait(false),
                AssertRequest assert => await AssertAsync(assert, options, cancellationToken).ConfigureAwait(false),
                ErrorsRequest or LogsRequest => await EntriesAsync(request, options, stream, cancellationToken).ConfigureAwait(false),
                TreeRequest or FindRequest or InspectRequest or InspectAtRequest or FocusRequest or RouteRequest or StatsRequest
                    => await AppVerbAsync(request, options, cancellationToken).ConfigureAwait(false),
                _ when IsSessionRequest(request) => await SessionAsync(request, options, cancellationToken).ConfigureAwait(false),
                _ => NotImplemented($"'{request.GetType().Name}'"),
            };
        }
        catch (DiagnosticsProtocolException ex)
        {
            return CliResult.Failure(ex.Code, ex.Message, ex.ErrorData, options.Session);
        }
    }

    private static bool IsSessionRequest(CliRequest request)
        => request is ResizeRequest or KeysRequest or TypeRequest or PasteRequest or RawRequest or ClickRequest or MouseRequest
            or ScreenRequest or FramesRequest or DiffRequest or OutputRequest;

    private static CliResult NotImplemented(string what)
        => CliResult.Failure(DiagnosticsErrorCodes.Internal, $"{what} is not implemented yet.");

    private static JsonElement Json<T>(T value, JsonTypeInfo<T> typeInfo) => JsonSerializer.SerializeToElement(value, typeInfo);

    private static TimeSpan Deadline(TimeSpan timeout) => timeout + HostClient.ResponseGrace;

    private static long Milliseconds(TimeSpan value) => (long)value.TotalMilliseconds;

    private static string Describe(SessionInfo info)
    {
        var state = info.Exited
            ? info.ExitCode is { } code ? $"exited {code.ToString(CultureInfo.InvariantCulture)}" : "exited"
            : "running";
        var app = info.AppLayer switch
        {
            null => string.Empty,
            { Connected: true } => ", app layer connected",
            var layer => ", app layer " + (layer.Reason ?? "unavailable"),
        };
        return $"{info.Name} (pid {info.Pid.ToString(CultureInfo.InvariantCulture)}, {info.Width.ToString(CultureInfo.InvariantCulture)}x{info.Height.ToString(CultureInfo.InvariantCulture)}, {state}{app}) {info.Command}";
    }

    private static DiagnosticsProtocolException NoSession(string? name)
        => new(
            DiagnosticsErrorCodes.SessionNotFound,
            name is null
                ? "No session given and no current session. Start one, or pass --session."
                : $"There is no session named '{name}' (the session host is not running).");

    private async Task<CliResult> StartAsync(StartRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var parameters = new StartParams(
            request.Name ?? options.Session,
            request.TargetKind,
            request.TargetPath,
            request.Arguments,
            request.Width,
            request.Height,
            request.Environment,
            request.WorkingDirectory,
            request.TerminalType,
            request.NoWait,
            Milliseconds(options.Timeout));

        var deadline = Deadline(options.Timeout) + (request.TargetKind == StartTargetKind.Project ? BuildAllowance : TimeSpan.Zero);
        var client = await connector.ConnectOrStartAsync(cancellationToken).ConfigureAwait(false);
        await using (client.ConfigureAwait(false))
        {
            var info = await client.RequestAsync(HostMethods.Start, parameters, HostJsonContext.Default.StartParams, HostJsonContext.Default.SessionInfo, deadline, cancellationToken).ConfigureAwait(false);
            Files.WriteCurrent(info.Name);
            return CliResult.Success(info.Name, Json(info, HostJsonContext.Default.SessionInfo), "started " + Describe(info));
        }
    }

    private async Task<CliResult> StopAsync(StopRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var name = request.Name ?? ResolveSession(options);
        var result = await OnHostAsync(
            name,
            client => client.RequestAsync(
                HostMethods.Stop,
                new StopParams(name, Milliseconds(request.Timeout)),
                HostJsonContext.Default.StopParams,
                HostJsonContext.Default.StopResult,
                Deadline(request.Timeout),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        Files.ClearCurrent(name);

        var exit = result.ExitCode is { } code ? $" (exit code {code.ToString(CultureInfo.InvariantCulture)})" : string.Empty;
        return CliResult.Success(name, Json(result, HostJsonContext.Default.StopResult), $"{(result.Killed ? "killed" : "stopped")} {name}{exit}");
    }

    private async Task<CliResult> RestartAsync(RestartRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var name = request.Name ?? ResolveSession(options);
        var info = await OnHostAsync(
            name,
            client => client.RequestAsync(
                HostMethods.Restart,
                new RestartParams(name, Milliseconds(options.Timeout)),
                HostJsonContext.Default.RestartParams,
                HostJsonContext.Default.SessionInfo,
                Deadline(options.Timeout),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return CliResult.Success(name, Json(info, HostJsonContext.Default.SessionInfo), "restarted " + Describe(info));
    }

    private async Task<CliResult> ListAsync(CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var list = await ListSessionsAsync(options, cancellationToken).ConfigureAwait(false);
        var current = Files.TryReadCurrent();
        var text = new StringBuilder();
        foreach (var info in list.Sessions)
        {
            if (text.Length > 0)
            {
                text.AppendLine();
            }

            text.Append(string.Equals(info.Name, current, StringComparison.Ordinal) ? "* " : "  ").Append(Describe(info));
        }

        var data = Json(list, HostJsonContext.Default.ListResult);
        return CliResult.Success(null, data, list.Sessions.Count == 0 ? "no sessions" : text.ToString());
    }

    private async Task<ListResult> ListSessionsAsync(CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var client = await connector.ConnectExistingAsync(cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return new ListResult([]);
        }

        await using (client.ConfigureAwait(false))
        {
            return await client.RequestAsync(
                HostMethods.List,
                new EmptyParams(),
                HostJsonContext.Default.EmptyParams,
                HostJsonContext.Default.ListResult,
                Deadline(options.Timeout),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CliResult> UseAsync(UseRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var list = await ListSessionsAsync(options, cancellationToken).ConfigureAwait(false);
        if (!list.Sessions.Any(info => string.Equals(info.Name, request.Name, StringComparison.Ordinal)))
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.SessionNotFound, $"There is no session named '{request.Name}'.");
        }

        Files.WriteCurrent(request.Name);
        return CliResult.Success(request.Name, Json(new UseResult(request.Name), HostJsonContext.Default.UseResult), $"current session: {request.Name}");
    }

    private async Task<CliResult> HostStatusAsync(CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var client = await connector.ConnectExistingAsync(cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return CliResult.Success(null, Json(new HostStatusView(false), HostJsonContext.Default.HostStatusView), "the session host is not running");
        }

        await using (client.ConfigureAwait(false))
        {
            var status = await client.RequestAsync(
                HostMethods.Status,
                new EmptyParams(),
                HostJsonContext.Default.EmptyParams,
                HostJsonContext.Default.HostStatusResult,
                Deadline(options.Timeout),
                cancellationToken).ConfigureAwait(false);
            var view = new HostStatusView(true, status.Pid, status.Version, status.Protocol, status.Sessions, status.Clients, status.StartedAt, status.IdleTimeoutMs);
            var text = $"the session host is running (pid {status.Pid.ToString(CultureInfo.InvariantCulture)}, version {status.Version}, {status.Sessions.ToString(CultureInfo.InvariantCulture)} session(s))";
            return CliResult.Success(null, Json(view, HostJsonContext.Default.HostStatusView), text);
        }
    }

    private async Task<CliResult> HostStopAsync(CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var client = await connector.ConnectExistingAsync(cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return CliResult.Success(null, Json(new HostStopView(false, 0), HostJsonContext.Default.HostStopView), "the session host is not running");
        }

        await using (client.ConfigureAwait(false))
        {
            var result = await client.RequestAsync(
                HostMethods.Shutdown,
                new EmptyParams(),
                HostJsonContext.Default.EmptyParams,
                HostJsonContext.Default.HostShutdownResult,
                Deadline(options.Timeout),
                cancellationToken).ConfigureAwait(false);
            if (Files.TryReadCurrent() is { } current)
            {
                Files.ClearCurrent(current);
            }

            var text = $"stopped the session host and {result.StoppedSessions.ToString(CultureInfo.InvariantCulture)} session(s)";
            return CliResult.Success(null, Json(new HostStopView(true, result.StoppedSessions), HostJsonContext.Default.HostStopView), text);
        }
    }

    private async Task<CliResult> HostRunAsync(HostRunRequest request, CancellationToken cancellationToken)
    {
        var host = new SessionHost(Files, request.IdleTimeout);
        var registrations = new List<PosixSignalRegistration>();
        if (!OperatingSystem.IsWindows())
        {
            // The host outlives the terminal and the shell's process group: hang-ups and Ctrl+C there are not for it.
            foreach (var signal in new[] { PosixSignal.SIGHUP, PosixSignal.SIGINT, PosixSignal.SIGQUIT })
            {
                registrations.Add(PosixSignalRegistration.Create(signal, static context => context.Cancel = true));
            }

            registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                host.RequestShutdown();
            }));
        }

        try
        {
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return CliResult.Success(null, null, null);
        }
        finally
        {
            foreach (var registration in registrations)
            {
                registration.Dispose();
            }
        }
    }

    private async Task<CliResult> SessionAsync(CliRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var name = ResolveSession(options);
        var json = HostJsonContext.Default;
        var deadline = Deadline(options.Timeout);
        var (data, text) = await OnHostAsync(
            name,
            async client =>
            {
                switch (request)
                {
                    case ResizeRequest resize:
                    {
                        var result = await client.RequestAsync(HostMethods.Resize, new ResizeParams(name, resize.Width, resize.Height), json.ResizeParams, json.ResizeResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.ResizeResult), $"resized {name} to {result.Width.ToString(CultureInfo.InvariantCulture)}x{result.Height.ToString(CultureInfo.InvariantCulture)}");
                    }

                    case KeysRequest keys:
                    {
                        var presses = keys.Keys.Select(KeyPress.From).ToList();
                        var parameters = new KeysParams(name, presses, keys.Element, Milliseconds(options.Timeout));
                        var result = await client.RequestAsync(HostMethods.Keys, parameters, json.KeysParams, json.SentResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.SentResult), $"sent {result.Sent.ToString(CultureInfo.InvariantCulture)} key(s)");
                    }

                    case TypeRequest type:
                    {
                        var result = await client.RequestAsync(HostMethods.Type, new TextParams(name, type.Text), json.TextParams, json.SentResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.SentResult), $"typed {result.Sent.ToString(CultureInfo.InvariantCulture)} character(s)");
                    }

                    case PasteRequest paste:
                    {
                        var result = await client.RequestAsync(HostMethods.Paste, new TextParams(name, paste.Text), json.TextParams, json.PasteResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.PasteResult), result.Bracketed ? "pasted (bracketed)" : "pasted");
                    }

                    case RawRequest raw:
                    {
                        var result = await client.RequestAsync(HostMethods.Raw, new TextParams(name, raw.Data), json.TextParams, json.RawResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.RawResult), $"sent {result.Bytes.ToString(CultureInfo.InvariantCulture)} byte(s)");
                    }

                    case ClickRequest click:
                    {
                        var parameters = new ClickParams(name, click.X, click.Y, click.Text, click.Nth, click.Button, click.Double, click.Element, Milliseconds(options.Timeout));
                        var result = await client.RequestAsync(HostMethods.Click, parameters, json.ClickParams, json.ClickResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.ClickResult), $"clicked ({result.X.ToString(CultureInfo.InvariantCulture)},{result.Y.ToString(CultureInfo.InvariantCulture)})");
                    }

                    case MouseRequest mouse:
                    {
                        var parameters = new MouseParams(name, mouse.Kind, mouse.X, mouse.Y, mouse.Button, mouse.Delta);
                        var result = await client.RequestAsync(HostMethods.Mouse, parameters, json.MouseParams, json.MouseResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.MouseResult), $"mouse {mouse.Kind.ToString().ToLowerInvariant()} at ({result.X.ToString(CultureInfo.InvariantCulture)},{result.Y.ToString(CultureInfo.InvariantCulture)})");
                    }

                    case ScreenRequest screen:
                    {
                        var parameters = new PtyScreenParams(name, screen.Format == ScreenFormat.Json, screen.Format == ScreenFormat.Ansi, screen.Region);
                        var result = await client.RequestAsync(HostMethods.Screen, parameters, json.PtyScreenParams, json.PtyScreenResult, deadline, cancellationToken).ConfigureAwait(false);
                        var screenText = result.Ansi ?? string.Join('\n', result.Lines.Select(static line => line.TrimEnd()));
                        return (Json(result, json.PtyScreenResult), screenText);
                    }

                    case FramesRequest frames:
                    {
                        var result = await client.RequestAsync(HostMethods.Frames, new FramesParams(name, frames.Last), json.FramesParams, json.FramesResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.FramesResult), FramesText(result));
                    }

                    case DiffRequest diff:
                    {
                        var result = await client.RequestAsync(HostMethods.Diff, new DiffParams(name, diff.From, diff.To), json.DiffParams, json.DiffResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.DiffResult), DiffText(result));
                    }

                    case OutputRequest output:
                    {
                        var result = await client.RequestAsync(HostMethods.Output, new OutputParams(name, output.Since, output.MaxBytes), json.OutputParams, json.OutputResult, deadline, cancellationToken).ConfigureAwait(false);
                        return (Json(result, json.OutputResult), result.Data);
                    }

                    default:
                        throw new InvalidOperationException($"{request.GetType().Name} is not a session request.");
                }
            },
            cancellationToken).ConfigureAwait(false);
        return CliResult.Success(name, data, text);
    }

    private static string FramesText(FramesResult result)
    {
        var text = new StringBuilder();
        foreach (var frame in result.Frames)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(CultureInfo.InvariantCulture, $"--- frame {frame.Number} at {frame.ObservedAt:O} ---");
            foreach (var line in frame.Lines)
            {
                text.Append('\n').Append(line.TrimEnd());
            }
        }

        return text.ToString();
    }

    private static string DiffText(DiffResult result)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"frame {result.From} -> {result.To}: {result.Changes.Count} changed line(s)");
        foreach (var change in result.Changes)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n@{change.Line}");
            if (change.Before is not null)
            {
                text.Append("\n- ").Append(change.Before.TrimEnd());
            }

            if (change.After is not null)
            {
                text.Append("\n+ ").Append(change.After.TrimEnd());
            }
        }

        return text.ToString();
    }

    private async Task<CliResult> AttachAsync(AttachRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        // Like start, attach starts the host when none runs.
        var client = await connector.ConnectOrStartAsync(cancellationToken).ConfigureAwait(false);
        await using (client.ConfigureAwait(false))
        {
            var parameters = new AttachParams(request.ProcessId, request.Name, Milliseconds(options.Timeout));
            var info = await client.RequestAsync(HostMethods.Attach, parameters, HostJsonContext.Default.AttachParams, HostJsonContext.Default.SessionInfo, Deadline(options.Timeout), cancellationToken).ConfigureAwait(false);
            Files.WriteCurrent(info.Name);
            return CliResult.Success(info.Name, Json(info, HostJsonContext.Default.SessionInfo), "attached " + Describe(info));
        }
    }

    private async Task<CliResult> DetachAsync(DetachRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var name = request.Name ?? ResolveSession(options);
        var result = await OnHostAsync(
            name,
            client => client.RequestAsync(
                HostMethods.Detach,
                new SessionParams(name),
                HostJsonContext.Default.SessionParams,
                HostJsonContext.Default.DetachResult,
                Deadline(options.Timeout),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        Files.ClearCurrent(name);
        return CliResult.Success(name, Json(result, HostJsonContext.Default.DetachResult), $"detached {name}");
    }

    private static WaitSpec ToSpec(WaitCondition condition)
        => new(condition.Kind, condition.Value, condition.Idle is { } idle ? Milliseconds(idle) : null);

    private async Task<CliResult> WaitAsync(WaitRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var name = ResolveSession(options);
        var spec = ToSpec(request.Condition);
        var json = HostJsonContext.Default;

        // The host sends progress while it waits; the answer is what the CLI reports, so it is only drained here.
        var result = await OnHostAsync(
            name,
            client => client.RequestAsync(
                HostMethods.Wait,
                new WaitParams(name, spec, Milliseconds(options.Timeout)),
                json.WaitParams,
                json.WaitResult,
                Deadline(options.Timeout),
                cancellationToken,
                static _ => { }),
            cancellationToken).ConfigureAwait(false);
        var text = $"{HostWait.Describe(spec)}: met after {result.ElapsedMs.ToString(CultureInfo.InvariantCulture)} ms";
        return CliResult.Success(name, Json(result, json.WaitResult), text);
    }

    private async Task<CliResult> AssertAsync(AssertRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var name = ResolveSession(options);
        var spec = ToSpec(request.Condition);
        var json = HostJsonContext.Default;
        var result = await OnHostAsync(
            name,
            client => client.RequestAsync(
                HostMethods.Assert,
                new AssertParams(name, spec, request.Not, Milliseconds(options.Timeout)),
                json.AssertParams,
                json.AssertResult,
                Deadline(options.Timeout),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var text = (request.Not ? "assertion passed: not " : "assertion passed: ") + HostWait.Describe(spec);
        return CliResult.Success(name, Json(result, json.AssertResult), text);
    }

    // The app-layer verbs: one call to the app's endpoint through the host, shown as text or as the app's own JSON.
    private async Task<CliResult> AppVerbAsync(CliRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
    {
        var name = ResolveSession(options);
        var app = DiagnosticsJsonContext.Default;
        var (method, parameters) = request switch
        {
            TreeRequest tree => Call(DiagnosticsAppMethods.Tree, Json(new TreeParams(tree.Selector, tree.Depth, tree.IncludeText, tree.IncludeLayout), app.TreeParams)),
            FindRequest find => Call(DiagnosticsAppMethods.Find, Json(new FindParams(find.Selector), app.FindParams)),
            InspectRequest inspect => Call(DiagnosticsAppMethods.Inspect, Json(new InspectParams(inspect.Selector), app.InspectParams)),
            InspectAtRequest at => Call(DiagnosticsAppMethods.InspectAt, Json(new InspectAtParams(at.X, at.Y), app.InspectAtParams)),
            FocusRequest { Action: FocusAction.Set } focus => Call(DiagnosticsAppMethods.FocusSet, Json(new FocusSetParams(focus.Key!), app.FocusSetParams)),
            FocusRequest { Action: FocusAction.Next } => Call(DiagnosticsAppMethods.FocusNext),
            FocusRequest { Action: FocusAction.Previous } => Call(DiagnosticsAppMethods.FocusPrevious),
            FocusRequest => Call(DiagnosticsAppMethods.Focus),
            RouteRequest { Go: { } go } => Call(DiagnosticsAppMethods.Navigate, Json(new NavigateParams(go), app.NavigateParams)),
            RouteRequest => Call(DiagnosticsAppMethods.Route),
            _ => Call(DiagnosticsAppMethods.Stats),
        };

        var raw = await OnHostAsync(name, client => AppRequestAsync(client, name, method, parameters, options.Timeout, cancellationToken), cancellationToken).ConfigureAwait(false);
        switch (request)
        {
            case TreeRequest:
                return CliResult.Success(name, raw, AppText.Tree(AppLayer.Deserialize(raw, app.TreeResult, method)));

            case FindRequest find:
            {
                var result = AppLayer.Deserialize(raw, app.FindResult, method);
                if (find.FirstOnly && result.Matches.Count > 1)
                {
                    result = result with { Matches = [result.Matches[0]] };
                    raw = Json(result, app.FindResult);
                }

                return CliResult.Success(name, raw, AppText.Find(result));
            }

            case InspectRequest:
                return CliResult.Success(name, raw, AppText.Inspect(AppLayer.Deserialize(raw, app.InspectResult, method)));

            case InspectAtRequest:
                return CliResult.Success(name, raw, AppText.InspectAt(AppLayer.Deserialize(raw, app.InspectAtResult, method)));

            case FocusRequest { Action: FocusAction.List }:
                return CliResult.Success(name, raw, AppText.Focus(AppLayer.Deserialize(raw, app.FocusResult, method)));

            case FocusRequest:
                return CliResult.Success(name, raw, AppText.FocusChange(AppLayer.Deserialize(raw, app.FocusChangeResult, method)));

            case RouteRequest { Go: not null }:
                return CliResult.Success(name, raw, AppText.Navigated(AppLayer.Deserialize(raw, app.NavigateResult, method)));

            case RouteRequest:
                return CliResult.Success(name, raw, AppText.Route(AppLayer.Deserialize(raw, app.RouteResult, method)));

            default:
                return CliResult.Success(name, raw, AppText.Stats(AppLayer.Deserialize(raw, app.StatsResult, method)));
        }

        static (string Method, JsonElement? Params) Call(string method, JsonElement? parameters = null) => (method, parameters);
    }

    private static Task<JsonElement> AppRequestAsync(HostClient client, string session, string method, JsonElement? parameters, TimeSpan timeout, CancellationToken cancellationToken)
        => client.RequestAsync(
            HostMethods.App,
            new AppParams(session, method, parameters, Milliseconds(timeout)),
            HostJsonContext.Default.AppParams,
            HostJsonContext.Default.JsonElement,
            Deadline(timeout),
            cancellationToken);

    // errors and logs: once, or with --follow as a stream of entries that ends at Ctrl+C or an explicit --timeout.
    private async Task<CliResult> EntriesAsync(CliRequest request, CliGlobalOptions options, ICliStream? stream, CancellationToken cancellationToken)
    {
        var name = ResolveSession(options);
        var app = DiagnosticsJsonContext.Default;
        var logs = request as LogsRequest;
        var errors = request as ErrorsRequest;
        var method = logs is null ? DiagnosticsAppMethods.Errors : DiagnosticsAppMethods.Logs;
        var items = logs is null ? "errors" : "entries";
        var follow = logs?.Follow ?? errors!.Follow;
        var since = logs?.Since ?? errors?.Since;

        JsonElement Parameters(long? cursor) => logs is null
            ? Json(new ErrorsParams(cursor), app.ErrorsParams)
            : Json(new LogsParams(cursor, logs.Level, logs.Category), app.LogsParams);
        string Line(JsonElement item) => logs is null ? AppText.ErrorLine(item) : AppText.LogLine(item);

        if (!follow || stream is null)
        {
            var raw = await OnHostAsync(name, client => AppRequestAsync(client, name, method, Parameters(since), options.Timeout, cancellationToken), cancellationToken).ConfigureAwait(false);
            var lines = raw.GetProperty(items).EnumerateArray().Select(Line).ToList();
            return CliResult.Success(name, raw, lines.Count == 0 ? (logs is null ? "no errors" : "no log entries") : string.Join('\n', lines));
        }

        // The default timeout is for single calls; only a timeout the user chose bounds a follow.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Timeout != CliGlobalOptions.DefaultTimeout)
        {
            limit.CancelAfter(options.Timeout);
        }

        return await OnHostAsync(
            name,
            async client =>
            {
                try
                {
                    while (true)
                    {
                        var raw = await AppRequestAsync(client, name, method, Parameters(since), CliGlobalOptions.DefaultTimeout, limit.Token).ConfigureAwait(false);
                        foreach (var item in raw.GetProperty(items).EnumerateArray())
                        {
                            await stream.WriteAsync(item, Line(item)).ConfigureAwait(false);
                        }

                        since = raw.GetProperty("next").GetInt64();
                        await Task.Delay(FollowInterval, limit.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (limit.IsCancellationRequested)
                {
                    // Ctrl+C or the chosen timeout ends the stream normally.
                    return CliResult.Success(name, null, null);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private string ResolveSession(CliGlobalOptions options)
        => options.Session ?? Files.TryReadCurrent() ?? throw NoSession(null);

    // Runs one call against a running host; without one, the session cannot exist.
    private async Task<T> OnHostAsync<T>(string session, Func<HostClient, Task<T>> call, CancellationToken cancellationToken)
    {
        var client = await connector.ConnectExistingAsync(cancellationToken).ConfigureAwait(false) ?? throw NoSession(session);
        await using (client.ConfigureAwait(false))
        {
            return await call(client).ConfigureAwait(false);
        }
    }
}
