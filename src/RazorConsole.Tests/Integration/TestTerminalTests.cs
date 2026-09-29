// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;
using RazorConsole.Tests.Integration.Infrastructure;

namespace RazorConsole.Tests.Integration;

public sealed class TestTerminalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitSnapshot_LateOlderCaptureCannotReplaceNewerState(bool newerIsDuplicate)
    {
        await using var terminal = new TestTerminal(20, 1);
        var initial = CreateSnapshot(1, newerIsDuplicate ? "Approval required" : "Ready", "approve-tool");
        terminal.CommitSnapshot(initial);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var olderCapture = Task.Run(async () =>
        {
            var snapshot = CreateSnapshot(2, "Working", "composer");
            captured.SetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            terminal.CommitSnapshot(snapshot);
        }, TestContext.Current.CancellationToken);

        try
        {
            await captured.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            terminal.CommitSnapshot(CreateSnapshot(3, "Approval required", "approve-tool"));
        }
        finally
        {
            release.TrySetResult();
            await olderCapture.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        terminal.Snapshot.ContainsText("Approval required").ShouldBeTrue();
        terminal.Snapshot.Layouts.ContainsKey("approve-tool").ShouldBeTrue();
        terminal.Frames.Select(frame => frame.FrameNumber).ShouldBe(newerIsDuplicate ? [1L] : [1L, 3L]);
        (await terminal.WaitUntilAsync(frame => frame.Layouts.ContainsKey("approve-tool"),
            cancellationToken: TestContext.Current.CancellationToken)).ShouldBeSameAs(terminal.Snapshot);

        terminal.CommitSnapshot(CreateSnapshot(4, "Finished", "composer"));
        terminal.Snapshot.FrameNumber.ShouldBe(4);
        terminal.Snapshot.ContainsText("Finished").ShouldBeTrue();
        terminal.Snapshot.Layouts.ContainsKey("approve-tool").ShouldBeFalse();
    }

    [Fact]
    public async Task CommitSnapshot_HistoryRemainsBoundedAndRejectsRepeatedCapture()
    {
        await using var terminal = new TestTerminal(20, 1);
        for (var number = 1; number <= 55; number++)
        {
            terminal.CommitSnapshot(CreateSnapshot(number, number.ToString(), "composer"));
        }

        terminal.CommitSnapshot(CreateSnapshot(55, "Stale", "approve-tool"));
        terminal.CommitSnapshot(CreateSnapshot(1, "Stale", "approve-tool"));
        terminal.Frames.Select(frame => frame.FrameNumber).ShouldBe(Enumerable.Range(6, 50).Select(number => (long)number));
        terminal.Snapshot.ContainsText("55").ShouldBeTrue();
    }

    [Fact]
    public async Task OnNext_LateNotificationCapturesCurrentSourceInsteadOfReplayingOldState()
    {
        var initialized = new TaskCompletionSource<SnapshotComponent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var terminal = await TestTerminal.StartAsync<SnapshotComponent>(20, 1,
            new Dictionary<string, object?>
            {
                [nameof(SnapshotComponent.Initialized)] = (Action<SnapshotComponent>)(component => initialized.SetResult(component)),
            },
            TestContext.Current.CancellationToken);
        var component = await initialized.Task.WaitAsync(TestContext.Current.CancellationToken);
        var oldNotification = component.Renderer.RefreshSnapshot();
        var oldCapture = terminal.CaptureSnapshot();
        oldCapture.ShouldNotBeNull();
        oldCapture.ContainsText("Working").ShouldBeTrue();

        await component.ShowApprovalAsync();
        var currentCapture = terminal.CaptureSnapshot();
        currentCapture.ShouldNotBeNull();
        currentCapture.FrameNumber.ShouldBeGreaterThan(oldCapture.FrameNumber);
        currentCapture.ContainsText("Approval required").ShouldBeTrue();
        currentCapture.Layouts.ContainsKey("approve-tool").ShouldBeTrue();
        terminal.CommitSnapshot(currentCapture);

        terminal.OnNext(oldNotification);
        terminal.CommitSnapshot(oldCapture);

        terminal.Snapshot.ContainsText("Approval required").ShouldBeTrue();
        terminal.Snapshot.Layouts.ContainsKey("approve-tool").ShouldBeTrue();
        oldCapture.ContainsText("Working").ShouldBeTrue();
        terminal.Frames.Select(frame => frame.FrameNumber).ShouldBeInOrder();
    }

    private static TestTerminalSnapshot CreateSnapshot(long number, string text, string hook)
    {
        var canvas = new TerminalCanvas(20, 1);
        canvas.Write(0, 0, text);
        return new TestTerminalSnapshot(number, DateTimeOffset.UtcNow, canvas, 20, 1, null,
            new Dictionary<string, VNodeLayoutInfo> { [hook] = new() });
    }

    internal sealed class SnapshotComponent : ComponentBase
    {
        private bool _approval;

        [Inject]
        public ConsoleRenderer Renderer { get; set; } = null!;

        [Parameter]
        public Action<SnapshotComponent> Initialized { get; set; } = null!;

        protected override void OnInitialized() => Initialized(this);

        public Task ShowApprovalAsync() => InvokeAsync(() =>
        {
            _approval = true;
            StateHasChanged();
        });

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, IVNodeIdAccessor.HookAttributeName, _approval ? "approve-tool" : "composer");
            builder.AddAttribute(2, "data-text", "true");
            builder.AddAttribute(3, "data-content", _approval ? "Approval required" : "Working");
            builder.CloseElement();
        }
    }
}
