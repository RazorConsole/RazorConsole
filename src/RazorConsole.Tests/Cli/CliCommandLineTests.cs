// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Commands;
using RazorConsole.Cli.Operations;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Input;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// Every verb turns its tokens into the expected request, and every rule the parser cannot express becomes a usage
/// error instead of a request.
/// </summary>
public sealed class CliCommandLineTests
{
    private static readonly string CurrentDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rc-cli-cwd"));

    [Fact]
    public void GlobalOptions_HaveDefaults()
    {
        var outcome = Parse("list");

        outcome.Request.ShouldBe(new ListRequest());
        outcome.Options.ShouldBe(new CliGlobalOptions(false, null, CliGlobalOptions.DefaultTimeout));
    }

    [Fact]
    public void GlobalOptions_AreAcceptedBeforeAndAfterTheVerb()
    {
        Parse("--json", "-s", "app.1", "list", "--timeout", "1.5s").Options
            .ShouldBe(new CliGlobalOptions(true, "app.1", TimeSpan.FromMilliseconds(1500)));
        Parse("list", "--session", "s2", "--json").Options
            .ShouldBe(new CliGlobalOptions(true, "s2", CliGlobalOptions.DefaultTimeout));
    }

    [Fact]
    public void Session_FallsBackToTheEnvironmentVariable_AndTheOptionWins()
    {
        Parse(new Dictionary<string, string> { [CliCommandLine.SessionEnvironmentVariable] = "from-env" }, "list")
            .Options.Session.ShouldBe("from-env");
        Parse(new Dictionary<string, string> { [CliCommandLine.SessionEnvironmentVariable] = "from-env" }, "list", "-s", "s1")
            .Options.Session.ShouldBe("s1");
        ShouldBeUsageError(
            Parse(new Dictionary<string, string> { [CliCommandLine.SessionEnvironmentVariable] = "../evil" }, "list"),
            CliCommandLine.SessionEnvironmentVariable);
    }

    [Theory]
    [InlineData("-s", "../x")]
    [InlineData("-s", "-x")]
    [InlineData("--timeout", "10")]
    [InlineData("--timeout", "2d")]
    [InlineData("--timeout", "25h")]
    public void InvalidGlobalOptions_AreUsageErrors(string option, string value)
        => ShouldBeUsageError(Parse("list", option, value));

    [Fact]
    public void NoVerb_UnknownVerb_AndHostWithoutSubcommand_AreUsageErrors()
    {
        ShouldBeUsageError(Parse());
        ShouldBeUsageError(Parse("bogus"));
        ShouldBeUsageError(Parse("host"));
        ShouldBeUsageError(Parse("list", "--bogus"));
    }

    [Fact]
    public void HelpAndVersion_AreBuiltIns()
    {
        Parse("--help").BuiltIn.ShouldNotBeNull();
        Parse("click", "-h").BuiltIn.ShouldNotBeNull();
        Parse("--version").BuiltIn.ShouldNotBeNull();
        Parse("--help").Request.ShouldBeNull();
    }

    [Fact]
    public void JsonIsHonoured_EvenWhenTheCommandLineDoesNotParse()
    {
        Parse("--json", "bogus").Options.Json.ShouldBeTrue();
        Parse("start", "--json").Options.Json.ShouldBeTrue();
        Parse("start", "--exe", "a", "--", "--json").Options.Json.ShouldBeFalse();
    }

    [Fact]
    public void Start_BuildsTheRequest_WithFullPathsAndTheArgumentsAfterTheSeparator()
    {
        var request = Parse(
            "start", "demo", "--exe", "bin/app.exe", "--width", "80", "--height", "24", "--env", "A=1", "--env", "B=",
            "--cwd", "work", "--term", "xterm", "--no-wait", "--", "--json", "-s", "x", "--").Request.ShouldBeOfType<StartRequest>();

        request.Name.ShouldBe("demo");
        request.TargetKind.ShouldBe(StartTargetKind.Exe);
        request.TargetPath.ShouldBe(Path.Combine(CurrentDirectory, "bin", "app.exe"));
        request.Arguments.ShouldBe(["--json", "-s", "x", "--"]);
        request.Width.ShouldBe(80);
        request.Height.ShouldBe(24);
        request.Environment.ShouldBe(new Dictionary<string, string> { ["A"] = "1", ["B"] = string.Empty });
        request.WorkingDirectory.ShouldBe(Path.Combine(CurrentDirectory, "work"));
        request.TerminalType.ShouldBe("xterm");
        request.NoWait.ShouldBeTrue();
    }

