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
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Testing;

/// <summary>
/// Hosts a RazorConsole component in a fixed-size in-memory terminal, without a real console.
/// Send keyboard, mouse, and raw terminal input, resize the terminal, and inspect the rendered frames.
/// </summary>
/// <remarks>
/// Input methods and <see cref="Resize(int, int)"/> are serialized: concurrent calls run one at a time.
/// Every input call captures a frame after the input has been handled.
/// </remarks>
public sealed class HeadlessTerminal : IAsyncDisposable
{
    private const int MaximumFrameHistory = 50;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly ServiceProvider _services;
    private readonly TerminalMonitor _terminalMonitor;
    private readonly HeadlessConsoleInput _consoleInput = new();
    private readonly ConsoleRenderer _renderer;
    private readonly VNodeLayoutAccessor _layoutAccessor;
    private readonly FocusManager _focusManager;
    private readonly KeyboardEventManager _keyboardEventManager;
    private readonly MouseEventManager _mouseEventManager;
    private readonly TerminalInputDispatcher _terminalInputDispatcher;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<HeadlessTerminalFrame> _frames = [];
    private readonly AnsiOutputWriter? _ansiOutput;
    private readonly IAnsiConsole? _ansiConsole;
    private IDisposable? _snapshotSubscription;
    private IDisposable? _focusSubscription;
    private ConsoleLiveDisplayContext? _liveContext;
    private FocusManager.FocusSession? _focusSession;
    private Exception? _observerError;
    private long _frameNumber;
    private volatile bool _disposed;

