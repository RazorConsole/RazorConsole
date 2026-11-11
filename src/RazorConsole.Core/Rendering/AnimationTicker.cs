// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering.ComponentMarkup;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// Drives every animation on screen from one timer.
/// </summary>
/// <remarks>
/// Animated renderables pick their frame from the clock, so a single tick at the shortest
/// interval refreshes all of them. The timer runs only while at least one animation is on screen.
/// </remarks>
internal sealed class AnimationTicker : IDisposable
{
    /// <summary>
    /// The shortest interval the ticker runs at, about 60 frames per second.
    /// </summary>
    internal static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(16);

    private readonly Action _tick;
    private readonly Lock _sync = new();
    private Timer? _timer;
    private bool _disposed;

    public AnimationTicker(Action tick)
    {
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
    }

    /// <summary>
    /// Gets the interval the ticker currently runs at, or <see langword="null"/> when it is stopped.
    /// </summary>
    public TimeSpan? Interval { get; private set; }

    /// <summary>
    /// Matches the ticker to the animations in the latest frame. An unchanged interval keeps the running timer.
    /// </summary>
    public void Update(IReadOnlyCollection<IAnimatedConsoleRenderable>? animations)
    {
        var interval = ResolveInterval(animations);
        lock (_sync)
        {
            if (_disposed || interval == Interval)
            {
                return;
            }

            Interval = interval;
            if (interval is not { } period)
            {
                _timer?.Dispose();
                _timer = null;
            }
            else if (_timer is null)
            {
                _timer = new Timer(static state => ((AnimationTicker)state!).OnTick(), this, period, period);
            }
            else
            {
                _timer.Change(period, period);
            }
        }
    }

    internal static TimeSpan? ResolveInterval(IReadOnlyCollection<IAnimatedConsoleRenderable>? animations)
    {
        if (animations is null || animations.Count == 0)
        {
            return null;
        }

        TimeSpan? shortest = null;
        foreach (var animation in animations)
        {
            var interval = animation.RefreshInterval;
            if (interval <= TimeSpan.Zero)
            {
                continue;
            }

            if (shortest is null || interval < shortest)
            {
                shortest = interval;
            }
        }

        return shortest is { } value && value < MinimumInterval ? MinimumInterval : shortest;
    }

    private void OnTick()
    {
        try
        {
            _tick();
        }
        catch
        {
            // Ignore rendering exceptions from background updates.
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Interval = null;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
