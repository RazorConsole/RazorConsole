// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using RazorConsole.Cli.Host;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Testing;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The app layer through a real session host: pairing a PTY session with its app, the app verbs, element-based input,
/// waits and assertions, <c>logs --follow</c>, and attaching to a running app. The app is PtyApp's <c>app</c> mode.
/// </summary>
public sealed class AppLayerTests : IAsyncDisposable
{
    private const int TestTimeoutMs = 120_000;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rc-app-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<InProcessLauncher> _launchers = [];

    private HostFiles Files => new(_directory);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string AppPath => Path.Combine(AppContext.BaseDirectory, "RazorConsole.Tests.PtyApp.dll");

    public async ValueTask DisposeAsync()
    {
        foreach (var launcher in _launchers)
        {
            await launcher.StopAllAsync();
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task AppSession_PairsByTokenAndAnswersTheAppVerbs()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "PTY is not supported on this OS.");
        var operations = Operations();
        var options = Options();
        try
        {
            Succeeded(await StartAsync(operations, options, "app"));

            // Straight after start the app may not have registered yet: the host waits for it within the timeout.
            var tree = await operations.ExecuteAsync(new TreeRequest(null, null, IncludeText: true, IncludeLayout: true), options, Token);
            Succeeded(tree);
            tree.Text.ShouldNotBeNull().ShouldContain("Save");

            var list = await operations.ExecuteAsync(new ListRequest(), options, Token);
            var layer = list.Data!.Value.GetProperty("sessions")[0].GetProperty("appLayer");
            layer.GetProperty("connected").GetBoolean().ShouldBeTrue();
            layer.GetProperty("pipeline").GetString().ShouldBe("WidgetLayout");
            list.Text.ShouldNotBeNull().ShouldContain("app layer connected");

            var find = await operations.ExecuteAsync(new FindRequest("hook:save", FirstOnly: false), options, Token);
            Succeeded(find);
            var match = find.Data!.Value.GetProperty("matches").EnumerateArray().ShouldHaveSingleItem();
            match.GetProperty("focusKey").GetString().ShouldBe("save");
            var rect = match.GetProperty("rect");

            var inspect = await operations.ExecuteAsync(new InspectRequest("hook:save"), options, Token);
            Succeeded(inspect);
            inspect.Text.ShouldNotBeNull().ShouldContain("focus=save");

            // In a PTY session the cell is the one the terminal shows.
            var at = await operations.ExecuteAsync(new InspectAtRequest(rect.GetProperty("left").GetInt32(), rect.GetProperty("top").GetInt32()), options, Token);
            Succeeded(at);
            at.Data!.Value.GetProperty("cell").GetProperty("text").GetString().ShouldNotBeNull();
            at.Data!.Value.GetProperty("stack").GetArrayLength().ShouldBeGreaterThan(0);

            var stats = await operations.ExecuteAsync(new StatsRequest(), options, Token);
            Succeeded(stats);
            stats.Data!.Value.GetProperty("pipeline").GetString().ShouldBe("WidgetLayout");

            var focus = await operations.ExecuteAsync(new FocusRequest(FocusAction.List, null), options, Token);
            Succeeded(focus);
            focus.Data!.Value.GetProperty("targets").EnumerateArray().Select(static target => target.GetProperty("key").GetString()).ShouldBe(["save", "cancel"]);

            // Moving focus is a state change in the app: no key reaches it.
            var next = await operations.ExecuteAsync(new FocusRequest(FocusAction.Next, null), options, Token);
            Succeeded(next);
            next.Data!.Value.GetProperty("current").GetString().ShouldBe("cancel");
            await WaitForTextAsync(operations, options, "[Cancel]");

            var set = await operations.ExecuteAsync(new FocusRequest(FocusAction.Set, "save"), options, Token);
            Succeeded(set);
            set.Data!.Value.GetProperty("current").GetString().ShouldBe("save");
            await WaitForTextAsync(operations, options, "[Save]");

            var missing = await operations.ExecuteAsync(new FocusRequest(FocusAction.Set, "nope"), options, Token);
            missing.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.ElementNotFound);

            var route = await operations.ExecuteAsync(new RouteRequest("counter"), options, Token);
            Succeeded(route);
            route.Data!.Value.GetProperty("relative").GetString().ShouldBe("counter");
            await WaitForTextAsync(operations, options, "route: counter");
            Succeeded(await operations.ExecuteAsync(new RouteRequest(null), options, Token));

            var logs = await operations.ExecuteAsync(new LogsRequest(null, null, null, false), options, Token);
            Succeeded(logs);
            logs.Data!.Value.GetProperty("entries").EnumerateArray().Select(static entry => entry.GetProperty("message").GetString()).ShouldContain("app ready");

            var errors = await operations.ExecuteAsync(new ErrorsRequest(null, false), options, Token);
            Succeeded(errors);
            errors.Data!.Value.GetProperty("errors").GetArrayLength().ShouldBe(0);

            Succeeded(await operations.ExecuteAsync(new StopRequest(null, TimeSpan.FromSeconds(10)), options, Token));
        }
        finally
        {
            await StopHostAsync(operations, options);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ElementInput_Waits_AndAssertions_RunInTheHost()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "PTY is not supported on this OS.");
        var operations = Operations();
        var options = Options();
        try
        {
            Succeeded(await StartAsync(operations, options, "app"));

            var click = await operations.ExecuteAsync(new ClickRequest(null, null, null, 1, "hook:save", 0, false), options, Token);
            Succeeded(click);
            Succeeded(await operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Text, "clicked: 1")), options, Token));

            // keys --element focuses the element through the app, then the keys go through the terminal.
            var keys = await operations.ExecuteAsync(new KeysRequest([new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)], "hook:cancel"), options, Token);
            Succeeded(keys);
            Succeeded(await operations.ExecuteAsync(new AssertRequest(new WaitCondition(WaitConditionKind.Focus, "cancel"), Not: false), options, Token));

            Succeeded(await operations.ExecuteAsync(new FocusRequest(FocusAction.Set, "save"), options, Token));
            Succeeded(await operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Focus, "save")), options, Token));
            Succeeded(await operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Element, "hook:cancel")), options, Token));
            Succeeded(await operations.ExecuteAsync(new RouteRequest("other"), options, Token));
            Succeeded(await operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Route, "other")), options, Token));
            Succeeded(await operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Regex, @"clicked: \d")), options, Token));
            Succeeded(await operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Idle, Idle: TimeSpan.FromMilliseconds(300))), options, Token));

            var notFound = await operations.ExecuteAsync(new ClickRequest(null, null, null, 1, "hook:missing", 0, false), options, Token);
            notFound.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.ElementNotFound);
            CliExitCodes.For(notFound.Error.Code).ShouldBe(CliExitCodes.NotFound);

            Succeeded(await operations.ExecuteAsync(new AssertRequest(new WaitCondition(WaitConditionKind.Text, "never on screen"), Not: true), options, Token));

            // A failed assertion carries the screen and a short tree.
            var failed = await operations.ExecuteAsync(new AssertRequest(new WaitCondition(WaitConditionKind.Text, "never on screen"), Not: false), options, Token);
            failed.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.AssertionFailed);
            CliExitCodes.For(failed.Error.Code).ShouldBe(CliExitCodes.AssertionFailed);
            failed.Error.Data!.Value.GetProperty("screen").GetString().ShouldNotBeNull().ShouldContain("App layer view");
            failed.Error.Data!.Value.GetProperty("tree").GetProperty("root").ValueKind.ShouldBe(JsonValueKind.Object);

            // A timed-out wait says what the screen showed.
            var timedOut = await operations.ExecuteAsync(
                new WaitRequest(new WaitCondition(WaitConditionKind.Text, "never on screen")),
                options with { Timeout = TimeSpan.FromMilliseconds(600) },
                Token);
            timedOut.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.Timeout);
            CliExitCodes.For(timedOut.Error.Code).ShouldBe(CliExitCodes.Timeout);
            timedOut.Error.Data!.Value.GetProperty("screen").GetString().ShouldNotBeNull().ShouldContain("App layer view");

            // Cancelling a wait (Ctrl+C) cancels it in the host too, which keeps serving.
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
            cancel.CancelAfter(TimeSpan.FromMilliseconds(400));
            await Should.ThrowAsync<OperationCanceledException>(
                () => operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Text, "never on screen")), options with { Timeout = TimeSpan.FromSeconds(60) }, cancel.Token));
            Succeeded(await operations.ExecuteAsync(new ListRequest(), options, Token));

            Succeeded(await operations.ExecuteAsync(new StopRequest(null, TimeSpan.FromSeconds(10)), options, Token));
        }
        finally
        {
            await StopHostAsync(operations, options);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task LogsFollow_StreamsNewEntries_UntilTheChosenTimeout()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "PTY is not supported on this OS.");
        var operations = Operations();
        var options = Options();
        try
        {
            Succeeded(await StartAsync(operations, options, "app"));

            var stream = new ListStream();
            var follow = operations.ExecuteAsync(new LogsRequest(null, null, null, Follow: true), options with { Timeout = TimeSpan.FromSeconds(8) }, Token, stream);
            await UntilAsync(() => stream.Lines.Any(static line => line.Contains("app ready", StringComparison.Ordinal)), "the first log entry");

            Succeeded(await operations.ExecuteAsync(new ClickRequest(null, null, null, 1, "hook:save", 0, false), options, Token));
            await UntilAsync(() => stream.Lines.Any(static line => line.Contains("saved 1", StringComparison.Ordinal)), "the entry of the click");

            // The stream ends normally at the timeout the user chose.
            var final = await follow;
            Succeeded(final);
            stream.Items.ShouldNotBeEmpty();
            foreach (var item in stream.Items)
            {
                item.GetProperty("seq").GetInt64().ShouldBeGreaterThan(0);
            }

            Succeeded(await operations.ExecuteAsync(new StopRequest(null, TimeSpan.FromSeconds(10)), options, Token));
        }
        finally
        {
            await StopHostAsync(operations, options);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task AppWithoutAnEndpoint_IsNotConnectedYet_ThenDisabled()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "PTY is not supported on this OS.");
        var operations = Operations();
        var options = Options();
        try
        {
            // PtyApp's echo mode is not a RazorConsole app, so it never registers an endpoint.
            Succeeded(await StartAsync(operations, options, "echo", "e"));

            var early = await operations.ExecuteAsync(new TreeRequest(null, null, false, false), options with { Timeout = TimeSpan.FromMilliseconds(300) }, Token);
            early.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.AppLayerUnavailable);
            early.Error.Data!.Value.GetProperty("reason").GetString().ShouldBe("not_connected_yet");
            CliExitCodes.For(early.Error.Code).ShouldBe(CliExitCodes.AppLayerUnavailable);

            // Once the app has had its first frame and the grace period, the reason changes.
            await Task.Delay(AppLayer.Grace + TimeSpan.FromMilliseconds(700), Token);
            var late = await operations.ExecuteAsync(new TreeRequest(null, null, false, false), options, Token);
            late.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.AppLayerUnavailable);
            late.Error.Data!.Value.GetProperty("reason").GetString().ShouldBe("disabled");
            late.Error.Message.ShouldContain("not a RazorConsole app");
            late.Error.Message.ShouldContain("Release");

            var list = await operations.ExecuteAsync(new ListRequest(), options, Token);
            list.Data!.Value.GetProperty("sessions")[0].GetProperty("appLayer").GetProperty("reason").GetString().ShouldBe("disabled");

            Succeeded(await operations.ExecuteAsync(new StopRequest(null, TimeSpan.FromSeconds(10)), options, Token));
        }
        finally
        {
            await StopHostAsync(operations, options);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Attach_UsesTheRendererAndTheInputQueue_AndDetachLeavesTheAppRunning()
    {
        Assert.SkipUnless(PtyTerminal.IsSupported, "PTY is not supported on this OS.");
        var operations = Operations();
        var options = Options();
        try
        {
            Succeeded(await StartAsync(operations, options, "app"));

            // list tries once and does not wait; tree waits for the app to register.
            Succeeded(await operations.ExecuteAsync(new TreeRequest(null, 1, false, false), options, Token));
            var list = await operations.ExecuteAsync(new ListRequest(), options, Token);
            Succeeded(list);

            // The app's own pid, as its endpoint reports it (the PTY's process can be a launcher of the app).
            var pid = list.Data!.Value.GetProperty("sessions")[0].GetProperty("appLayer").GetProperty("pid").GetInt32();
            var attach = await operations.ExecuteAsync(new AttachRequest(pid, null), options, Token);
            Succeeded(attach);
            attach.Session.ShouldBe("a1");
            attach.Data!.Value.GetProperty("kind").GetString().ShouldBe("attach");
            var attached = options with { Session = "a1" };

            var screen = await operations.ExecuteAsync(new ScreenRequest(ScreenFormat.Json, null), attached, Token);
            Succeeded(screen);
            screen.Data!.Value.GetProperty("source").GetString().ShouldBe("renderer");
            screen.Text.ShouldNotBeNull().ShouldContain("App layer view");

            // Input goes through the endpoint's input queue.
            Succeeded(await operations.ExecuteAsync(new KeysRequest([new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)], null), attached, Token));
            Succeeded(await operations.ExecuteAsync(new ClickRequest(null, null, "Save", 1, null, 0, false), attached, Token));
            Succeeded(await operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Text, "clicked: 1")), attached, Token));
            Succeeded(await operations.ExecuteAsync(new ClickRequest(null, null, null, 1, "hook:save", 0, false), attached, Token));
            Succeeded(await operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Text, "clicked: 2")), attached, Token));

            // The cell comes from the renderer in attach mode.
            var at = await operations.ExecuteAsync(new InspectAtRequest(0, 0), attached, Token);
            Succeeded(at);
            at.Data!.Value.GetProperty("cell").ValueKind.ShouldBe(JsonValueKind.Object);

            // There is no terminal to resize.
            var resize = await operations.ExecuteAsync(new ResizeRequest(100, 30), attached, Token);
            resize.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.TerminalLayerUnavailable);
            resize.Error.Data!.Value.GetProperty("reason").GetString().ShouldBe("attached");
            CliExitCodes.For(resize.Error.Code).ShouldBe(CliExitCodes.AppLayerUnavailable);

            Succeeded(await operations.ExecuteAsync(new DetachRequest("a1"), options, Token));
            using (var process = Process.GetProcessById(pid))
            {
                process.HasExited.ShouldBeFalse();
            }

            // The PTY session still works.
            Succeeded(await operations.ExecuteAsync(new ScreenRequest(ScreenFormat.Text, null), options with { Session = "s1" }, Token));
            Succeeded(await operations.ExecuteAsync(new StopRequest("s1", TimeSpan.FromSeconds(10)), options, Token));
        }
        finally
        {
            await StopHostAsync(operations, options);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Attach_ToAProcessWithoutAnEndpoint_OrNoProcess_FailsClearly()
    {
        var operations = Operations();
        var options = Options(TimeSpan.FromSeconds(2));
        try
        {
            using var self = Process.GetCurrentProcess();
            var disabled = await operations.ExecuteAsync(new AttachRequest(self.Id, null), options, Token);
            disabled.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.AppLayerUnavailable);
            disabled.Error.Data!.Value.GetProperty("reason").GetString().ShouldBe("disabled");

            var gone = await operations.ExecuteAsync(new AttachRequest(int.MaxValue - 1, null), options, Token);
            gone.Error.ShouldNotBeNull().Code.ShouldBe(DiagnosticsErrorCodes.ProcessExited);
            CliExitCodes.For(gone.Error.Code).ShouldBe(CliExitCodes.ProcessExited);

            var list = await operations.ExecuteAsync(new ListRequest(), options, Token);
            list.Data!.Value.GetProperty("sessions").GetArrayLength().ShouldBe(0);
        }
        finally
        {
            await StopHostAsync(operations, options);
        }
    }

    private static CliGlobalOptions Options(TimeSpan? timeout = null) => new(false, null, timeout ?? Patience);

    private static void Succeeded(CliResult result)
        => result.Error.ShouldBeNull(result.Error is { } error ? $"{error.Code}: {error.Message}" : null);

    private static Task<CliResult> StartAsync(HostOperations operations, CliGlobalOptions options, params string[] arguments)
        => operations.ExecuteAsync(
            new StartRequest(null, StartTargetKind.Dll, AppPath, arguments, 80, 12, new Dictionary<string, string>(), null, StartRequest.DefaultTerminalType, false),
            options,
            Token);

    private static async Task WaitForTextAsync(HostOperations operations, CliGlobalOptions options, string text)
        => Succeeded(await operations.ExecuteAsync(new WaitRequest(new WaitCondition(WaitConditionKind.Text, text)), options, Token));

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(50, Token);
        }
    }

    // Every test ends here, so no host lingers for its idle timeout.
    private static async Task StopHostAsync(HostOperations operations, CliGlobalOptions options)
        => Succeeded(await operations.ExecuteAsync(new HostStopRequest(), options, CancellationToken.None));

    private HostOperations Operations()
    {
        var launcher = new InProcessLauncher(TimeSpan.FromMinutes(10), "1.0.0");
        _launchers.Add(launcher);
        return new HostOperations(new HostConnector(Files, launcher, "1.0.0"));
    }

    private sealed class ListStream : ICliStream
    {
        private readonly List<(JsonElement Item, string Text)> _items = [];

        public IReadOnlyList<JsonElement> Items
        {
            get
            {
                lock (_items)
                {
                    return [.. _items.Select(static entry => entry.Item)];
                }
            }
        }

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_items)
                {
                    return [.. _items.Select(static entry => entry.Text)];
                }
            }
        }

        public Task WriteAsync(JsonElement item, string text)
        {
            lock (_items)
            {
                _items.Add((item, text));
            }

            return Task.CompletedTask;
        }
    }
}
