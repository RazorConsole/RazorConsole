// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Controllers;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;
using Spectre.Console;

namespace RazorConsole.Tests.Rendering;

public sealed class ConsoleLiveDisplayContextTests
{
    [Fact]
    public void AnEarlierRender_NeverReplacesALaterOne()
    {
        var canvas = new RecordingCanvas();
        var later = Snapshot("later", 5);
        using var context = ConsoleLiveDisplayContext.CreateForTesting(canvas, null, new VdomDiffService());

        context.Present(later);
        context.Present(Snapshot("earlier", 3));
        context.UpdateView(ConsoleViewResult.FromSnapshot(Snapshot("also earlier", 4)));

        canvas.Targets.ShouldBe([later.Source]);
    }

    [Fact]
    public void Present_ShowsASnapshotWhoseTreeDidNotChange()
    {
        var canvas = new RecordingCanvas();
        var first = Snapshot("same", 1);
        var second = Snapshot("same", 2);
        using var context = ConsoleLiveDisplayContext.CreateForTesting(canvas, null, new VdomDiffService());

        context.UpdateView(ConsoleViewResult.FromSnapshot(first)).ShouldBeTrue();
        context.UpdateView(ConsoleViewResult.FromSnapshot(second)).ShouldBeFalse();
        context.Present(second);

        canvas.Targets.ShouldBe([first.Source, second.Source]);
    }

    private static ConsoleRenderer.RenderSnapshot Snapshot(string text, long sequence)
    {
        var root = VNode.CreateElement("div");
        root.AddChild(VNode.CreateText(text));
        return new ConsoleRenderer.RenderSnapshot(root, TestFrames.Of(new Text(text), sequence), Array.Empty<IAnimatedConsoleRenderable>())
        {
            Sequence = sequence,
        };
    }

    private sealed class RecordingCanvas : ConsoleLiveDisplayContext.ILiveDisplayCanvas
    {
        public List<FrameSource?> Targets { get; } = [];

        public event Action? Refreshed;

        public void UpdateTarget(FrameSource? source) => Targets.Add(source);

        public void Refresh() => Refreshed?.Invoke();

        public Task WhenIdleAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
