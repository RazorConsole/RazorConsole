// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RazorConsole.Core;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using PresentedFrame = RazorConsole.Core.Rendering.TerminalFrame;

namespace RazorConsole.Testing;

/// <summary>
/// Hosts a RazorConsole component in a fixed-size in-memory terminal, without a real console.
/// Send keyboard, mouse, and raw terminal input, resize the terminal, and inspect the rendered frames.
/// </summary>
/// <remarks>
/// Input methods and <see cref="Resize(int, int)"/> are serialized: concurrent calls run one at a time.
/// Every input call returns once the event handlers it raised have started (their code up to the first
/// <c>await</c> has run), and captures a frame. Like a real terminal, it does not wait for an <c>async</c> handler
/// to finish: wait for the state you expect with <see cref="WaitUntilAsync"/> or <see cref="WaitForTextAsync"/>.
/// </remarks>
public sealed class HeadlessTerminal : IAsyncDisposable
{
    private const int MaximumFrameHistory = 50;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private readonly object _captureSync = new();
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly ServiceProvider _services;
    private readonly TerminalMonitor _terminalMonitor;
    private readonly ConsoleRenderer _renderer;
    private readonly VNodeLayoutAccessor _layoutAccessor;
    private readonly FocusManager _focusManager;
    private readonly MouseEventManager _mouseEventManager;
    private readonly InputRouter _input;
    private readonly TerminalInputDecoder _terminalInputDecoder = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task _inputLoop = Task.CompletedTask;
    private readonly List<HeadlessTerminalFrame> _frames = [];
    private readonly AnsiOutputWriter? _ansiOutput;
    private readonly IAnsiConsole? _ansiConsole;
    private IDisposable? _snapshotSubscription;
    private RazorConsole.Core.Layout.ScrollStateRegistry? _scrollRegistry;
    private IDisposable? _focusSubscription;
    private ConsoleLiveDisplayContext? _liveContext;
    private FocusManager.FocusSession? _focusSession;
    private Exception? _observerError;
    private long _frameNumber;
    private long _capturedSequence = long.MinValue;
    private volatile bool _disposed;

    private HeadlessTerminal(HeadlessTerminalOptions options)
    {
        _terminalMonitor = new TerminalMonitor(options.Width, options.Height);

        var services = new ServiceCollection();
        services.AddSingleton(_terminalMonitor);
        services.AddSingleton<IConsoleInput>(new HeadlessConsoleInput());
        services.AddRazorConsoleServices();
        options.ConfigureServices?.Invoke(services);
        services.Configure<ConsoleAppOptions>(appOptions =>
        {
            appOptions.EnableTerminalResizing = true;
        });

        _services = services.BuildServiceProvider();
        _renderer = _services.GetRequiredService<ConsoleRenderer>();
        _layoutAccessor = _services.GetRequiredService<VNodeLayoutAccessor>();
        _focusManager = _services.GetRequiredService<FocusManager>();
        _mouseEventManager = _services.GetRequiredService<MouseEventManager>();
        _input = _services.GetRequiredService<InputRouter>();

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
    internal ConsoleRenderer Renderer => _renderer;

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
        => RunInputAsync(token => DispatchAsync([new KeyInputEvent(key)], token), cancellationToken);

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
            token => DispatchAsync(keys.Select(static key => (TerminalInputEvent)new KeyInputEvent(key)).ToArray(), token),
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

        // All the keys at once, as a real console reads a paste.
        return RunInputAsync(token => DispatchAsync([new KeyBatchInputEvent(keys)], token), cancellationToken);
    }

    /// <summary>
    /// Sends one mouse event.
    /// </summary>
    /// <param name="input">The mouse event, in zero-based terminal-cell coordinates.</param>
    /// <param name="cancellationToken">A token that cancels the input.</param>
    public Task SendMouseAsync(TerminalMouseEvent input, CancellationToken cancellationToken = default)
        => RunInputAsync(token => DispatchAsync([new MouseInputEvent(input)], token), cancellationToken);

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
                await DispatchAsync([new MouseInputEvent(new TerminalMouseEvent(TerminalMouseKind.Down, x, y))], token).ConfigureAwait(false);
                await PresentLatestAsync(token).ConfigureAwait(false);
                await DispatchAsync([new MouseInputEvent(new TerminalMouseEvent(TerminalMouseKind.Up, x, y))], token).ConfigureAwait(false);
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

        // One decoder for the whole stream: an escape sequence split across calls is still decoded.
        return RunInputAsync(
            token => DispatchAsync(TerminalInputEvents.Decode(_terminalInputDecoder, data, endOfInput: false), token),
            cancellationToken);
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
            PresentLatestAsync(CancellationToken.None).GetAwaiter().GetResult();
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
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        if (_scrollRegistry is not null)
        {
            _scrollRegistry.RepaintRequested -= OnScrollRepaintRequested;
        }

