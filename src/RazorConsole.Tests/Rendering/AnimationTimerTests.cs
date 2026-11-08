// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Controllers;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Rendering;

public sealed class AnimationTimerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task Run_RefreshesTheCanvasEveryInterval()
    {
        var canvas = new CountingCanvas();
        using var timer = new ConsoleLiveDisplayContext.AnimationTimer(canvas);

        timer.Run(Interval);

        await WaitUntilAsync(() => canvas.Refreshes >= 3);
    }

    [Fact]
    public async Task Run_InfiniteInterval_StopsRefreshing()
    {
        var canvas = new CountingCanvas();
        using var timer = new ConsoleLiveDisplayContext.AnimationTimer(canvas);
        timer.Run(Interval);
        await WaitUntilAsync(() => canvas.Refreshes >= 1);

        timer.Run(Timeout.InfiniteTimeSpan);
        await Task.Delay(Interval * 2, TestContext.Current.CancellationToken);
        var stopped = canvas.Refreshes;
        await Task.Delay(Interval * 5, TestContext.Current.CancellationToken);

        canvas.Refreshes.ShouldBe(stopped);
    }

    [Fact]
    public async Task Tick_WhileARefreshRuns_IsSkipped()
    {
        using var release = new ManualResetEventSlim();
        var canvas = new CountingCanvas { Block = release };
        using var timer = new ConsoleLiveDisplayContext.AnimationTimer(canvas);

        timer.Run(TimeSpan.FromMilliseconds(5));
        await WaitUntilAsync(() => canvas.Refreshes >= 1);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        canvas.Refreshes.ShouldBe(1);
        release.Set();
        await WaitUntilAsync(() => canvas.Refreshes >= 2);
        canvas.MaxConcurrent.ShouldBe(1);
    }

    [Fact]
    public async Task ManySpinners_RefreshTheCanvasOncePerInterval()
    {
        var canvas = new CountingCanvas();
        var spinners = Enumerable.Range(0, 50)
            .Select(_ => (IAnimatedConsoleRenderable)new AnimatedRenderable(TimeSpan.FromMilliseconds(50)))
            .ToArray();
        var view = ConsoleViewResult.Create("<div></div>", VNode.CreateElement("div"), new Text(""), spinners);
        using var context = ConsoleLiveDisplayContext.CreateForTesting(canvas, view, diffService: null);

        await Task.Delay(500, TestContext.Current.CancellationToken);

        // One timer per spinner would repaint about 500 times here.
        canvas.Refreshes.ShouldBeLessThan(25);
        canvas.Refreshes.ShouldBeGreaterThan(0);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private sealed class CountingCanvas : ConsoleLiveDisplayContext.ILiveDisplayCanvas
    {
        private int _refreshes;
        private int _running;
        private int _maxConcurrent;

        public ManualResetEventSlim? Block { get; init; }

        public int Refreshes => Volatile.Read(ref _refreshes);

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public void Refresh()
        {
            var running = Interlocked.Increment(ref _running);
            InterlockedMax(ref _maxConcurrent, running);
            Interlocked.Increment(ref _refreshes);
            Block?.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Decrement(ref _running);
            Refreshed?.Invoke();
        }

        public void UpdateTarget(IRenderable? renderable)
        {
        }

        public bool TryReplaceNode(IReadOnlyList<int> path, IRenderable renderable) => false;

        public bool TryUpdateText(IReadOnlyList<int> path, string? text) => false;

        public bool TryUpdateAttributes(IReadOnlyList<int> path, IReadOnlyDictionary<string, string?> attributes) => false;

        public event Action? Refreshed;

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value
                   && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    private sealed class AnimatedRenderable(TimeSpan interval) : IAnimatedConsoleRenderable
    {
        public TimeSpan RefreshInterval => interval;

        public Measurement Measure(RenderOptions options, int maxWidth) => new(0, 0);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => [];
    }
}