    [Fact]
    public void Start_HasDefaults_AndAcceptsDllAndProject()
    {
        var dll = Parse("start", "--dll", "app.dll").Request.ShouldBeOfType<StartRequest>();
        dll.ShouldBe(dll with
        {
            Name = null,
            TargetKind = StartTargetKind.Dll,
            TargetPath = Path.Combine(CurrentDirectory, "app.dll"),
            Width = StartRequest.DefaultWidth,
            Height = StartRequest.DefaultHeight,
            WorkingDirectory = null,
            TerminalType = StartRequest.DefaultTerminalType,
            NoWait = false,
        });
        dll.Arguments.ShouldBeEmpty();
        dll.Environment.ShouldBeEmpty();

        Parse("start", "--project", ".").Request.ShouldBeOfType<StartRequest>().TargetKind.ShouldBe(StartTargetKind.Project);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("start", "--exe", "a", "--dll", "b")]
    [InlineData("start", "--exe", "a", "--width", "0")]
    [InlineData("start", "--exe", "a", "--height", "1001")]
    [InlineData("start", "--exe", "a", "--env", "=1")]
    [InlineData("start", "--exe", "a", "--env", "NOVALUE")]
    [InlineData("start", "bad/name", "--exe", "a")]
    public void Start_RejectsInvalidCombinations(params string[] args) => ShouldBeUsageError(Parse(args));

    [Fact]
    public void Separator_OnlySplitsForStart()
    {
        Parse("type", "--", "--json").Request.ShouldBe(new TypeRequest("--json"));
        Parse("type", "--", "--json").Options.Json.ShouldBeFalse();
    }

    [Fact]
    public void Stop_UsesFiveSecondsUnlessTimeoutIsGiven()
    {
        Parse("stop").Request.ShouldBe(new StopRequest(null, StopRequest.DefaultTimeout));
        Parse("stop", "s2", "--timeout", "30s").Request.ShouldBe(new StopRequest("s2", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void SessionVerbs_BuildTheirRequests()
    {
        Parse("restart", "s1").Request.ShouldBe(new RestartRequest("s1"));
        Parse("restart").Request.ShouldBe(new RestartRequest(null));
        Parse("resize", "100", "30").Request.ShouldBe(new ResizeRequest(100, 30));
        Parse("use", "s3").Request.ShouldBe(new UseRequest("s3"));
        Parse("attach", "4242").Request.ShouldBe(new AttachRequest(4242, null));
        Parse("attach", "4242", "app").Request.ShouldBe(new AttachRequest(4242, "app"));
        Parse("detach").Request.ShouldBe(new DetachRequest(null));
        Parse("detach", "app").Request.ShouldBe(new DetachRequest("app"));
    }

    [Theory]
    [InlineData("resize", "0", "10")]
    [InlineData("resize", "10")]
    [InlineData("use")]
    [InlineData("use", "a/b")]
    [InlineData("attach", "0")]
    [InlineData("attach", "abc")]
    public void SessionVerbs_RejectInvalidArguments(params string[] args) => ShouldBeUsageError(Parse(args));

    [Fact]
    public void Keys_ParsesEveryKey()
    {
        var request = Parse("keys", "Tab", "Shift+Tab", "Ctrl+C", "a", "--element", "#name").Request.ShouldBeOfType<KeysRequest>();

        request.Element.ShouldBe("#name");
        request.Keys.ShouldBe(
        [
            new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false),
            new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false),
            new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true),
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
        ]);
    }

    [Theory]
    [InlineData("keys")]
    [InlineData("keys", "Tab", "NotAKey")]
    public void Keys_RejectsMissingOrUnknownKeys(params string[] args) => ShouldBeUsageError(Parse(args));

    [Fact]
    public void TextInput_BuildsRequests_AndRawDecodesEscapes()
    {
        Parse("type", "hello world").Request.ShouldBe(new TypeRequest("hello world"));
        Parse("paste", "a\nb").Request.ShouldBe(new PasteRequest("a\nb"));
        Parse("raw", @"\e[A\x41\u00e9\r").Request.ShouldBe(new RawRequest("\u001b[AAé\r"));
        ShouldBeUsageError(Parse("raw", @"\q"));
        ShouldBeUsageError(Parse("type"));
    }

    [Fact]
    public void Click_AcceptsExactlyOneTarget()
    {
        Parse("click", "3", "4").Request.ShouldBe(new ClickRequest(3, 4, null, 1, null, 0, false));
        Parse("click", "--text", "OK", "--nth", "2", "--button", "right", "--double").Request
            .ShouldBe(new ClickRequest(null, null, "OK", 2, null, 2, true));
        Parse("click", "--element", "button#save", "--button", "middle").Request
            .ShouldBe(new ClickRequest(null, null, null, 1, "button#save", 1, false));
    }

