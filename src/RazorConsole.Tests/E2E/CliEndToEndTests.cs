// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using RazorConsole.Testing;

namespace RazorConsole.Tests.E2E;

/// <summary>
/// The real <c>razorconsole</c> tool against the PtyApp <c>diag</c> mode: its own processes, its own session host started
/// on demand, real exit codes. Each test has a private state directory and ends with <c>host stop</c>.
/// </summary>
[Trait("Category", "E2E")]
public sealed class CliEndToEndTests
{
    private const int TestTimeoutMs = 240_000;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeoutMs)]
    public async Task FullLoop_StartWaitTreeClickAssertScreenStop()
    {
        await using var cli = CliE2E.Create();
        var app = CliE2E.PtyAppPath;

        var start = Ok(await cli.RunAsync("--json", "start", "--dll", app, "--", "diag"));
        start.GetProperty("name").GetString().ShouldBe("s1");
        start.GetProperty("kind").GetString().ShouldBe("pty");
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));

        // The app layer: tree, find, inspect, and inspect-at with the cell the terminal shows.
        var tree = await cli.RunAsync("--json", "tree");
        Ok(tree);
        tree.Output.ShouldContain("\"hook\":\"save\"");

        var find = Ok(await cli.RunAsync("--json", "find", "hook:save"));
        var save = find.GetProperty("matches").EnumerateArray().ShouldHaveSingleItem();
        save.GetProperty("focusKey").GetString().ShouldBe("save");
        var rect = save.GetProperty("rect");
        Ok(await cli.RunAsync("--json", "inspect", "hook:save"));

        var at = Ok(await cli.RunAsync("--json", "inspect-at", rect.GetProperty("left").GetInt32().ToString(), rect.GetProperty("top").GetInt32().ToString()));
        at.GetProperty("cell").GetProperty("text").GetString().ShouldNotBeNull();
        at.GetProperty("stack").GetArrayLength().ShouldBeGreaterThan(0);

        // A click on the element reaches the app as real mouse input through the PTY.
        var click = Ok(await cli.RunAsync("--json", "click", "--element", "hook:save"));
        click.GetProperty("x").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        Ok(await cli.RunAsync("--json", "wait", "--text", "clicked: 1"));
        Ok(await cli.RunAsync("assert", "--text", "clicked: 1"));
        var logs = Ok(await cli.RunAsync("--json", "logs"));
        logs.GetProperty("entries").EnumerateArray().Select(static entry => entry.GetProperty("message").GetString()).ShouldContain("saved 1");
        Ok(await cli.RunAsync("--json", "errors")).GetProperty("errors").GetArrayLength().ShouldBe(0);

        // Focus is a state change in the app; its three targets are in layout order.
        Ok(await cli.RunAsync("--json", "focus")).GetProperty("targets").EnumerateArray()
            .Select(static target => target.GetProperty("key").GetString()).ShouldBe(["save", "cancel", "quit", "fail", "boom"]);
        Ok(await cli.RunAsync("--json", "focus", "--set", "cancel")).GetProperty("current").GetString().ShouldBe("cancel");
        Ok(await cli.RunAsync("--json", "focus", "--next")).GetProperty("current").GetString().ShouldBe("quit");
        Ok(await cli.RunAsync("assert", "--focus", "quit"));

        Ok(await cli.RunAsync("--json", "route", "--go", "other"));
        Ok(await cli.RunAsync("--json", "wait", "--route", "other"));
        Ok(await cli.RunAsync("--json", "route")).GetProperty("relative").GetString().ShouldBe("other");
        Ok(await cli.RunAsync("--json", "stats")).GetProperty("pipeline").GetString().ShouldBe("WidgetLayout");

        var screen = Ok(await cli.RunAsync("--json", "screen"));
        screen.GetProperty("source").GetString().ShouldBe("pty");
        screen.GetProperty("lines").EnumerateArray().Select(static line => line.GetString()).ShouldContain(line => line!.Contains("route: other", StringComparison.Ordinal));

        Ok(await cli.RunAsync("--json", "stop"));
        Ok(await cli.RunAsync("--json", "list")).GetProperty("sessions").GetArrayLength().ShouldBe(0);
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ExitCodes_MatchTheDocumentedTable()
    {
        await using var cli = CliE2E.Create();

        // No host and no session yet.
        Failed(await cli.RunAsync("--json", "tree"), 3, "session_not_found");
        Failed(await cli.RunAsync("--json", "click", "1"), 2, "usage");

        Ok(await cli.RunAsync("--json", "start", "--dll", CliE2E.PtyAppPath, "--", "diag"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));

        // A failed assertion carries the screen, in text and in JSON.
        var asserted = await cli.RunAsync("assert", "--text", "never on screen");
        asserted.ExitCode.ShouldBe(1, asserted.Describe());
        asserted.Error.ShouldContain("[assertion_failed]");
        asserted.Error.ShouldContain("--- screen ---");
        asserted.Error.ShouldContain("App layer view");
        Failed(await cli.RunAsync("--json", "assert", "--text", "never on screen"), 1, "assertion_failed")
            .GetProperty("data").GetProperty("screen").GetString().ShouldNotBeNull().ShouldContain("App layer view");

        Failed(await cli.RunAsync("--json", "wait", "--text", "never on screen", "--timeout", "1s"), 5, "timeout");
        Failed(await cli.RunAsync("--json", "inspect", "hook:nope"), 3, "element_not_found");
        Failed(await cli.RunAsync("--json", "focus", "--set", "nope"), 3, "element_not_found");

        // The app quits with code 3: waiting for the exit works, anything else says the process exited.
        Ok(await cli.RunAsync("--json", "click", "--element", "hook:quit"));
        Ok(await cli.RunAsync("--json", "wait", "--exit")).GetProperty("exitCode").GetInt32().ShouldBe(3);
        // The very first app call after the exit already says so; it does not report an unavailable app layer.
        var exited = Failed(await cli.RunAsync("--json", "tree"), 6, "process_exited");
        exited.GetProperty("data").GetProperty("exitCode").GetInt32().ShouldBe(3);
        Failed(await cli.RunAsync("--json", "keys", "Tab"), 6, "process_exited");

        // The screen and the raw output of a session that ended stay readable: that is where an app that died says why.
        Ok(await cli.RunAsync("--json", "screen")).GetProperty("lines").GetArrayLength().ShouldBeGreaterThan(0);
        Ok(await cli.RunAsync("--json", "output")).GetProperty("data").GetString().ShouldNotBeNullOrEmpty();

        Ok(await cli.RunAsync("--json", "stop")).GetProperty("exitCode").GetInt32().ShouldBe(3);
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task AppWithDiagnosticsOff_GivesExit4WithTheCauses_AndTheTerminalVerbsStillWork()
    {
        await using var cli = CliE2E.Create();

        // The opt-out makes this independent of the build configuration (the activation rule has its own tests).
        Ok(await cli.RunAsync("--json", "start", "--dll", CliE2E.PtyAppPath, "--env", "RAZORCONSOLE_DIAGNOSTICS=0", "--", "golden"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "Golden view"));

        // The verb waits for the connection only while it is "not connected yet"; after the first frame and the grace
        // period the reason is known.
        var tree = Failed(await cli.RunAsync("--json", "tree", "--timeout", "20s"), 4, "app_layer_unavailable");
        tree.GetProperty("data").GetProperty("reason").GetString().ShouldBe("disabled");
        tree.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("Release or Production build");

        var list = Ok(await cli.RunAsync("--json", "list"));
        list.GetProperty("sessions")[0].GetProperty("appLayer").GetProperty("reason").GetString().ShouldBe("disabled");

        Ok(await cli.RunAsync("--json", "screen")).GetProperty("lines").EnumerateArray()
            .Select(static line => line.GetString()).ShouldContain(line => line!.Contains("Golden view", StringComparison.Ordinal));
        Ok(await cli.RunAsync("--json", "keys", "Tab"));

        Ok(await cli.RunAsync("--json", "stop"));
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Attach_ToAnAppStartedOutsideTheHost_ThenDetach_LeavesTheAppRunning()
    {
        await using var cli = CliE2E.Create();

        // The app runs in a terminal of the test, not under the host; the state directory is how it finds the CLI.
        var options = PtyTerminal.CreateDotnetOptions(CliE2E.PtyAppPath, 100, 30, ["app"]);
        options.Environment["RAZORCONSOLE_CLI_HOME"] = cli.Home;
        options.DefaultTimeout = Patience;
        await using var terminal = await PtyTerminal.StartAsync(options, Token);
        cli.Track(terminal.ProcessId);
        await terminal.WaitForTextAsync("App layer view", cancellationToken: Token);

        // The first command starts the host, as start does.
        var attach = Ok(await cli.RunAsync("--json", "attach", terminal.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        attach.GetProperty("kind").GetString().ShouldBe("attach");
        attach.GetProperty("appLayer").GetProperty("connected").GetBoolean().ShouldBeTrue();
        attach.GetProperty("appLayer").GetProperty("pid").GetInt32().ShouldBe(terminal.ProcessId);

        // Tree and screen come from the app itself; the screen is the renderer's, not a terminal's.
        Stdout(await cli.RunAsync("tree")).ShouldContain("Save");
        var screen = Ok(await cli.RunAsync("--json", "screen"));
        screen.GetProperty("source").GetString().ShouldBe("renderer");
        screen.GetProperty("lines").EnumerateArray().Select(static line => line.GetString()).ShouldContain(line => line!.Contains("App layer view", StringComparison.Ordinal));

        // Input goes through the endpoint: the click and the Tab reach the real app, whose terminal shows the result.
        Ok(await cli.RunAsync("--json", "click", "--element", "hook:save"));
        await terminal.WaitForTextAsync("clicked: 1", cancellationToken: Token);
        Ok(await cli.RunAsync("--json", "focus", "--set", "save"));
        Ok(await cli.RunAsync("--json", "keys", "Tab"));
        Ok(await cli.RunAsync("--json", "wait", "--focus", "cancel"));
        await terminal.WaitForTextAsync("[Cancel]", cancellationToken: Token);
        Ok(await cli.RunAsync("assert", "--text", "clicked: 1"));

        // There is no terminal to resize.
        var resize = Failed(await cli.RunAsync("--json", "resize", "90", "20"), 4, "terminal_layer_unavailable");
        resize.GetProperty("data").GetProperty("reason").GetString().ShouldBe("attached");

        // Detaching, and even stopping the host, leave the app alone; the host never recorded it for a sweep.
        Ok(await cli.RunAsync("--json", "detach"));
        terminal.HasExited.ShouldBeFalse();
        Ok(await cli.RunAsync("--json", "host", "stop"));
        terminal.HasExited.ShouldBeFalse();
        var ledger = Path.Combine(cli.Home, "sessions");
        (!Directory.Exists(ledger) || Directory.GetFiles(ledger).Length == 0).ShouldBeTrue();
        IsRunning(terminal.ProcessId).ShouldBeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Doctor_RunsForReal_AndLeavesNoHostItStartedBehind()
    {
        await using var cli = CliE2E.Create();

        var first = Ok(await cli.RunAsync("--json", "doctor"));
        var checks = Checks(first);
        checks.Select(static check => check.Name).ShouldBe(["os.pty", "os.shell", "dotnet", "state.dir", "host.start", "host.socket", "pty.smoke", "apps.stale"]);
        checks.ShouldNotContain(static check => check.Status == "fail");
        Detail(checks, "host.start").ShouldStartWith("started by doctor");
        checks.Single(static check => check.Name == "pty.smoke").Status.ShouldBe("pass");
        Ok(await cli.RunAsync("--json", "host", "status")).GetProperty("running").GetBoolean().ShouldBeFalse();

        // A host that was already running stays.
        Ok(await cli.RunAsync("--json", "start", "--dll", CliE2E.PtyAppPath, "--", "diag"));
        var second = Ok(await cli.RunAsync("--json", "doctor"));
        Detail(Checks(second), "host.start").ShouldStartWith("already running");
        var status = Ok(await cli.RunAsync("--json", "host", "status"));
        status.GetProperty("running").GetBoolean().ShouldBeTrue();
        status.GetProperty("sessions").GetInt32().ShouldBe(1);

        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ResizeAndRestart_KeepTheAppLayer()
    {
        await using var cli = CliE2E.Create();
        Ok(await cli.RunAsync("--json", "start", "--dll", CliE2E.PtyAppPath, "--", "diag"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));

        Ok(await cli.RunAsync("--json", "resize", "100", "30")).GetProperty("width").GetInt32().ShouldBe(100);
        Ok(await cli.RunAsync("--json", "screen")).GetProperty("width").GetInt32().ShouldBe(100);

        // A restarted app has a new process and a new pairing token; the app verbs follow it.
        Ok(await cli.RunAsync("--json", "restart"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));
        Stdout(await cli.RunAsync("tree")).ShouldContain("Save");

        Ok(await cli.RunAsync("--json", "stop"));
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task LogsFollow_StreamsNdjsonFromARealProcess()
    {
        await using var cli = CliE2E.Create();
        Ok(await cli.RunAsync("--json", "start", "--dll", CliE2E.PtyAppPath, "--", "diag"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));

        // An explicit --timeout bounds the stream; without one it runs until Ctrl+C.
        await using var follow = cli.Start("--json", "logs", "--follow", "--timeout", "20s");
        await ReadUntilAsync(follow, "app ready");

        Ok(await cli.RunAsync("--json", "click", "--element", "hook:save"));
        var line = await ReadUntilAsync(follow, "saved 1");

        // Every streamed line is one JSON object; the run ends at its timeout with success.
        using var entry = JsonDocument.Parse(line);
        entry.RootElement.GetProperty("level").GetString().ShouldBe("Information");
        var rest = await follow.WaitForExitAsync(TimeSpan.FromSeconds(60));
        rest.ExitCode.ShouldBe(0, rest.Describe());

        Ok(await cli.RunAsync("--json", "stop"));
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ExceptionsInHandlersAndRendering_AreInErrorsAndLogs_AndDoNotEndTheProcess()
    {
        await using var cli = CliE2E.Create();
        var app = CliE2E.PtyAppPath;

        // After the first renderer exception the app no longer reacts to input (reported with this step), so each kind
        // of exception gets a session of its own.
        // An exception in a click handler is caught by the renderer and recorded.
        Ok(await cli.RunAsync("--json", "start", "handler", "--dll", app, "--", "diag"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));
        Ok(await cli.RunAsync("--json", "errors")).GetProperty("errors").GetArrayLength().ShouldBe(0);
        Ok(await cli.RunAsync("--json", "click", "--element", "hook:fail"));
        var handler = (await ErrorsAsync(cli, 1)).ShouldHaveSingleItem();
        handler.GetProperty("source").GetString().ShouldBe("render");
        handler.GetProperty("type").GetString().ShouldBe("System.InvalidOperationException");
        handler.GetProperty("message").GetString().ShouldBe("diag failure");
        handler.GetProperty("stack").GetString().ShouldNotBeNull().ShouldContain("OnFail");
        await AssertLoggedAndAliveAsync(cli, "diag failure");
        Ok(await cli.RunAsync("--json", "stop"));

        // So is one thrown while a component is created during a render.
        Ok(await cli.RunAsync("--json", "start", "render", "--dll", app, "--", "diag"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));
        Ok(await cli.RunAsync("--json", "click", "--element", "hook:boom"));
        var render = (await ErrorsAsync(cli, 1)).ShouldHaveSingleItem();
        render.GetProperty("message").GetString().ShouldBe("diag boom");
        render.GetProperty("stack").GetString().ShouldNotBeNull().ShouldContain("OnInitialized");
        await AssertLoggedAndAliveAsync(cli, "diag boom");
        Ok(await cli.RunAsync("--json", "stop"));

        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    // The exception is in the log at Error level with its stack, and neither the process nor its terminal output show it.
    private static async Task AssertLoggedAndAliveAsync(CliE2E cli, string message)
    {
        var logged = Ok(await cli.RunAsync("--json", "logs", "--level", "Error")).GetProperty("entries").EnumerateArray()
            .Select(static entry => entry.TryGetProperty("exception", out var text) ? text.GetString() : null).ToList();
        logged.ShouldContain(text => text != null && text.Contains(message, StringComparison.Ordinal));
        Ok(await cli.RunAsync("--json", "list")).GetProperty("sessions")[0].GetProperty("exited").GetBoolean().ShouldBeFalse();
        Ok(await cli.RunAsync("--json", "output", "--max-bytes", "1000000")).GetProperty("data").GetString().ShouldNotBeNull().ShouldNotContain(message);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ACrashedApp_ReportsItsReasonInProcessExited()
    {
        await using var cli = CliE2E.Create();

        // The app dies before it draws, so there is no first frame to wait for, and no endpoint to ask for errors.
        Ok(await cli.RunAsync("--json", "start", "--dll", CliE2E.PtyAppPath, "--no-wait", "--", "crash"));
        var exit = Ok(await cli.RunAsync("--json", "wait", "--exit"));
        exit.GetProperty("exitCode").GetInt32().ShouldNotBe(0);

        // .NET printed the exception to the terminal; the error of any later command carries the end of that output.
        var exited = Failed(await cli.RunAsync("--json", "keys", "Tab"), 6, "process_exited");
        var data = exited.GetProperty("data");
        data.GetProperty("exitCode").GetInt32().ShouldBe(exit.GetProperty("exitCode").GetInt32());
        data.EnumerateObject().Select(static property => property.Name).ShouldBe(["exitCode", "outputTail"]);
        var tail = data.GetProperty("outputTail").GetString().ShouldNotBeNull();
        tail.ShouldContain("diag crash");
        tail.ShouldContain("InvalidOperationException");
        tail.ShouldNotContain('\u001b');
        tail.Length.ShouldBeLessThanOrEqualTo(4096);

        // In text the same goes to stderr after the message.
        var text = await cli.RunAsync("keys", "Tab");
        text.ExitCode.ShouldBe(6, text.Describe());
        text.Error.ShouldContain("[process_exited]");
        text.Error.ShouldContain("--- output tail ---");
        text.Error.ShouldContain("diag crash");
        text.Error.ShouldNotContain("--- last errors ---");

        // The raw output and the screen of a session that ended stay readable, too.
        Ok(await cli.RunAsync("--json", "output")).GetProperty("data").GetString().ShouldNotBeNull().ShouldContain("diag crash");
        Ok(await cli.RunAsync("--json", "screen")).GetProperty("lines").EnumerateArray()
            .Select(static line => line.GetString()).ShouldContain(line => line!.Contains("InvalidOperationException", StringComparison.Ordinal));

        Ok(await cli.RunAsync("--json", "stop"));
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task AnAppThatQuitsAfterAnError_ReportsTheErrorInProcessExited()
    {
        await using var cli = CliE2E.Create();

        Ok(await cli.RunAsync("--json", "start", "--dll", CliE2E.PtyAppPath, "--", "diag"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));

        // Keys, not clicks: a mouse click whose handler throws stops the app's input, a key press does not (see the
        // report of this step). Nothing asks for the errors; the session reads them in the background.
        Ok(await cli.RunAsync("--json", "keys", "Enter", "--element", "hook:fail"));
        await Task.Delay(TimeSpan.FromSeconds(2.5), Token);

        Ok(await cli.RunAsync("--json", "keys", "Enter", "--element", "hook:quit"));
        Ok(await cli.RunAsync("--json", "wait", "--exit")).GetProperty("exitCode").GetInt32().ShouldBe(3);

        var exited = Failed(await cli.RunAsync("--json", "tree"), 6, "process_exited");
        var data = exited.GetProperty("data");
        data.GetProperty("exitCode").GetInt32().ShouldBe(3);
        var last = data.GetProperty("lastErrors").EnumerateArray().ShouldHaveSingleItem();
        last.GetProperty("source").GetString().ShouldBe("render");
        last.GetProperty("type").GetString().ShouldBe("System.InvalidOperationException");
        last.GetProperty("message").GetString().ShouldBe("diag failure");
        last.GetProperty("stack").GetString().ShouldNotBeNull().ShouldContain("OnFail");

        var text = await cli.RunAsync("tree");
        text.ExitCode.ShouldBe(6, text.Describe());
        text.Error.ShouldContain("--- last errors ---");
        text.Error.ShouldContain("System.InvalidOperationException: diag failure");

        Ok(await cli.RunAsync("--json", "stop"));
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task FirstRun_OnAFreshStateDirectory_StartsTheHost_AndDoctorPasses()
    {
        // Nothing creates the state directory for the tests: the tool has to, before it starts its host there.
        await using var first = CliE2E.Create();
        Directory.Exists(first.Home).ShouldBeFalse();
        Ok(await first.RunAsync("--json", "start", "--dll", CliE2E.PtyAppPath, "--", "diag"));
        Directory.Exists(first.Home).ShouldBeTrue();
        Ok(await first.RunAsync("--json", "host", "status")).GetProperty("running").GetBoolean().ShouldBeTrue();
        Ok(await first.RunAsync("--json", "stop"));
        Ok(await first.RunAsync("--json", "host", "stop"));

        // Doctor is the other first command people run.
        await using var second = CliE2E.Create();
        Directory.Exists(second.Home).ShouldBeFalse();
        var checks = Checks(Ok(await second.RunAsync("--json", "doctor")));
        checks.ShouldNotContain(static check => check.Status == "fail");
        Detail(checks, "host.start").ShouldStartWith("started by doctor");
        Ok(await second.RunAsync("--json", "host", "status")).GetProperty("running").GetBoolean().ShouldBeFalse();
        Ok(await second.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task SessionSelection_UsesTheOption_TheEnvironmentVariable_AndTheCurrentFile()
    {
        await using var cli = CliE2E.Create();
        var app = CliE2E.PtyAppPath;
        Ok(await cli.RunAsync("--json", "start", "alpha", "--dll", app, "--", "diag"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));
        Ok(await cli.RunAsync("--json", "start", "beta", "--dll", app, "--", "diag"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));

        // The last start is current; -s and RAZORCONSOLE_SESSION choose another.
        Ok(await cli.RunAsync("--json", "route", "--go", "on-beta")).GetProperty("session").GetString().ShouldBe("beta");
        Ok(await cli.RunAsync("--json", "-s", "alpha", "route", "--go", "on-alpha")).GetProperty("session").GetString().ShouldBe("alpha");
        var viaEnvironment = Ok(await cli.RunAsync(new Dictionary<string, string> { ["RAZORCONSOLE_SESSION"] = "alpha" }, "--json", "route"));
        viaEnvironment.GetProperty("session").GetString().ShouldBe("alpha");
        viaEnvironment.GetProperty("relative").GetString().ShouldBe("on-alpha");
        Ok(await cli.RunAsync("--json", "route")).GetProperty("relative").GetString().ShouldBe("on-beta");

        // use changes the current session; stopping it clears the file, so a command without a session fails.
        Ok(await cli.RunAsync("--json", "use", "alpha"));
        Ok(await cli.RunAsync("--json", "route")).GetProperty("relative").GetString().ShouldBe("on-alpha");
        Ok(await cli.RunAsync("--json", "stop", "alpha"));
        Failed(await cli.RunAsync("--json", "route"), 3, "session_not_found");
        Ok(await cli.RunAsync("--json", "-s", "beta", "route")).GetProperty("relative").GetString().ShouldBe("on-beta");

        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task StartProject_OfAMultiTargetingProject_PicksAFramework_AndConnectsTheAppLayer()
    {
        await using var cli = CliE2E.Create();
        var project = Path.Combine(FindRepositoryRoot(), "src", "RazorConsole.Tests.PtyApp");

        // The PtyApp targets net8.0 to net11.0, so a plain 'dotnet run' would ask which one in the pseudoterminal.
        // The test run builds Release, so the project is started with --configuration Release: an incremental build.
        // (An environment variable would not do; the build runs in the session host, which may already be running.)
        var start = Ok(await cli.RunAsync("--json", "start", "--project", project, "--configuration", "Release", "--", "diag"));

        var framework = start.GetProperty("framework").GetString();
        framework.ShouldNotBeNullOrEmpty();
        framework.ShouldStartWith("net");
        start.GetProperty("configuration").GetString().ShouldBe("Release");
        start.GetProperty("command").GetString()!.ShouldContain("--framework " + framework);
        start.GetProperty("command").GetString()!.ShouldContain("--configuration Release");

        // The app layer is up on the framework that was chosen, and the screen shows the app, not a prompt.
        Ok(await cli.RunAsync("--json", "wait", "--text", "App layer view"));
        Ok(await cli.RunAsync("--json", "tree")).GetRawText().ShouldContain("\"hook\":\"save\"");
        Ok(await cli.RunAsync("--json", "list")).GetProperty("sessions").EnumerateArray().ShouldHaveSingleItem().GetProperty("framework").GetString().ShouldBe(framework);
        Stdout(await cli.RunAsync("screen")).ShouldNotContain("target framework");

        Ok(await cli.RunAsync("--json", "stop"));
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RazorConsole.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("RazorConsole.slnx was not found above the test output.");
    }

    // The errors of the app so far, once there are at least this many (the app records them as it renders).
    private static async Task<IReadOnlyList<JsonElement>> ErrorsAsync(CliE2E cli, int count)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (true)
        {
            var errors = Ok(await cli.RunAsync("--json", "errors")).GetProperty("errors").EnumerateArray().ToList();
            if (errors.Count >= count || DateTimeOffset.UtcNow >= deadline)
            {
                errors.Count.ShouldBeGreaterThanOrEqualTo(count);
                return errors;
            }

            await Task.Delay(100, Token);
        }
    }

    // The log also holds the framework's own entries, so the stream is read up to the one that matters.
    private static async Task<string> ReadUntilAsync(CliProcess process, string text)
    {
        while (true)
        {
            var line = await process.ReadLineAsync(Patience);
            line.ShouldNotBeNull($"The stream ended before an entry with '{text}' arrived.");
            if (line.Contains(text, StringComparison.Ordinal))
            {
                return line;
            }
        }
    }

    // A successful run without --json: exit 0, and what it printed.
    private static string Stdout(CliRun run)
    {
        run.ExitCode.ShouldBe(0, run.Describe());
        return run.Output;
    }

    // A successful --json run: exit 0, and the one JSON line with ok:true.
    private static JsonElement Ok(CliRun run)
    {
        run.ExitCode.ShouldBe(0, run.Describe());
        if (run.Output.TrimStart().StartsWith('{'))
        {
            var json = run.Json;
            json.GetProperty("ok").GetBoolean().ShouldBeTrue(run.Describe());
            return json;
        }

        return default;
    }

    // A failed --json run: the exit code and the error code, and the error object returned.
    private static JsonElement Failed(CliRun run, int exitCode, string code)
    {
        run.ExitCode.ShouldBe(exitCode, run.Describe());
        var json = run.Json;
        json.GetProperty("ok").GetBoolean().ShouldBeFalse(run.Describe());
        var error = json.GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe(code, run.Describe());
        return error;
    }

    private static IReadOnlyList<(string Name, string Status, string Detail)> Checks(JsonElement json)
        => [.. json.GetProperty("checks").EnumerateArray().Select(static check => (
            check.GetProperty("name").GetString()!,
            check.GetProperty("status").GetString()!,
            check.GetProperty("detail").GetString()!))];

    private static string Detail(IReadOnlyList<(string Name, string Status, string Detail)> checks, string name)
        => checks.Single(check => check.Name == name).Detail;

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
