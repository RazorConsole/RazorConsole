// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RazorConsole.Core;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Diagnostics;

public sealed class DiagnosticsAppQueriesTests
{
    private readonly DiagnosticsState _state = new();
    private readonly FocusManager _focus = new();
    private readonly ConsoleNavigationManager _navigation = new();
    private readonly DiagnosticsAppQueries _queries;

    // div[hook=form] (0,0 40x10)         /
    //   span key=title                   /0    "Settings"
    //   button key=save (2,3 10x1)       /1    "Save"     focusable, focused
    //   button key=cancel (20,3 10x1)    /2    "Cancel"   focusable
    private readonly VNode _root;
    private readonly VNode _save;
    private readonly VNode _cancel;

    public DiagnosticsAppQueriesTests()
    {
        _queries = new DiagnosticsAppQueries(_state, _focus, _navigation, new ConsoleAppOptions());

        _root = VNode.CreateElement("div");
        _root.SetAttribute(IVNodeIdAccessor.HookAttributeName, "form");
        var title = VNode.CreateElement("span", "title");
        title.AddChild(VNode.CreateText("Settings"));
        _save = Button("save", "Save");
        _save.SetAttribute("class", "primary");
        _cancel = Button("cancel", "Cancel");
        _root.AddChild(title);
        _root.AddChild(_save);
        _root.AddChild(_cancel);

        Publish(_root, Layout(_root, 0, 0, 40, 10), Layout(_save, 2, 3, 10, 1), Layout(_cancel, 20, 3, 10, 1));
    }

    [Fact]
    public void Tree_ReturnsNodesWithLayoutFocusAndPaths()
    {
        var result = _queries.Tree(new TreeParams());

        result.Frame.ShouldBe(_state.LatestFrame!.Sequence);
        var root = result.Root.ShouldNotBeNull();
        root.Id.ShouldBe(_root.ID);
        root.Path.ShouldBe("/");
        root.Hook.ShouldBe("form");
        root.Attributes.ShouldBeNull();
        root.Rect.ShouldBe(new DiagnosticsRect(0, 0, 40, 10, 0));

        var save = root.Children.ShouldNotBeNull()[1];
        save.Path.ShouldBe("/1");
        save.Key.ShouldBe("save");
        save.Attributes.ShouldNotBeNull()["class"].ShouldBe("primary");
        save.Events.ShouldBe(["onclick"]);
        save.Rect.ShouldBe(new DiagnosticsRect(2, 3, 10, 1, 0));
        save.FocusKey.ShouldBe("save");
        save.Focused.ShouldBe(true);
        root.Children[2].Focused.ShouldBe(false);

        var text = save.Children.ShouldNotBeNull().ShouldHaveSingleItem();
        text.Kind.ShouldBe("text");
        text.Text.ShouldBe("Save");
        text.Path.ShouldBe("/1/0");
        text.Id.ShouldBeNull();
    }

    [Fact]
    public void Tree_HonoursDepthTextAndLayoutOptions()
    {
        var shallow = _queries.Tree(new TreeParams(Depth: 0)).Root.ShouldNotBeNull();
        shallow.Children.ShouldBeNull();
        shallow.ChildCount.ShouldBe(3);

        var noText = _queries.Tree(new TreeParams(IncludeText: false, IncludeLayout: false)).Root.ShouldNotBeNull();
        noText.Rect.ShouldBeNull();
        noText.Children.ShouldNotBeNull().ShouldAllBe(child => child.Children == null && child.Rect == null);

        Should.Throw<DiagnosticsProtocolException>(() => _queries.Tree(new TreeParams(Depth: -1)))
            .Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
    }

    [Fact]
    public void Tree_StartsAtASelectedNode()
    {
        var result = _queries.Tree(new TreeParams(Selector: "key:cancel"));

        result.Root.ShouldNotBeNull().Id.ShouldBe(_cancel.ID);
    }

