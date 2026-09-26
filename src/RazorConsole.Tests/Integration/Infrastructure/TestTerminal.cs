// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Integration.Infrastructure;

internal sealed class TestTerminal : IObserver<ConsoleRenderer.RenderSnapshot>, IAsyncDisposable
{
    private const int MaximumFrameHistory = 50;
    private readonly object _sync = new();
    private readonly ServiceProvider _services;
    private readonly TerminalMonitor _terminalMonitor;
    private readonly ConsoleRenderer _renderer;
    private readonly VNodeLayoutAccessor _layoutAccessor;
    private readonly FocusManager _focusManager;
    private readonly KeyboardEventManager _keyboardEventManager;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<TestTerminalSnapshot> _frames = [];
    private IDisposable? _snapshotSubscription;
    private IDisposable? _focusSubscription;
    private ConsoleLiveDisplayContext? _liveContext;
    private FocusManager.FocusSession? _focusSession;
    private Exception? _observerError;
    private long _frameNumber;
    private bool _disposed;

    private TestTerminal(int width, int height)
    {
        _terminalMonitor = new TerminalMonitor(width, height);

        var services = new ServiceCollection();
        services.AddSingleton(_terminalMonitor);
        services.AddRazorConsoleServices();
        services.Configure<ConsoleAppOptions>(options =>
        {
            options.RenderingPipeline = RazorConsoleRenderingPipeline.WidgetLayout;
            options.EnableTerminalResizing = true;
        });

        _services = services.BuildServiceProvider();
        _renderer = _services.GetRequiredService<ConsoleRenderer>();
        _layoutAccessor = _services.GetRequiredService<VNodeLayoutAccessor>();
        _focusManager = _services.GetRequiredService<FocusManager>();
        _keyboardEventManager = _services.GetRequiredService<KeyboardEventManager>();
    }