        try
        {
            await _inputLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _focusSession?.Dispose();
        _focusSubscription?.Dispose();
        _snapshotSubscription?.Dispose();
        if (_liveContext is not null)
        {
            await _liveContext.DisposeAsync().ConfigureAwait(false);
        }

        _shutdown.Dispose();
        await _services.DisposeAsync().ConfigureAwait(false);
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

        // Frames are recorded by the backend as the presenter presents them, so this terminal sees exactly the frames
        // an ANSI terminal would be sent, painted once.
        var view = ConsoleViewResult.FromSnapshot(renderSnapshot);
        _liveContext = new ConsoleLiveDisplayContext(
            new FramePresenter(
                new HeadlessBackend(
                    this,
                    _ansiConsole is not null
                        ? new AnsiTerminal(new ConsoleLiveDisplayOptions { EnableMouseEvents = true }, _ansiConsole)
                        : null),
                _services.GetService<ILoggerFactory>()?.CreateLogger<FramePresenter>()),
            _renderer,
            _terminalMonitor,
            view);
        _liveContext.Present(renderSnapshot);
        _focusSubscription = _renderer.Subscribe(_focusManager);
        _focusSession = _focusManager.BeginSession(_liveContext, view, _shutdown.Token);
        await _focusSession.InitializationTask.ConfigureAwait(false);
        _inputLoop = _input.RunAsync(input: null, _shutdown.Token);
        await PresentLatestAsync(cancellationToken).ConfigureAwait(false);

        // Scroll state changed by the engine (not by a Blazor render or by input sent through this terminal) repaints the host; capture it like one.
        if (_renderer.ScrollRegistry is { } scrollRegistry)
        {
            _scrollRegistry = scrollRegistry;
            scrollRegistry.RepaintRequested += OnScrollRepaintRequested;
        }
    }

    private void OnScrollRepaintRequested()
        => _ = Task.Run(async () =>
        {
            try
            {
                await _inputGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!_disposed)
                    {
                        await PresentLatestAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _inputGate.Release();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        });

    private async Task RunInputAsync(Func<CancellationToken, Task> input, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await input(cancellationToken).ConfigureAwait(false);
            await PresentLatestAsync(cancellationToken).ConfigureAwait(false);
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

    // Hands events to the app's input router, behind any input already waiting, and waits until their handlers started.
    // A cancelled wait withdraws them if the router has not started them yet.
    private async Task DispatchAsync(IReadOnlyList<TerminalInputEvent> events, CancellationToken cancellationToken)
    {
        var submission = _input.Submit(events);
        try
        {
            await submission.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            submission.TryCancel();
            throw;
        }
    }

    // Presents the latest render, even when its tree did not change (a focus move, a scroll, a resize), and waits until
    // the presenter presented it, so the frame and AnsiOutput include everything the caller did.
    private async Task PresentLatestAsync(CancellationToken cancellationToken)
    {
        if (_liveContext is null)
        {
            return;
        }

        _liveContext.Present(_renderer.RefreshSnapshot());
        await _liveContext.WhenIdleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Presents <paramref name="renderSnapshot"/> and waits for it. A snapshot from an earlier render than the frame on
    /// screen is ignored, so a late render notification never replaces a later frame.
    /// </summary>
    internal void Capture(ConsoleRenderer.RenderSnapshot renderSnapshot)
    {
        if (_liveContext is null)
        {
            return;
        }

        _liveContext.Present(renderSnapshot);
        _liveContext.WhenIdleAsync().GetAwaiter().GetResult();
    }

    // Called on the presenter's frame loop for every presented frame.
    private void Record(PresentedFrame presented)
    {
        if (presented.Source.Root is not { } root)
        {
            return;
        }

        // The presenter never goes back to an earlier render, but a frame is still never recorded over a later one.
        lock (_captureSync)
        {
            if (presented.Source.RenderSequence < _capturedSequence)
            {
                return;
            }

            _capturedSequence = presented.Source.RenderSequence;
            var layouts = CollectHookLayouts(root, presented.Source.Layouts);
            var frame = new HeadlessTerminalFrame(
                Interlocked.Increment(ref _frameNumber),
                DateTimeOffset.UtcNow,
                presented.Canvas,
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
    }

    // The layouts of the render that is painted, so the frame never pairs one render's picture with another's geometry.
    // The accessor holds the newest layout of all and only stands in when the render carries none.
    private Dictionary<string, VNodeLayoutInfo> CollectHookLayouts(VNode root, IReadOnlyCollection<VNodeLayoutInfo>? renderLayouts)
    {
        var byNode = renderLayouts?.ToDictionary(layout => layout.VNodeId, StringComparer.Ordinal);
        var layouts = new Dictionary<string, VNodeLayoutInfo>(StringComparer.Ordinal);
        var pending = new Stack<VNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (node.Attributes.TryGetValue(IVNodeIdAccessor.HookAttributeName, out var hook)
                && !string.IsNullOrWhiteSpace(hook))
            {
                if (byNode is not null)
                {
                    if (byNode.TryGetValue(node.ID, out var own))
                    {
                        layouts[hook] = own;
                    }
                }
                else if (_layoutAccessor.TryGetLayoutByHookKey(hook, out var layout))
                {
                    layouts[hook] = layout;
                }
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

        // Every render is presented, even one whose tree did not change, so a frame records a focus move it carries.
        public void OnNext(ConsoleRenderer.RenderSnapshot value)
            => terminal._liveContext?.Present(value);
    }

    /// <summary>
    /// The terminal backend of a headless terminal: it records every presented frame, and writes it as ANSI too when
    /// <see cref="HeadlessTerminalOptions.CaptureAnsiOutput"/> is set.
    /// </summary>
    private sealed class HeadlessBackend(HeadlessTerminal terminal, ITerminal? ansi) : ITerminal
    {
        public bool KeepsLastFrameAfterDispose => ansi?.KeepsLastFrameAfterDispose ?? true;

        public async ValueTask PresentAsync(PresentedFrame frame, CancellationToken cancellationToken = default)
        {
            try
            {
                if (ansi is not null)
                {
                    await ansi.PresentAsync(frame, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                // Recorded even when the ANSI write failed: the frame is what the app showed.
                terminal.Record(frame);
            }
        }

        public ValueTask DisposeAsync() => ansi?.DisposeAsync() ?? ValueTask.CompletedTask;
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