    [Fact]
    public void Tree_BeforeTheFirstFrameIsEmpty()
    {
        var queries = new DiagnosticsAppQueries(new DiagnosticsState(), new FocusManager(), _navigation, new ConsoleAppOptions());

        var result = queries.Tree(new TreeParams());

        result.Root.ShouldBeNull();
        result.Frame.ShouldBe(0);
    }

    [Fact]
    public void Find_ReturnsEveryMatchWithItsSummary()
    {
        var result = _queries.Find(new FindParams("tag:button"));

        result.Matches.Select(m => m.Id).ShouldBe([_save.ID, _cancel.ID]);
        result.Matches[0].ShouldBe(new DiagnosticsMatch(
            _save.ID, "/1", "element", "button", "Save", new DiagnosticsRect(2, 3, 10, 1, 0), "save"));
    }

    [Fact]
    public void Inspect_ReturnsTheNodeAndItsElementAncestors()
    {
        var result = _queries.Inspect(new InspectParams("text:Cancel"));

        result.Node.Id.ShouldBe(_cancel.ID);
        result.Node.Children.ShouldNotBeNull().ShouldHaveSingleItem().Text.ShouldBe("Cancel");
        result.Ancestors.Select(a => a.Id).ShouldBe([_root.ID]);
        result.Ancestors[0].Text.ShouldBe("Settings Save Cancel");
    }

    [Fact]
    public void Inspect_RejectsAmbiguousSelectorsWithTheCandidatesUnlessFirstIsSet()
    {
        var ex = Should.Throw<DiagnosticsProtocolException>(() => _queries.Inspect(new InspectParams("tag:button")));

        ex.Code.ShouldBe(DiagnosticsErrorCodes.AmbiguousSelector);
        var data = ex.ErrorData.ShouldNotBeNull().Deserialize(DiagnosticsJsonContext.Default.AmbiguousSelectorData).ShouldNotBeNull();
        data.Candidates.Select(c => c.Id).ShouldBe([_save.ID, _cancel.ID]);

        _queries.Inspect(new InspectParams("tag:button", First: true)).Node.Id.ShouldBe(_save.ID);

        Should.Throw<DiagnosticsProtocolException>(() => _queries.Inspect(new InspectParams("key:missing")))
            .Code.ShouldBe(DiagnosticsErrorCodes.ElementNotFound);
    }

    [Fact]
    public void InspectAt_ReturnsTheTopmostDeepestElementThenItsAncestors()
    {
        var dialog = VNode.CreateElement("div", "dialog");
        _root.AddChild(dialog);
        Publish(
            _root,
            Layout(_root, 0, 0, 40, 10),
            Layout(_save, 2, 3, 10, 1),
            Layout(_cancel, 20, 3, 10, 1),
            Layout(dialog, 15, 2, 10, 3, zIndex: 1));

        _queries.InspectAt(new InspectAtParams(3, 3)).Stack.Select(m => m.Id).ShouldBe([_save.ID, _root.ID]);
        _queries.InspectAt(new InspectAtParams(22, 3)).Stack[0].Id.ShouldBe(dialog.ID);
        _queries.InspectAt(new InspectAtParams(35, 8)).Stack.Select(m => m.Id).ShouldBe([_root.ID]);
        _queries.InspectAt(new InspectAtParams(12, 3)).Stack.Select(m => m.Id).ShouldBe([_root.ID]);
        _queries.InspectAt(new InspectAtParams(100, 100)).Stack.ShouldBeEmpty();
    }

    [Fact]
    public void Focus_ListsTargetsInOrderWithTheCurrentKey()
    {
        var result = _queries.Focus();

        result.Current.ShouldBe("save");
        result.Targets.ShouldBe([new FocusTargetResult("save", _save.ID), new FocusTargetResult("cancel", _cancel.ID)]);
    }

    [Fact]
    public void Route_ReturnsTheAbsoluteAndRelativeUri()
    {
        _navigation.NavigateTo("settings/display");

        _queries.Route().ShouldBe(new RouteResult("app:///settings/display", "settings/display"));
    }

