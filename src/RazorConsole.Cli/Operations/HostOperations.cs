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
/// Runs requests against the session host: PTY sessions, input, the screen and the host itself.
/// </summary>
/// <remarks>
/// Only <c>start</c> starts the host. Other commands connect to a running host; with none, a session command fails
/// with <c>session_not_found</c> and <c>list</c> and <c>host status</c> report nothing running. The session is
/// <c>--session</c>, then <c>RAZORCONSOLE_SESSION</c> (both already in <see cref="CliGlobalOptions.Session"/>), then
/// the <c>current</c> file. The app-layer commands, attach and the waits come in later steps.
/// </remarks>
internal sealed class HostOperations(HostConnector connector) : ICliOperations
{
    private static readonly TimeSpan BuildAllowance = TimeSpan.FromMinutes(10);

    private HostFiles Files => connector.Files;

    public async Task<CliResult> ExecuteAsync(CliRequest request, CliGlobalOptions options, CancellationToken cancellationToken)
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
                KeysRequest { Element: not null } or ClickRequest { Element: not null } => NotImplemented("--element"),
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
        return $"{info.Name} (pid {info.Pid.ToString(CultureInfo.InvariantCulture)}, {info.Width.ToString(CultureInfo.InvariantCulture)}x{info.Height.ToString(CultureInfo.InvariantCulture)}, {state}) {info.Command}";
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
                        var result = await client.RequestAsync(HostMethods.Keys, new KeysParams(name, presses), json.KeysParams, json.SentResult, deadline, cancellationToken).ConfigureAwait(false);
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
                        var parameters = new ClickParams(name, click.X, click.Y, click.Text, click.Nth, click.Button, click.Double);
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