    [Theory]
    [InlineData("click")]
    [InlineData("click", "3")]
    [InlineData("click", "3", "4", "--text", "OK")]
    [InlineData("click", "--text", "OK", "--element", "#a")]
    [InlineData("click", "3", "4", "--nth", "2")]
    [InlineData("click", "--text", "OK", "--nth", "0")]
    [InlineData("click", "-1", "4")]
    [InlineData("click", "3", "4", "--button", "fourth")]
    [InlineData("click", "a", "4")]
    [InlineData("mouse", "wheel", "1", "2", "--delta", "x")]
    public void Click_RejectsInvalidTargets(params string[] args) => ShouldBeUsageError(Parse(args));

    [Fact]
    public void Mouse_BuildsRequests()
    {
        Parse("mouse", "move", "1", "2").Request.ShouldBe(new MouseRequest(TerminalMouseKind.Move, 1, 2, 0, 3));
        Parse("mouse", "down", "1", "2", "--button", "right").Request.ShouldBe(new MouseRequest(TerminalMouseKind.Down, 1, 2, 2, 3));
        Parse("mouse", "up", "1", "2").Request.ShouldBe(new MouseRequest(TerminalMouseKind.Up, 1, 2, 0, 3));
        Parse("mouse", "wheel", "5", "6", "--delta", "-2").Request.ShouldBe(new MouseRequest(TerminalMouseKind.Wheel, 5, 6, 0, -2));
    }

    [Theory]
    [InlineData("mouse", "drag", "1", "2")]
    [InlineData("mouse", "move", "1")]
    [InlineData("mouse", "wheel", "1", "2", "--delta", "0")]
    [InlineData("mouse", "move", "1", "2", "--delta", "1")]
    public void Mouse_RejectsInvalidEvents(params string[] args) => ShouldBeUsageError(Parse(args));

    [Fact]
    public void ScreenVerbs_BuildRequests()
    {
        Parse("screen").Request.ShouldBe(new ScreenRequest(ScreenFormat.Text, null));
        Parse("screen", "--format", "json", "--region", "0,1,40,10").Request
            .ShouldBe(new ScreenRequest(ScreenFormat.Json, new DiagnosticsRegion(0, 1, 40, 10)));
        Parse("screen", "--format", "ansi").Request.ShouldBe(new ScreenRequest(ScreenFormat.Ansi, null));
        Parse("frames").Request.ShouldBe(new FramesRequest(1));
        Parse("frames", "--last", "50").Request.ShouldBe(new FramesRequest(50));
        Parse("diff").Request.ShouldBe(new DiffRequest(null, null));
        Parse("diff", "--from", "3", "--to", "7").Request.ShouldBe(new DiffRequest(3, 7));
        Parse("output").Request.ShouldBe(new OutputRequest(null, null));
        Parse("output", "--since", "1024", "--max-bytes", "4096").Request.ShouldBe(new OutputRequest(1024, 4096));
    }

    [Theory]
    [InlineData("screen", "--format", "svg")]
    [InlineData("screen", "--region", "0,0,0,1")]
    [InlineData("screen", "--region", "0,0,10")]
    [InlineData("frames", "--last", "0")]
    [InlineData("frames", "--last", "51")]
    [InlineData("frames", "--last", "many")]
    [InlineData("output", "--max-bytes", "1k")]
    [InlineData("diff", "--from", "-1")]
    [InlineData("output", "--max-bytes", "0")]
    public void ScreenVerbs_RejectInvalidOptions(params string[] args) => ShouldBeUsageError(Parse(args));

