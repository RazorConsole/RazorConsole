// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli.Mcp;
using RazorConsole.Cli.Operations;
using RazorConsole.Core.Input;

namespace RazorConsole.Tests.Mcp;

/// <summary>
/// Tool arguments become the same CLI requests the verbs build, with the same rules; what the CLI would refuse is a
/// list of problems here.
/// </summary>
public sealed class McpArgumentMappingTests
{
    private static readonly CliGlobalOptions Defaults = new(false, null, CliGlobalOptions.DefaultTimeout);

    [Fact]
    public void SendKeys_ParsesKeyNames_AndTheElementToFocusFirst()
    {
        var bound = Bind("send_keys", """{"session":"s1","keys":["Shift+Tab","Enter","a"],"selector":"hook:save"}""");

        var request = bound.Request.ShouldBeOfType<KeysRequest>();
        request.Keys.Select(static key => (key.Key, key.Modifiers)).ShouldBe(
        [
            (ConsoleKey.Tab, ConsoleModifiers.Shift),
            (ConsoleKey.Enter, default(ConsoleModifiers)),
            (ConsoleKey.A, default(ConsoleModifiers)),
        ]);
        request.Element.ShouldBe("hook:save");
        bound.Options.Session.ShouldBe("s1");
    }

    [Fact]
    public void SendRaw_DecodesEscapes()
        => Bind("send_raw", """{"session":"s1","data":"\\e[A\\x41\\r"}""").Request.ShouldBe(new RawRequest("\u001b[AA\r"));

    [Fact]
    public void TypeAndPaste_KeepTheTextAsGiven()
    {
        Bind("type_text", """{"session":"s1","text":"héllo wörld"}""").Request.ShouldBe(new TypeRequest("héllo wörld"));
        Bind("paste_text", """{"session":"s1","text":"line1\nline2"}""").Request.ShouldBe(new PasteRequest("line1\nline2"));
    }

    [Fact]
    public void Click_TakesExactlyOneTarget()
    {
        Bind("click", """{"session":"s1","x":10,"y":4}""").Request.ShouldBe(new ClickRequest(10, 4, null, 1, null, 0, false));
        Bind("click", """{"session":"s1","text":"Save","nth":2,"button":"right","double":true}""").Request
            .ShouldBe(new ClickRequest(null, null, "Save", 2, null, 2, true));
        Bind("click", """{"session":"s1","selector":"hook:save"}""").Request.ShouldBe(new ClickRequest(null, null, null, 1, "hook:save", 0, false));

        Errors("click", """{"session":"s1"}""").ShouldContain(error => error.Contains("exactly one target", StringComparison.Ordinal));
        Errors("click", """{"session":"s1","x":1}""").ShouldContain(error => error.Contains("both x and y", StringComparison.Ordinal));
        Errors("click", """{"session":"s1","x":1,"y":2,"text":"a"}""").ShouldContain(error => error.Contains("exactly one target", StringComparison.Ordinal));
        Errors("click", """{"session":"s1","selector":"a","nth":2}""").ShouldContain(error => error.Contains("nth needs text", StringComparison.Ordinal));
        Errors("click", """{"session":"s1","selector":"a","button":"back"}""").ShouldNotBeEmpty();
    }

    [Fact]
    public void Mouse_MapsTheActions_AndChecksDelta()
    {
        Bind("mouse", """{"session":"s1","action":"move","x":3,"y":4}""").Request.ShouldBe(new MouseRequest(TerminalMouseKind.Move, 3, 4, 0, 3));
        Bind("mouse", """{"session":"s1","action":"down","x":3,"y":4,"button":"middle"}""").Request.ShouldBe(new MouseRequest(TerminalMouseKind.Down, 3, 4, 1, 3));
        Bind("mouse", """{"session":"s1","action":"wheel","x":3,"y":4,"delta":-5}""").Request.ShouldBe(new MouseRequest(TerminalMouseKind.Wheel, 3, 4, 0, -5));

        Errors("mouse", """{"session":"s1","action":"move","x":1,"y":1,"delta":2}""").ShouldContain(error => error.Contains("only applies to wheel", StringComparison.Ordinal));
        Errors("mouse", """{"session":"s1","action":"wheel","x":1,"y":1,"delta":0}""").ShouldContain(error => error.Contains("must not be 0", StringComparison.Ordinal));
        Errors("mouse", """{"session":"s1","action":"drag","x":1,"y":1}""").ShouldNotBeEmpty();
        Errors("mouse", """{"session":"s1","action":"move","x":-1,"y":1}""").ShouldNotBeEmpty();
    }

