// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Vdom;

public class VdomDiffServiceTests
{
    private readonly VdomDiffService _service = new();

    [Theory]
    [InlineData("<div><span>Hi</span></div>", "<div><span>Hi</span></div>")]
    [InlineData("<div><span>Hi</span></div>", "<div><span>Hi</span><span>There</span></div>")]
    [InlineData("<div><span>Hi</span><span>There</span></div>", "<div><span>Hi</span></div>")]
    [InlineData("<div><span>Hi</span></div>", "<div><span>Bye</span></div>")]
    [InlineData("<panel header=\"Old\"></panel>", "<panel header=\"New\"></panel>")]
    [InlineData("<div><span>Hi</span></div>", "<div><strong>Hi</strong></div>")]
    public void HasChanges_MatchesFullDiff(string previousHtml, string currentHtml)
    {
        HtmlVdomConverter.TryConvert(previousHtml, out var previous);
        HtmlVdomConverter.TryConvert(currentHtml, out var current);

        _service.HasChanges(previous, current).ShouldBe(_service.Diff(previous, current).HasChanges);
    }

    [Fact]
    public void HasChanges_HandlesMissingTrees()
    {
        HtmlVdomConverter.TryConvert("<div></div>", out var tree);

        _service.HasChanges(null, null).ShouldBeFalse();
        _service.HasChanges(null, tree).ShouldBeTrue();
        _service.HasChanges(tree, null).ShouldBeTrue();
        _service.HasChanges(tree, tree).ShouldBeFalse();
    }

    [Fact]
    public void Diff_InsertsNode_WhenNewChildAppears()
    {
        HtmlVdomConverter.TryConvert("<div><span>Hi</span></div>", out var previous);
        HtmlVdomConverter.TryConvert("<div><span>Hi</span><span>There</span></div>", out var current);

        var diff = _service.Diff(previous, current);

        diff.HasChanges.ShouldBeTrue();
        diff.Mutations.ShouldContain(mutation => mutation.Kind == VdomMutationKind.InsertNode);
        var insert = diff.Mutations.Single(mutation => mutation.Kind == VdomMutationKind.InsertNode);
        insert.Path.ShouldBe(new[] { 1 });
    }

    [Fact]
    public void Diff_UpdatesAttributes_WhenValuesChange()
    {
        HtmlVdomConverter.TryConvert("<panel header=\"Old\"></panel>", out var previous);
        HtmlVdomConverter.TryConvert("<panel header=\"New\"></panel>", out var current);

        var diff = _service.Diff(previous, current);

        diff.Mutations.ShouldHaveSingleItem();
        var mutation = diff.Mutations.Single();
        mutation.Kind.ShouldBe(VdomMutationKind.UpdateAttributes);
        mutation.Attributes!["header"].ShouldBe("New");
    }

    [Fact]
    public void Diff_RemovesNode_WhenChildDisappears()
    {
        HtmlVdomConverter.TryConvert("<div><span>Hi</span></div>", out var previous);
        HtmlVdomConverter.TryConvert("<div></div>", out var current);

        var diff = _service.Diff(previous, current);

        diff.Mutations.ShouldHaveSingleItem();
        var mutation = diff.Mutations.Single();
        mutation.Kind.ShouldBe(VdomMutationKind.RemoveNode);
        mutation.Path.ShouldBe(new[] { 0 });
    }
}