    public TestTerminalSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _frames.Count > 0
                    ? _frames[^1]
                    : throw new InvalidOperationException("The test terminal has not rendered a frame.");
            }
        }
    }

    public IReadOnlyList<TestTerminalSnapshot> Frames
    {
        get
        {
            lock (_sync)
            {
                return _frames.ToArray();
            }
        }
    }

    public string? CurrentFocusKey => _focusManager.CurrentFocusKey;

    public static async Task<TestTerminal> StartAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        int width,
        int height,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
        where TComponent : IComponent
    {
        var terminal = new TestTerminal(width, height);
        try
        {
            await terminal.StartCoreAsync<TComponent>(parameters, cancellationToken).ConfigureAwait(false);
            return terminal;
        }
        catch
        {
            await terminal.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task SendKeyAsync(
        ConsoleKey key,
        char keyChar = '\0',
        ConsoleModifiers modifiers = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var keyInfo = new ConsoleKeyInfo(
            keyChar,
            key,
            (modifiers & ConsoleModifiers.Shift) != 0,
            (modifiers & ConsoleModifiers.Alt) != 0,
            (modifiers & ConsoleModifiers.Control) != 0);

        await _keyboardEventManager.HandleKeyAsync(keyInfo, cancellationToken).ConfigureAwait(false);
        Capture(_renderer.RefreshSnapshot());
    }

    public void Resize(int width, int height)
    {
        ThrowIfDisposed();
        _terminalMonitor.Resize(width, height);
        Capture(_renderer.RefreshSnapshot());
    }

    public VNodeLayoutInfo GetLayout(string hookKey)
        => _layoutAccessor.GetLayoutByHookKeyOrDefault(hookKey)
            ?? throw new KeyNotFoundException($"No layout was found for hook '{hookKey}'.");

    public async Task<TestTerminalSnapshot> WaitUntilAsync(
        Func<TestTerminalSnapshot, bool> predicate,
        TimeSpan? timeout = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ThrowIfDisposed();

        var limit = timeout ?? TimeSpan.FromSeconds(2);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Exception? observerError;
            TestTerminalSnapshot? snapshot;
            lock (_sync)
            {
                observerError = _observerError;
                snapshot = _frames.Count > 0 ? _frames[^1] : null;
            }

            if (observerError is not null)
            {
                throw new InvalidOperationException(
                    $"Rendering failed while waiting for terminal state.{Environment.NewLine}{DumpDiagnostics()}",
                    observerError);
            }

            if (snapshot is not null && predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Timed out after {limit} waiting for {description ?? "the requested terminal state"}.{Environment.NewLine}{DumpDiagnostics()}");
    }

    public string DumpDiagnostics(int frameCount = 3)
    {
        TestTerminalSnapshot[] frames;
        Exception? observerError;
        int totalFrameCount;
        lock (_sync)
        {
            frames = _frames.TakeLast(Math.Max(1, frameCount)).ToArray();
            observerError = _observerError;
            totalFrameCount = _frames.Count;
        }

        var builder = new StringBuilder();
        builder.Append("Captured frames: ").Append(totalFrameCount)
            .Append(", current focus: ").AppendLine(CurrentFocusKey ?? "<none>");

        if (observerError is not null)
        {
            builder.Append("Observer error: ").AppendLine(observerError.ToString());
        }

        foreach (var frame in frames)
        {
            builder.AppendLine(frame.ToDiagnosticString());
            builder.AppendLine(new string('-', Math.Max(1, Math.Min(frame.Width, 80))));
        }

        return builder.ToString().TrimEnd();
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
        lock (_sync)
        {
            _observerError = error;
        }
    }

    public void OnNext(ConsoleRenderer.RenderSnapshot value)
        => Capture(value);

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _shutdown.Cancel();
        _focusSession?.Dispose();
        _focusSubscription?.Dispose();
        _snapshotSubscription?.Dispose();
        _liveContext?.Dispose();
        _shutdown.Dispose();
        _services.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task StartCoreAsync<TComponent>(
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
        where TComponent : IComponent
    {
        _snapshotSubscription = _renderer.Subscribe(this);
        var parameterView = parameters is null
            ? ParameterView.Empty
            : ParameterView.FromDictionary(new Dictionary<string, object?>(parameters, StringComparer.Ordinal));
        var renderSnapshot = await _renderer.MountComponentAsync<TComponent>(parameterView, cancellationToken).ConfigureAwait(false);
        Capture(renderSnapshot);

        var view = ConsoleViewResult.FromSnapshot(renderSnapshot);
        _liveContext = new ConsoleLiveDisplayContext(
            new NoopLiveDisplayCanvas(),
            _renderer,
            _terminalMonitor,
            view);
        _focusSubscription = _renderer.Subscribe(_focusManager);
        _focusSession = _focusManager.BeginSession(_liveContext, view, _shutdown.Token);
        await _focusSession.InitializationTask.ConfigureAwait(false);
        Capture(_renderer.RefreshSnapshot());
    }

    private void Capture(ConsoleRenderer.RenderSnapshot renderSnapshot)
    {
        if (renderSnapshot.Renderable is not WidgetCanvasRenderable renderable || renderSnapshot.Root is null)
        {
            return;
        }

        var canvas = renderable.PaintToCanvas();
        var layouts = CollectHookLayouts(renderSnapshot.Root);
        var snapshot = new TestTerminalSnapshot(
            Interlocked.Increment(ref _frameNumber),
            DateTimeOffset.UtcNow,
            canvas,
            _terminalMonitor.Width,
            _terminalMonitor.Height,
            CurrentFocusKey,
            layouts);

        lock (_sync)
        {
            if (_frames.Count > 0 && IsEquivalent(_frames[^1], snapshot))
            {
                return;
            }

            _frames.Add(snapshot);
            if (_frames.Count > MaximumFrameHistory)
            {
                _frames.RemoveAt(0);
            }
        }
    }

    private Dictionary<string, VNodeLayoutInfo> CollectHookLayouts(VNode root)
    {
        var layouts = new Dictionary<string, VNodeLayoutInfo>(StringComparer.Ordinal);
        var pending = new Stack<VNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (node.Attributes.TryGetValue(IVNodeIdAccessor.HookAttributeName, out var hook)
                && !string.IsNullOrWhiteSpace(hook)
                && _layoutAccessor.TryGetLayoutByHookKey(hook, out var layout))
            {
                layouts[hook] = layout;
            }

            foreach (var child in node.Children)
            {
                pending.Push(child);
            }
        }

        return layouts;
    }

    private static bool IsEquivalent(TestTerminalSnapshot left, TestTerminalSnapshot right)
        => left.Width == right.Width
            && left.Height == right.Height
            && string.Equals(left.FocusKey, right.FocusKey, StringComparison.Ordinal)
            && string.Equals(left.ScreenText, right.ScreenText, StringComparison.Ordinal)
            && left.Layouts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(right.Layouts.OrderBy(pair => pair.Key, StringComparer.Ordinal));

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class NoopLiveDisplayCanvas : ConsoleLiveDisplayContext.ILiveDisplayCanvas
    {
        public event Action? Refreshed;

        public void UpdateTarget(IRenderable? renderable)
        {
        }

        public bool TryReplaceNode(IReadOnlyList<int> path, IRenderable renderable) => false;

        public bool TryUpdateText(IReadOnlyList<int> path, string? text) => false;

        public bool TryUpdateAttributes(IReadOnlyList<int> path, IReadOnlyDictionary<string, string?> attributes) => false;

        public void Refresh() => Refreshed?.Invoke();
    }
}