    [Fact]
    public void WaitFor_TakesExactlyOneCondition_AndAnOptionalTimeout()
    {
        Bind("wait_for", """{"session":"s1","text":"Ready"}""").Request.ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Text, "Ready")));
        Bind("wait_for", """{"session":"s1","idle":"500ms"}""").Request
            .ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Idle, Idle: TimeSpan.FromMilliseconds(500))));
        Bind("wait_for", """{"session":"s1","exit":true}""").Request.ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Exit)));
        Bind("wait_for", """{"session":"s1","selector":"hook:save","timeout":"2m"}""").Options.Timeout.ShouldBe(TimeSpan.FromMinutes(2));

        Errors("wait_for", """{"session":"s1"}""").ShouldContain(error => error.Contains("exactly one condition", StringComparison.Ordinal));
        Errors("wait_for", """{"session":"s1","text":"a","gone":"b"}""").ShouldContain(error => error.Contains("exactly one condition", StringComparison.Ordinal));
        Errors("wait_for", """{"session":"s1","regex":"("}""").ShouldContain(error => error.StartsWith("regex:", StringComparison.Ordinal));
        Errors("wait_for", """{"session":"s1","text":"a","timeout":"6m"}""").ShouldContain(error => error.StartsWith("timeout:", StringComparison.Ordinal));
        Errors("wait_for", """{"session":"s1","text":"a","timeout":"soon"}""").ShouldNotBeEmpty();
    }

    [Fact]
    public void Assert_CarriesNot()
        => Bind("assert", """{"session":"s1","gone":"Error","not":true}""").Request
            .ShouldBe(new AssertRequest(new WaitCondition(WaitConditionKind.Gone, "Error"), true));

    [Fact]
    public void SessionAndTimeout_DefaultToTheServersGlobalOptions_AndTheArgumentWins()
    {
        var defaults = new CliGlobalOptions(false, "from-cli", TimeSpan.FromSeconds(20));

        var inherited = McpTools.Find("get_stats")!.TryBind(McpToolCatalogTests.Arguments("{}"), defaults, out _)!;
        inherited.Options.ShouldBe(new CliGlobalOptions(true, "from-cli", TimeSpan.FromSeconds(20)));

        var given = McpTools.Find("get_stats")!.TryBind(McpToolCatalogTests.Arguments("""{"session":"mine","timeout":"3s"}"""), defaults, out _)!;
        given.Options.ShouldBe(new CliGlobalOptions(true, "mine", TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void InvalidSessionNames_UnknownArguments_AndWrongTypes_AreAllReportedTogether()
    {
        var errors = Errors("get_stats", """{"session":"../evil","bogus":1}""");
        errors.ShouldContain(error => error.StartsWith("session:", StringComparison.Ordinal));
        errors.ShouldContain("bogus: this tool has no such argument.");

        Errors("resize_session", """{"session":"s1","cols":"wide","rows":0}""").Count.ShouldBe(2);
        Errors("type_text", """{"session":"s1"}""").ShouldContain("text: is required.");
        Errors("get_stats", """{"session":"s1","timeout":0}""").ShouldNotBeEmpty();
        Errors("list_sessions", """{"session":"s1"}""").ShouldContain("session: this tool has no such argument.");
    }

    [Fact]
    public void StartSession_BuildsTheRequestWithFullPaths()
    {
        var bound = Bind("start_session", """{"name":"demo","dll":"app.dll","args":["diag","--x"],"width":100,"height":30,"env":{"A":"1"},"cwd":"work","noWait":true}""");

        var request = bound.Request.ShouldBeOfType<StartRequest>();
        request.Name.ShouldBe("demo");
        request.TargetKind.ShouldBe(StartTargetKind.Dll);
        request.TargetPath.ShouldBe(Path.GetFullPath("app.dll"));
        request.Arguments.ShouldBe(["diag", "--x"]);
        (request.Width, request.Height).ShouldBe((100, 30));
        request.Environment["A"].ShouldBe("1");
        request.WorkingDirectory.ShouldBe(Path.GetFullPath("work"));
        request.NoWait.ShouldBeTrue();
        request.TerminalType.ShouldBe(StartRequest.DefaultTerminalType);

        var defaults = Bind("start_session", """{"exe":"app"}""").Request.ShouldBeOfType<StartRequest>();
        (defaults.Width, defaults.Height).ShouldBe((StartRequest.DefaultWidth, StartRequest.DefaultHeight));

        var project = Bind("start_session", """{"project":"src/App","framework":"net9.0","configuration":"Release"}""").Request.ShouldBeOfType<StartRequest>();
        (project.Framework, project.Configuration).ShouldBe(("net9.0", "Release"));
    }

    [Fact]
    public void StartSession_RefusesWhatTheCliRefuses()
    {
        Errors("start_session", """{"exe":"a","dll":"b"}""").ShouldContain(error => error.Contains("exactly one of", StringComparison.Ordinal));
        Errors("start_session", "{}").ShouldContain(error => error.Contains("exactly one of", StringComparison.Ordinal));
        Errors("start_session", """{"dll":"a","framework":"net9.0"}""").ShouldContain(error => error.Contains("only be used with project", StringComparison.Ordinal));
        Errors("start_session", """{"dll":"a","width":0}""").ShouldNotBeEmpty();
        Errors("start_session", """{"dll":"a","height":1001}""").ShouldNotBeEmpty();
        Errors("start_session", """{"dll":"a","env":{"A=B":"x"}}""").ShouldNotBeEmpty();
        Errors("start_session", """{"dll":"a","env":{"":"x"}}""").ShouldNotBeEmpty();
        Errors("start_session", """{"dll":"a","name":"-x"}""").ShouldNotBeEmpty();
    }

    [Fact]
    public void Screenshot_MapsFormatAndRegion()
    {
        Bind("screenshot", """{"session":"s1"}""").Request.ShouldBe(new ScreenRequest(ScreenFormat.Text, null));

        var request = Bind("screenshot", """{"session":"s1","format":"json","region":"1,2,30,4"}""").Request.ShouldBeOfType<ScreenRequest>();
        request.Format.ShouldBe(ScreenFormat.Json);
        request.Region.ShouldNotBeNull().ShouldSatisfyAllConditions(
            region => region.X.ShouldBe(1),
            region => region.Y.ShouldBe(2),
            region => region.Width.ShouldBe(30),
            region => region.Height.ShouldBe(4));

        Errors("screenshot", """{"session":"s1","format":"png"}""").ShouldNotBeEmpty();
        Errors("screenshot", """{"session":"s1","region":"1,2"}""").ShouldNotBeEmpty();
    }

    [Fact]
    public void FramesDiffOutput_MapTheirCursorsAndLimits()
    {
        Bind("get_frames", """{"session":"s1"}""").Request.ShouldBe(new FramesRequest(1));
        Bind("get_frames", """{"session":"s1","last":50}""").Request.ShouldBe(new FramesRequest(50));
        Errors("get_frames", """{"session":"s1","last":51}""").ShouldNotBeEmpty();
        Bind("diff_frames", """{"session":"s1","from":3,"to":5}""").Request.ShouldBe(new DiffRequest(3, 5));

        Bind("get_output", """{"session":"s1"}""").Request.ShouldBe(new OutputRequest(null, McpResultLimiter.DefaultOutputBytes));
        Bind("get_output", """{"session":"s1","since":120,"maxBytes":500}""").Request.ShouldBe(new OutputRequest(120, 500));
        Errors("get_output", """{"session":"s1","maxBytes":0}""").ShouldNotBeEmpty();
    }

    [Fact]
    public void AppLayerTools_MapTheirArguments()
    {
        Bind("get_tree", """{"session":"s1"}""").Request.ShouldBe(new TreeRequest(null, McpResultLimiter.DefaultTreeDepth, true, true));
        Bind("get_tree", """{"session":"s1","selector":"hook:a","depth":2,"includeText":false,"includeLayout":false}""").Request
            .ShouldBe(new TreeRequest("hook:a", 2, false, false));
        Bind("find_elements", """{"session":"s1","selector":"text:Save","first":true}""").Request.ShouldBe(new FindRequest("text:Save", true));
        Bind("inspect_element", """{"session":"s1","selector":"focus:save"}""").Request.ShouldBe(new InspectRequest("focus:save"));
        Bind("inspect_at", """{"session":"s1","x":5,"y":6}""").Request.ShouldBe(new InspectAtRequest(5, 6));
        Bind("get_focus", """{"session":"s1"}""").Request.ShouldBe(new FocusRequest(FocusAction.List, null));
        Bind("set_focus", """{"session":"s1","key":"save"}""").Request.ShouldBe(new FocusRequest(FocusAction.Set, "save"));
        Bind("set_focus", """{"session":"s1","move":"prev"}""").Request.ShouldBe(new FocusRequest(FocusAction.Previous, null));
        Errors("set_focus", """{"session":"s1","key":"a","move":"next"}""").ShouldNotBeEmpty();
        Errors("set_focus", """{"session":"s1"}""").ShouldNotBeEmpty();
        Bind("get_route", """{"session":"s1"}""").Request.ShouldBe(new RouteRequest(null));
        Bind("navigate", """{"session":"s1","uri":"/settings"}""").Request.ShouldBe(new RouteRequest("/settings"));
        Bind("get_stats", """{"session":"s1"}""").Request.ShouldBe(new StatsRequest());
        Bind("doctor", """{"fix":true}""").Request.ShouldBe(new DoctorRequest(true));
    }

    [Fact]
    public void ErrorsAndLogs_MapCursorsLevelsAndLimits_NeverFollowing()
    {
        Bind("get_errors", """{"session":"s1","since":7}""").Request.ShouldBe(new ErrorsRequest(7, false));
        Bind("get_errors", """{"session":"s1"}""").Limit.ShouldBe(McpResultLimiter.DefaultLogLimit);

        var logs = Bind("get_logs", """{"session":"s1","since":3,"level":"warning","category":"App","limit":25}""");
        logs.Request.ShouldBe(new LogsRequest(3, "Warning", "App", false));
        logs.Limit.ShouldBe(25);
        Errors("get_logs", """{"session":"s1","level":"loud"}""").ShouldNotBeEmpty();
        Errors("get_logs", """{"session":"s1","limit":1001}""").ShouldNotBeEmpty();
        Errors("get_logs", """{"session":"s1","follow":true}""").ShouldContain("follow: this tool has no such argument.");
    }

    [Fact]
    public void SessionTools_MapToTheirRequests()
    {
        Bind("list_sessions", "{}").Request.ShouldBe(new ListRequest());
        Bind("stop_session", """{"session":"s1"}""").Request.ShouldBe(new StopRequest(null, StopRequest.DefaultTimeout));
        Bind("stop_session", """{"session":"s1","timeout":"2s"}""").Request.ShouldBe(new StopRequest(null, TimeSpan.FromSeconds(2)));
        Bind("restart_session", """{"session":"s1"}""").Request.ShouldBe(new RestartRequest(null));
        Bind("resize_session", """{"session":"s1","cols":80,"rows":24}""").Request.ShouldBe(new ResizeRequest(80, 24));
        Bind("attach_app", """{"pid":1234,"name":"live"}""").Request.ShouldBe(new AttachRequest(1234, "live"));
        Bind("detach_session", """{"session":"a1"}""").Request.ShouldBe(new DetachRequest(null));
        Errors("attach_app", """{"pid":0}""").ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("""{"session":"s1","text":"a","timeout":10}""", 10_000)]
    [InlineData("""{"session":"s1","text":"a","timeout":2.5}""", 2_500)]
    [InlineData("""{"session":"s1","text":"a","timeout":"10"}""", 10_000)]
    [InlineData("""{"session":"s1","text":"a","timeout":"10s"}""", 10_000)]
    [InlineData("""{"session":"s1","text":"a","timeout":"500ms"}""", 500)]
    [InlineData("""{"session":"s1","text":"a","timeout":300}""", 300_000)]
    public void Timeout_IsADurationStringOrANumberOfSeconds(string arguments, int milliseconds)
        => Bind("wait_for", arguments).Options.Timeout.ShouldBe(TimeSpan.FromMilliseconds(milliseconds));

    [Theory]
    [InlineData("""{"session":"s1","text":"a","timeout":0}""")]
    [InlineData("""{"session":"s1","text":"a","timeout":-3}""")]
    [InlineData("""{"session":"s1","text":"a","timeout":301}""")]
    [InlineData("""{"session":"s1","text":"a","timeout":1e300}""")]
    [InlineData("""{"session":"s1","text":"a","timeout":true}""")]
    [InlineData("""{"session":"s1","text":"a","timeout":"0"}""")]
    public void Timeout_ThatIsNotPositiveOrOverTheLimit_IsAUsageErrorNotACrash(string arguments)
        => Errors("wait_for", arguments).ShouldContain(error => error.StartsWith("timeout:", StringComparison.Ordinal));

    [Fact]
    public void Idle_AndStopTimeout_AcceptNumbersOfSecondsToo()
    {
        Bind("wait_for", """{"session":"s1","idle":0.5}""").Request
            .ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Idle, Idle: TimeSpan.FromMilliseconds(500))));
        Bind("assert", """{"session":"s1","idle":"1s"}""").Request
            .ShouldBe(new AssertRequest(new WaitCondition(WaitConditionKind.Idle, Idle: TimeSpan.FromSeconds(1)), false));
        Bind("stop_session", """{"session":"s1","timeout":3}""").Request.ShouldBe(new StopRequest(null, TimeSpan.FromSeconds(3)));
        Errors("wait_for", """{"session":"s1","idle":"soon"}""").ShouldNotBeEmpty();
    }

    [Fact]
    public void Selector_IsTheNameOnEveryToolThatTakesOne_AndElementIsRefused()
    {
        Bind("wait_for", """{"session":"s1","selector":"hook:save"}""").Request
            .ShouldBe(new WaitRequest(new WaitCondition(WaitConditionKind.Element, "hook:save")));
        Bind("assert", """{"session":"s1","selector":"hook:save"}""").Request
            .ShouldBe(new AssertRequest(new WaitCondition(WaitConditionKind.Element, "hook:save"), false));
        Errors("click", """{"session":"s1","element":"hook:save"}""").ShouldContain("element: this tool has no such argument.");
        Errors("send_keys", """{"session":"s1","keys":["a"],"element":"hook:save"}""").ShouldContain("element: this tool has no such argument.");
        Errors("wait_for", """{"session":"s1","element":"hook:save"}""").ShouldNotBeEmpty();
    }

    [Fact]
    public void GetTree_DefaultsToAShallowDepthAndASmallBudget_AnExplicitDepthMayUseTheHardCap()
    {
        var small = Bind("get_tree", """{"session":"s1"}""");
        small.Request.ShouldBe(new TreeRequest(null, McpResultLimiter.DefaultTreeDepth, true, true));
        small.TreeBudget.ShouldBe(McpResultLimiter.DefaultTreeBudget);
        McpResultLimiter.DefaultTreeBudget.ShouldBeLessThanOrEqualTo(20_000);

        var selected = Bind("get_tree", """{"session":"s1","selector":"hook:list"}""");
        selected.Request.ShouldBe(new TreeRequest("hook:list", McpResultLimiter.DefaultTreeDepth, true, true));
        selected.TreeBudget.ShouldBe(McpResultLimiter.DefaultTreeBudget);

        var deep = Bind("get_tree", """{"session":"s1","depth":8}""");
        deep.Request.ShouldBe(new TreeRequest(null, 8, true, true));
        deep.TreeBudget.ShouldBe(McpResultLimiter.MaxResultCharacters);
    }

    private const string Rule = "A number is seconds (e.g. 30), not milliseconds; or a string with a unit: 500ms, 10s, 2m.";

    [Fact]
    public void Timeout_ANumberInTheMillisecondRange_IsExplainedAsSecondsWithTheFix()
    {
        // 60000 is what an agent writes when it thinks in milliseconds.
        var expected = "timeout: 60000 is read as seconds (60000s), above the 5m limit. Numbers are seconds; use 60 or \"60s\", or \"500ms\" for milliseconds.";

        Errors("wait_for", """{"session":"s1","text":"a","timeout":60000}""").ShouldContain(expected);
        Errors("assert", """{"session":"s1","text":"a","timeout":60000}""").ShouldContain(expected);

        // The same number sent as text is read the same way.
        Errors("wait_for", """{"session":"s1","text":"a","timeout":"60000"}""").ShouldContain(expected);
    }

    [Fact]
    public void Timeout_ANumberJustOverTheLimit_SuggestsTheLimit_NotAMillisecondValue()
    {
        Errors("wait_for", """{"session":"s1","text":"a","timeout":301}""").ShouldContain(
            "timeout: 301 is read as seconds (301s), above the 5m limit. Numbers are seconds; use 300 or \"300s\", or \"500ms\" for milliseconds.");
        Errors("stop_session", """{"session":"s1","timeout":700}""").ShouldContain(
            "timeout: 700 is read as seconds (700s), above the 10m limit. Numbers are seconds; use 600 or \"600s\", or \"500ms\" for milliseconds.");

        // A number too large for any duration is still explained, not thrown.
        Errors("wait_for", """{"session":"s1","text":"a","timeout":1e300}""").ShouldContain(
            "timeout: 1e300 is read as seconds (1e300s), above the 5m limit. Numbers are seconds; use 300 or \"300s\", or \"500ms\" for milliseconds.");
    }

    [Fact]
    public void Timeout_ZeroOrNegative_IsExplainedWithTheFix()
    {
        Errors("wait_for", """{"session":"s1","text":"a","timeout":0}""").ShouldContain(
            "timeout: 0 is read as seconds and must be greater than 0. Numbers are seconds; use 10 or \"10s\", or \"500ms\" for milliseconds.");
        Errors("wait_for", """{"session":"s1","text":"a","timeout":-5}""").ShouldContain(
            "timeout: -5 is read as seconds and must be greater than 0. Numbers are seconds; use 10 or \"10s\", or \"500ms\" for milliseconds.");
    }

    [Fact]
    public void Timeout_AStringThatIsNotADuration_NamesTheUnitRule()
    {
        Errors("wait_for", """{"session":"s1","text":"a","timeout":"soon"}""").ShouldContain($"timeout: 'soon' is not a duration. {Rule}");
        Errors("wait_for", """{"session":"s1","text":"a","timeout":"30 sec"}""").ShouldContain($"timeout: '30 sec' is not a duration. {Rule}");
        Errors("wait_for", """{"session":"s1","text":"a","timeout":true}""").ShouldContain($"timeout: must be a duration. {Rule}");
    }

    [Fact]
    public void Timeout_AStringWithAUnitOverTheLimit_SaysTheLimit()
        => Errors("wait_for", """{"session":"s1","text":"a","timeout":"10m"}""").ShouldContain(
            "timeout: '10m' is above the 5m limit. Use a shorter duration such as \"5m\" or less, or a number of seconds.");

    [Fact]
    public void Idle_AnInvalidDuration_NamesTheUnitRuleToo()
        => Errors("wait_for", """{"session":"s1","idle":"fast"}""").ShouldContain($"idle: 'fast' is not a duration. {Rule}");

    [Fact]
    public void ADurationError_IsReportedOnce_EvenWhenTwoPartsOfTheToolReadTheArgument()
        => Errors("stop_session", """{"session":"s1","timeout":"soon"}""").ShouldBe([$"timeout: 'soon' is not a duration. {Rule}"]);

    private static McpBound Bind(string tool, string arguments)
    {
        var bound = McpTools.Find(tool)!.TryBind(McpToolCatalogTests.Arguments(arguments), Defaults, out var errors);
        bound.ShouldNotBeNull($"{tool} {arguments}: {string.Join("; ", errors)}");
        return bound;
    }

    private static IReadOnlyList<string> Errors(string tool, string arguments)
    {
        var bound = McpTools.Find(tool)!.TryBind(McpToolCatalogTests.Arguments(arguments), Defaults, out var errors);
        bound.ShouldBeNull($"{tool} {arguments} should not bind");
        errors.ShouldNotBeEmpty();
        return errors;
    }
}