    [Fact]
    public void Errors_UseAnInclusiveCursor()
    {
        var empty = _queries.Errors(new ErrorsParams());
        empty.Errors.ShouldBeEmpty();
        empty.Next.ShouldBe(1);

        for (var i = 1; i <= 3; i++)
        {
            _state.RecordError(new InvalidOperationException($"boom {i}"), "render");
        }

        var all = _queries.Errors(new ErrorsParams());
        all.Errors.Select(e => e.Seq).ShouldBe([1L, 2L, 3L]);
        all.Next.ShouldBe(4);
        all.Errors[2].Message.ShouldBe("boom 3");
        all.Errors[2].Type.ShouldBe(typeof(InvalidOperationException).FullName);
        all.Errors[2].Stack.ShouldContain("boom 3");

        _queries.Errors(new ErrorsParams(Since: 3)).Errors.ShouldHaveSingleItem().Seq.ShouldBe(3);
        var none = _queries.Errors(new ErrorsParams(Since: all.Next));
        none.Errors.ShouldBeEmpty();
        none.Next.ShouldBe(4);
    }

    [Fact]
    public void Logs_FilterByMinimumLevelAndCategoryPrefix()
    {
        using var provider = new DiagnosticsLoggerProvider(_state);
        provider.CreateLogger("App.Forms").LogDebug("a");
        provider.CreateLogger("App.Data").LogWarning("b");
        provider.CreateLogger("Other").LogError(new InvalidOperationException("bad"), "c");

        _queries.Logs(new LogsParams(Level: "warning")).Entries.Select(e => e.Message).ShouldBe(["b", "c"]);
        _queries.Logs(new LogsParams(Category: "app.")).Entries.Select(e => e.Message).ShouldBe(["a", "b"]);

        // The cursor moves past entries the filters dropped.
        var filtered = _queries.Logs(new LogsParams(Since: 2, Category: "App"));
        filtered.Entries.Select(e => e.Seq).ShouldBe([2L]);
        filtered.Next.ShouldBe(4);

        var error = _queries.Logs(new LogsParams(Since: 3)).Entries.ShouldHaveSingleItem();
        error.Level.ShouldBe("Error");
        error.Category.ShouldBe("Other");
        error.Exception.ShouldNotBeNull().ShouldContain("bad");

        Should.Throw<DiagnosticsProtocolException>(() => _queries.Logs(new LogsParams(Level: "loud")))
            .Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
        Should.Throw<DiagnosticsProtocolException>(() => _queries.Logs(new LogsParams(Level: "42")))
            .Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
    }

    [Fact]
    public void Stats_ReportCountersPipelineAndFrame()
    {
        _state.Counters.RecordFrame(2 * Stopwatch.Frequency / 1000);
        _state.Counters.RecordDroppedFrame();

        var stats = _queries.Stats();

        stats.Frames.ShouldBe(1);
        stats.DroppedFrames.ShouldBe(1);
        stats.RenderMs.Last.ShouldBe(2, tolerance: 0.01);
        stats.Pipeline.ShouldBe(new ConsoleAppOptions().RenderingPipeline.ToString());
        stats.Frame.ShouldBe(_state.LatestFrame!.Sequence);
        stats.UptimeMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    private void Publish(VNode root, params VNodeLayoutInfo[] layouts)
    {
        _state.PublishFrame(root, layouts, null);
        ((IObserver<ConsoleRenderer.RenderSnapshot>)_focus).OnNext(new ConsoleRenderer.RenderSnapshot(root, null, []));
    }

    private static VNodeLayoutInfo Layout(VNode node, int left, int top, int width, int height, int zIndex = 0)
        => new(node.ID, top, left, null, null, width, height, zIndex);

    private static VNode Button(string key, string text)
    {
        var button = VNode.CreateElement("button", key);
        button.SetEvent("onclick", 1);
        button.AddChild(VNode.CreateText(text));
        return button;
    }
}