    private HeadlessTerminal(HeadlessTerminalOptions options)
    {
        _terminalMonitor = new TerminalMonitor(options.Width, options.Height);

        var services = new ServiceCollection();
        services.AddSingleton(_terminalMonitor);
        services.AddSingleton<IConsoleInput>(_consoleInput);
        services.AddRazorConsoleServices();
        options.ConfigureServices?.Invoke(services);
        services.Configure<ConsoleAppOptions>(appOptions =>
        {
            appOptions.RenderingPipeline = RazorConsoleRenderingPipeline.WidgetLayout;
            appOptions.EnableTerminalResizing = true;
        });

        _services = services.BuildServiceProvider();
        _renderer = _services.GetRequiredService<ConsoleRenderer>();
        _layoutAccessor = _services.GetRequiredService<VNodeLayoutAccessor>();
        _focusManager = _services.GetRequiredService<FocusManager>();
        _keyboardEventManager = _services.GetRequiredService<KeyboardEventManager>();
        _mouseEventManager = _services.GetRequiredService<MouseEventManager>();
        _terminalInputDispatcher = _services.GetRequiredService<TerminalInputDispatcher>();

        if (options.CaptureAnsiOutput)
        {
            _ansiOutput = new AnsiOutputWriter();
            _ansiConsole = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
                Out = new AnsiConsoleOutput(_ansiOutput),
            });
            _ansiConsole.Profile.Capabilities.Unicode = true;
            _ansiConsole.Profile.Width = options.Width;
            _ansiConsole.Profile.Height = options.Height;
        }
    }

    /// <summary>
    /// Gets the service provider of the hosted application.
    /// </summary>
    public IServiceProvider Services => _services;

    /// <summary>
    /// Gets the current terminal width in columns.
    /// </summary>
    public int Width => _terminalMonitor.Width;

    /// <summary>
    /// Gets the current terminal height in rows.
    /// </summary>
    public int Height => _terminalMonitor.Height;

    /// <summary>
    /// Gets the most recently captured frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">No frame has been rendered yet.</exception>
    public HeadlessTerminalFrame CurrentFrame
    {
        get
        {
            lock (_sync)
            {
                return _frames.Count > 0
                    ? _frames[^1]
                    : throw new InvalidOperationException("The headless terminal has not rendered a frame.");
            }
        }
    }

    /// <summary>
    /// Gets the recently captured frames, oldest first. At most 50 frames are kept,
    /// and a frame identical to the one before it is not recorded.
    /// </summary>
    public IReadOnlyList<HeadlessTerminalFrame> Frames
    {
        get
        {
            lock (_sync)
            {
                return _frames.ToArray();
            }
        }
    }

    /// <summary>
    /// Gets the focus key of the currently focused element, if any.
    /// </summary>
    public string? FocusKey => _focusManager.CurrentFocusKey;

    /// <summary>
    /// Gets the ANSI output written to the terminal since it started.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="HeadlessTerminalOptions.CaptureAnsiOutput"/> is not enabled.</exception>
    public string AnsiOutput
        => _ansiOutput?.ToString()
            ?? throw new InvalidOperationException(
                $"ANSI output is not captured. Set {nameof(HeadlessTerminalOptions)}.{nameof(HeadlessTerminalOptions.CaptureAnsiOutput)} to true.");

    /// <summary>
    /// Mounts <typeparamref name="TComponent"/> in a new headless terminal.
    /// </summary>
    /// <typeparam name="TComponent">The component to mount, usually the application's root component.</typeparam>
    /// <param name="options">The terminal options.</param>
    /// <param name="cancellationToken">A token that cancels the start.</param>
    /// <returns>The started terminal, after the first frame has been captured and focus has been initialized.</returns>
    public static async Task<HeadlessTerminal> StartAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        HeadlessTerminalOptions options,
        CancellationToken cancellationToken = default)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Width, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Height, 1, nameof(options));

        var terminal = new HeadlessTerminal(options);
        try
        {
            await terminal.StartCoreAsync<TComponent>(options.Parameters, cancellationToken).ConfigureAwait(false);
            return terminal;
        }
        catch
        {
            await terminal.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Mounts <typeparamref name="TComponent"/> in a new headless terminal of the given size.
    /// </summary>
    /// <typeparam name="TComponent">The component to mount, usually the application's root component.</typeparam>
    /// <param name="width">The terminal width in columns.</param>
    /// <param name="height">The terminal height in rows.</param>
    /// <param name="configureServices">Registers the application's own services.</param>
    /// <param name="cancellationToken">A token that cancels the start.</param>
    /// <returns>The started terminal, after the first frame has been captured and focus has been initialized.</returns>
    public static Task<HeadlessTerminal> StartAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        int width,
        int height,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default)
        where TComponent : IComponent
        => StartAsync<TComponent>(
            new HeadlessTerminalOptions { Width = width, Height = height, ConfigureServices = configureServices },
            cancellationToken);

    /// <summary>
    /// Sends one key press to the focused element.
    /// </summary>
    /// <param name="key">The key, including its character and modifiers.</param>
    /// <param name="cancellationToken">A token that cancels the input.</param>
    public Task SendKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken = default)
        => RunInputAsync(token => _keyboardEventManager.HandleKeyAsync(key, token), cancellationToken);

    /// <summary>
    /// Sends one key press to the focused element.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="keyChar">The character the key produces, or <c>'\0'</c> for none.</param>
    /// <param name="modifiers">The modifier keys held down.</param>
    /// <param name="cancellationToken">A token that cancels the input.</param>
    public Task SendKeyAsync(
        ConsoleKey key,
        char keyChar = '\0',
        ConsoleModifiers modifiers = 0,
        CancellationToken cancellationToken = default)
        => SendKeyAsync(
            new ConsoleKeyInfo(
                keyChar,
                key,
                (modifiers & ConsoleModifiers.Shift) != 0,
                (modifiers & ConsoleModifiers.Alt) != 0,
                (modifiers & ConsoleModifiers.Control) != 0),
            cancellationToken);

    /// <summary>
    /// Types <paramref name="text"/> one key press per character, mapped the way a VT terminal sends them:
    /// <c>\r</c> and <c>\n</c> are Enter, <c>\t</c> is Tab, <c>\b</c> and DEL are Backspace,
    /// upper-case letters add Shift, and other control characters are Ctrl+letter.
    /// </summary>
    /// <param name="text">The text to type. Use <see cref="SendTerminalInputAsync"/> for escape sequences.</param>
    /// <param name="cancellationToken">A token that cancels the input.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains an ESC character.</exception>
    public Task TypeAsync(string text, CancellationToken cancellationToken = default)
    {
        var keys = DecodeText(text);
        return RunInputAsync(
            async token =>
            {
                foreach (var key in keys)
                {
                    await _keyboardEventManager.HandleKeyAsync(key, token).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Pastes <paramref name="text"/> the way RazorConsole handles a paste from a real terminal:
    /// each run of text reaches the focused input as a single <c>oninput</c> event,
    /// and Enter or Tab between runs are handled as key presses.
    /// Characters map to keys as in <see cref="TypeAsync"/>.
    /// </summary>
    /// <param name="text">The text to paste.</param>
    /// <param name="cancellationToken">A token that cancels the input.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains an ESC character.</exception>
    public Task PasteAsync(string text, CancellationToken cancellationToken = default)
    {
        var keys = DecodeText(text);
        return RunInputAsync(
            async token =>
            {
                _consoleInput.Enqueue(keys);
                try
                {
                    // The same batching decision as KeyboardEventManager.RunAsync.
                    while (_consoleInput.KeyAvailable)
                    {
                        var key = _consoleInput.ReadKey(intercept: true);
                        if (KeyboardEventManager.ShouldBatchInput(key) && _consoleInput.KeyAvailable && !_keyboardEventManager.IsManagedFocus())
                        {
                            await _keyboardEventManager.HandleBatchedTextInputAsync(key, token).ConfigureAwait(false);
                        }
                        else
                        {
                            await _keyboardEventManager.HandleKeyAsync(key, token).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    _consoleInput.Clear();
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends one mouse event.
    /// </summary>
    /// <param name="input">The mouse event, in zero-based terminal-cell coordinates.</param>
    /// <param name="cancellationToken">A token that cancels the input.</param>
    public Task SendMouseAsync(TerminalMouseEvent input, CancellationToken cancellationToken = default)
        => RunInputAsync(token => _mouseEventManager.HandleAsync(input, token), cancellationToken);

    /// <summary>
    /// Clicks the left mouse button at a cell: a press followed by a release,
    /// with a frame captured after each.
    /// </summary>
    /// <param name="x">The zero-based column.</param>
    /// <param name="y">The zero-based row.</param>
    /// <param name="cancellationToken">A token that cancels the input.</param>
    public Task ClickAsync(int x, int y, CancellationToken cancellationToken = default)
        => RunInputAsync(
            async token =>
            {
                await _mouseEventManager.HandleAsync(new TerminalMouseEvent(TerminalMouseKind.Down, x, y), token).ConfigureAwait(false);
                Capture(_renderer.RefreshSnapshot());
                await _mouseEventManager.HandleAsync(new TerminalMouseEvent(TerminalMouseKind.Up, x, y), token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>
    /// Sends raw terminal input, decoded the same way as input from a real VT terminal
    /// (keys, escape sequences, and SGR mouse reports).
    /// </summary>
    /// <param name="data">The raw input, for example <c>"\u001b[A"</c> for the Up arrow.</param>
    /// <param name="cancellationToken">A token that cancels the input.</param>
    public Task SendTerminalInputAsync(string data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return RunInputAsync(token => _terminalInputDispatcher.HandleAsync(data, token), cancellationToken);
    }

    /// <summary>
    /// Resizes the terminal and captures a frame at the new size.
    /// </summary>
    /// <param name="width">The new width in columns.</param>
    /// <param name="height">The new height in rows.</param>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ThrowIfDisposed();

        _inputGate.Wait();
        try
        {
            ThrowIfDisposed();
            if (_ansiConsole is not null)
            {
                _ansiConsole.Profile.Width = width;
                _ansiConsole.Profile.Height = height;
            }
            _terminalMonitor.Resize(width, height);
            Capture(_renderer.RefreshSnapshot());
        }
        finally
        {
            _inputGate.Release();
        }
    }

    /// <summary>
    /// Gets the current layout of the element with the given <c>data-vnode-hook</c>.
    /// </summary>
    /// <param name="hookKey">The hook key.</param>
    /// <exception cref="KeyNotFoundException">No layout was found for <paramref name="hookKey"/>.</exception>
    public VNodeLayoutInfo GetLayout(string hookKey)
        => _layoutAccessor.GetLayoutByHookKeyOrDefault(hookKey)
            ?? throw new KeyNotFoundException($"No layout was found for hook '{hookKey}'.");

    /// <summary>
    /// Tries to get the current layout of the element with the given <c>data-vnode-hook</c>.
    /// </summary>
    /// <param name="hookKey">The hook key.</param>
    /// <param name="layout">The layout, when found.</param>
    public bool TryGetLayout(string hookKey, out VNodeLayoutInfo layout)
        => _layoutAccessor.TryGetLayoutByHookKey(hookKey, out layout);

    /// <summary>
    /// Waits until the current frame satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <param name="predicate">The condition to wait for.</param>
    /// <param name="timeout">The maximum time to wait. Defaults to 2 seconds.</param>
    /// <param name="description">A description of the awaited state, used in the timeout message.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>The first frame that satisfied <paramref name="predicate"/>.</returns>
    /// <exception cref="TerminalTimeoutException">The condition was not met within <paramref name="timeout"/>.</exception>
    /// <exception cref="InvalidOperationException">Rendering failed while waiting.</exception>
    public async Task<HeadlessTerminalFrame> WaitUntilAsync(
        Func<HeadlessTerminalFrame, bool> predicate,
        TimeSpan? timeout = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ThrowIfDisposed();

        var limit = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Exception? observerError;
            HeadlessTerminalFrame? frame;
            lock (_sync)
            {
                observerError = _observerError;
                frame = _frames.Count > 0 ? _frames[^1] : null;
            }

            if (observerError is not null)
            {
                throw new InvalidOperationException(
                    $"Rendering failed while waiting for terminal state.{Environment.NewLine}{DumpDiagnostics()}",
                    observerError);
            }

            if (frame is not null && predicate(frame))
            {
                return frame;
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        var diagnostics = DumpDiagnostics();
        throw new TerminalTimeoutException(
            $"Timed out after {limit} waiting for {description ?? "the requested terminal state"}.{Environment.NewLine}{diagnostics}",
            diagnostics);
    }

    /// <summary>
    /// Waits until the screen text contains <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text to wait for (ordinal comparison).</param>
    /// <param name="timeout">The maximum time to wait. Defaults to 2 seconds.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>The first frame that contained <paramref name="text"/>.</returns>
    /// <exception cref="TerminalTimeoutException">The text did not appear within <paramref name="timeout"/>.</exception>
    public Task<HeadlessTerminalFrame> WaitForTextAsync(string text, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WaitUntilAsync(frame => frame.ContainsText(text), timeout, $"text '{text}'", cancellationToken);
    }

    /// <summary>
    /// Formats the terminal state for failure messages: frame count, focus, the last mouse dispatch,
    /// any rendering error, and the most recent frames.
    /// </summary>
    /// <param name="frameCount">How many of the most recent frames to include.</param>
    public string DumpDiagnostics(int frameCount = 3)
    {
        HeadlessTerminalFrame[] frames;
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
            .Append(", current focus: ").AppendLine(FocusKey ?? "<none>");
        builder.AppendLine(_mouseEventManager.LastDispatch);

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

    /// <summary>
    /// Stops the terminal and disposes the hosted application's services.
    /// </summary>
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

    private async Task StartCoreAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
        where TComponent : IComponent
    {
        _snapshotSubscription = _renderer.Subscribe(new SnapshotObserver(this));
        var parameterView = parameters is null
            ? ParameterView.Empty
            : ParameterView.FromDictionary(new Dictionary<string, object?>(parameters, StringComparer.Ordinal));
        var renderSnapshot = await _renderer.MountComponentAsync<TComponent>(parameterView, cancellationToken).ConfigureAwait(false);
        Capture(renderSnapshot);

        var view = ConsoleViewResult.FromSnapshot(renderSnapshot);
        _liveContext = new ConsoleLiveDisplayContext(
            _ansiConsole is not null
                ? new LiveDisplayCanvas(new ConsoleLiveDisplayOptions { EnableMouseEvents = true }, _ansiConsole)
                : new NoopLiveDisplayCanvas(),
            _renderer,
            _terminalMonitor,
            view);
        _focusSubscription = _renderer.Subscribe(_focusManager);
        _focusSession = _focusManager.BeginSession(_liveContext, view, _shutdown.Token);
        await _focusSession.InitializationTask.ConfigureAwait(false);
        Capture(_renderer.RefreshSnapshot());
    }

    private async Task RunInputAsync(Func<CancellationToken, Task> input, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await input(cancellationToken).ConfigureAwait(false);
            Capture(_renderer.RefreshSnapshot());
        }
        finally
        {
            _inputGate.Release();
        }
    }

    private static List<ConsoleKeyInfo> DecodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\u001b', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The text contains an ESC character. Use {nameof(SendTerminalInputAsync)} to send escape sequences.",
                nameof(text));
        }

        // Without ESC, the VT decoder maps each character to exactly one key.
        var decoder = new TerminalInputDecoder();
        decoder.Feed(text, Environment.TickCount64);
        var keys = new List<ConsoleKeyInfo>(text.Length);
        while (decoder.KeyAvailable)
        {
            keys.Add(decoder.ReadKey());
        }

        return keys;
    }

    private void Capture(ConsoleRenderer.RenderSnapshot renderSnapshot)
    {
        if (renderSnapshot.Renderable is not WidgetCanvasRenderable renderable || renderSnapshot.Root is null)
        {
            return;
        }

        var canvas = renderable.PaintToCanvas();
        var layouts = CollectHookLayouts(renderSnapshot.Root);
        var frame = new HeadlessTerminalFrame(
            Interlocked.Increment(ref _frameNumber),
            DateTimeOffset.UtcNow,
            canvas,
            _terminalMonitor.Width,
            _terminalMonitor.Height,
            FocusKey,
            layouts);

        lock (_sync)
        {
            if (_frames.Count > 0 && IsEquivalent(_frames[^1], frame))
            {
                return;
            }

            _frames.Add(frame);
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

    private static bool IsEquivalent(HeadlessTerminalFrame left, HeadlessTerminalFrame right)
        => left.Width == right.Width
            && left.Height == right.Height
            && string.Equals(left.FocusKey, right.FocusKey, StringComparison.Ordinal)
            && left.HasSameCells(right)
            && left.Layouts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(right.Layouts.OrderBy(pair => pair.Key, StringComparer.Ordinal));

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class SnapshotObserver(HeadlessTerminal terminal) : IObserver<ConsoleRenderer.RenderSnapshot>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            lock (terminal._sync)
            {
                terminal._observerError = error;
            }
        }

        public void OnNext(ConsoleRenderer.RenderSnapshot value)
            => terminal.Capture(value);
    }

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

    private sealed class AnsiOutputWriter : TextWriter
    {
        private readonly StringBuilder _buffer = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_buffer)
            {
                _buffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_buffer)
            {
                _buffer.Append(value);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_buffer)
            {
                _buffer.Append(buffer, index, count);
            }
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            lock (_buffer)
            {
                _buffer.Append(buffer);
            }
        }

        public override string ToString()
        {
            lock (_buffer)
            {
                return _buffer.ToString();
            }
        }
    }
}
