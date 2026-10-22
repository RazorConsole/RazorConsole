// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Diagnostics;

public sealed class DiagnosticsSelectorTests
{
    // div[hook=form]          /
    //   span key=title        /0     "Settings"
    //   button key=save       /1     "Save", "Save all"
    //   button key=cancel     /2     "Cancel changes"
    private readonly VNode _root;
    private readonly VNode _title;
    private readonly VNode _save;
    private readonly VNode _cancel;

    public DiagnosticsSelectorTests()
    {
        _root = VNode.CreateElement("div");
        _root.SetAttribute(IVNodeIdAccessor.HookAttributeName, "form");
        _title = Element("span", "title", "Settings");
        _save = Element("button", "save", "Save", "Save all");
        _save.SetAttribute("class", "primary");
        _cancel = Element("button", "cancel", "Cancel changes");
        _root.AddChild(_title);
        _root.AddChild(_save);
        _root.AddChild(_cancel);
    }

    [Fact]
    public void ElementSelectors_MatchById_Hook_Key_AndFocusKey()
    {
        Match($"id:{_save.ID}").ShouldBe([_save]);
        Match("hook:form").ShouldBe([_root]);
        Match("key:cancel").ShouldBe([_cancel]);
        Match("focus:primary-action").ShouldBe([_save]);
        Match("key:missing").ShouldBeEmpty();
    }

    [Fact]
    public void TextSelectors_MatchTheNearestElementOnceInDocumentOrder()
    {
        Match("text:Save").ShouldBe([_save]);
        Match("text:e").ShouldBe([_title, _save, _cancel]);
        Match("text~:^Can").ShouldBe([_cancel]);
    }

    [Fact]
    public void TagSelector_MatchesTheNameIgnoringCaseAndEveryAttributeFilter()
    {
        Match("tag:button").ShouldBe([_save, _cancel]);
        Match("tag:BUTTON[class=primary]").ShouldBe([_save]);
        Match("tag:button[class]").ShouldBe([_save]);
        Match("tag:button[class=other]").ShouldBeEmpty();
    }

    [Fact]
    public void PathSelector_ReachesTextNodes()
    {
        var matches = Match("path:/1/1");

        matches.ShouldHaveSingleItem().Text.ShouldBe("Save all");
        Match("path:/").ShouldBe([_root]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("save")]
    [InlineData(":save")]
    [InlineData("id:")]
    [InlineData("bogus:x")]
    [InlineData("text~:(")]
    [InlineData("tag:[class]")]
    [InlineData("tag:button[class")]
    [InlineData("tag:button[]")]
    public void Parse_RejectsMalformedSelectors(string? selector)
    {
        var ex = Should.Throw<DiagnosticsProtocolException>(() => DiagnosticsSelector.Parse(selector));

        ex.Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
    }

    private List<VNode> Match(string selector)
    {
        var frame = new DiagnosticsFrame(1, _root, null, null);
        var index = new DiagnosticsTreeIndex(frame, [new FocusManager.FocusTargetInfo("primary-action", _save.ID, IsFocused: true)]);
        return DiagnosticsSelector.Parse(selector).Match(index).ConvertAll(entry => entry.Node);
    }

    private static VNode Element(string tag, string key, params string[] texts)
    {
        var element = VNode.CreateElement(tag, key);
        foreach (var text in texts)
        {
            element.AddChild(VNode.CreateText(text));
        }

        return element;
    }
}