    [Fact]
    public void WaitAndAssert_TakeExactlyOneCondition()
    {
        Parse("wait", "--text", "Ready").Request.ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Text, "Ready")));
        Parse("wait", "--regex", "^Count: \\d+").Request.ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Regex, "^Count: \\d+")));
        Parse("wait", "--gone", "Loading").Request.ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Gone, "Loading")));
        Parse("wait", "--idle", "300ms").Request
            .ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Idle, Idle: TimeSpan.FromMilliseconds(300))));
        Parse("wait", "--exit").Request.ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Exit)));
        Parse("wait", "--element", "button").Request.ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Element, "button")));
        Parse("wait", "--focus", "name").Request.ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Focus, "name")));
        Parse("wait", "--route", "/settings").Request.ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Route, "/settings")));
        Parse("assert", "--text", "Saved").Request.ShouldBe(new AssertRequest(new WaitCondition(WaitConditionKind.Text, "Saved"), false));
        Parse("assert", "--not", "--gone", "x").Request.ShouldBe(new AssertRequest(new WaitCondition(WaitConditionKind.Gone, "x"), true));
    }

    [Theory]
    [InlineData("wait")]
    [InlineData("wait", "--text", "a", "--exit")]
    [InlineData("wait", "--regex", "(")]
    [InlineData("wait", "--idle", "fast")]
    [InlineData("assert", "--not")]
    [InlineData("wait", "--not", "--exit")]
    public void WaitAndAssert_RejectInvalidConditions(params string[] args) => ShouldBeUsageError(Parse(args));

    [Fact]
    public void AppVerbs_BuildRequests()
    {
        Parse("tree").Request.ShouldBe(new TreeRequest(null, null, true, true));
        Parse("tree", "panel", "--depth", "2", "--no-text", "--no-layout").Request.ShouldBe(new TreeRequest("panel", 2, false, false));
        Parse("find", "button").Request.ShouldBe(new FindRequest("button", false));
        Parse("find", "button", "--first").Request.ShouldBe(new FindRequest("button", true));
        Parse("find", "button", "--all").Request.ShouldBe(new FindRequest("button", false));
        Parse("inspect", "#save").Request.ShouldBe(new InspectRequest("#save"));
        Parse("inspect-at", "10", "2").Request.ShouldBe(new InspectAtRequest(10, 2));
        Parse("focus").Request.ShouldBe(new FocusRequest(FocusAction.List, null));
        Parse("focus", "--list").Request.ShouldBe(new FocusRequest(FocusAction.List, null));
        Parse("focus", "--set", "name").Request.ShouldBe(new FocusRequest(FocusAction.Set, "name"));
        Parse("focus", "--next").Request.ShouldBe(new FocusRequest(FocusAction.Next, null));
        Parse("focus", "--prev").Request.ShouldBe(new FocusRequest(FocusAction.Previous, null));
        Parse("route").Request.ShouldBe(new RouteRequest(null));
        Parse("route", "--go", "/about").Request.ShouldBe(new RouteRequest("/about"));
        Parse("errors").Request.ShouldBe(new ErrorsRequest(null, false));
        Parse("errors", "--since", "5", "--follow").Request.ShouldBe(new ErrorsRequest(5, true));
        Parse("logs").Request.ShouldBe(new LogsRequest(null, null, null, false));
        Parse("logs", "--since", "9", "--level", "warning", "--category", "App", "--follow").Request
            .ShouldBe(new LogsRequest(9, "Warning", "App", true));
        Parse("stats").Request.ShouldBe(new StatsRequest());
    }

    [Theory]
    [InlineData("tree", "--depth", "-1")]
    [InlineData("tree", "--depth", "deep")]
    [InlineData("find")]
    [InlineData("find", "button", "--first", "--all")]
    [InlineData("inspect")]
    [InlineData("inspect-at", "1")]
    [InlineData("focus", "--next", "--prev")]
    [InlineData("focus", "--list", "--set", "a")]
    [InlineData("logs", "--level", "Verbose")]
    [InlineData("logs", "--level", "None")]
    [InlineData("logs", "--level", "3")]
    [InlineData("errors", "--since", "x")]
    public void AppVerbs_RejectInvalidOptions(params string[] args) => ShouldBeUsageError(Parse(args));

    [Fact]
    public void ToolVerbs_BuildRequests()
    {
        Parse("doctor").Request.ShouldBe(new DoctorRequest(false));
        Parse("doctor", "--fix").Request.ShouldBe(new DoctorRequest(true));
        Parse("host", "status").Request.ShouldBe(new HostStatusRequest());
        Parse("host", "stop").Request.ShouldBe(new HostStopRequest());
        Parse("__host").Request.ShouldBe(new HostRunRequest(HostRunRequest.DefaultIdleTimeout));
        Parse("__host", "--idle-timeout", "30s").Request.ShouldBe(new HostRunRequest(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void HostRunVerb_IsHiddenFromHelp()
    {
        var commandLine = new CliCommandLine(static _ => null, CurrentDirectory);

        commandLine.Root.Subcommands.Single(static command => command.Name == "__host").Hidden.ShouldBeTrue();
    }

    private static CliParseOutcome Parse(params string[] args) => Parse(new Dictionary<string, string>(), args);

    private static CliParseOutcome Parse(Dictionary<string, string> environment, params string[] args)
        => new CliCommandLine(name => environment.GetValueOrDefault(name), CurrentDirectory).Parse(args);

    private static void ShouldBeUsageError(CliParseOutcome outcome, string? messageContains = null)
    {
        outcome.Request.ShouldBeNull();
        outcome.BuiltIn.ShouldBeNull();
        outcome.Errors.ShouldNotBeEmpty();
        if (messageContains is not null)
        {
            outcome.Errors.ShouldContain(error => error.Contains(messageContains, StringComparison.Ordinal));
        }
    }
}
