// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RazorConsole.Core;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Website;

internal interface IRazorConsoleRenderer : IAsyncDisposable
{
    Task HandleTerminalInputAsync(string data);
    void HandleResize(int cols, int rows);
    event Action<string>? SnapshotRendered;
}

internal class RazorConsoleRenderer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent> : IRazorConsoleRenderer
    where TComponent : IComponent
{
    private readonly string _componentId;
    private readonly int _initialCols;
    private readonly int _initialRows;
    private IServiceProvider? _serviceProvider;
    private ConsoleRenderer? _consoleRenderer;
    private XTermTerminal? _terminal;
    private InputRouter? _input;
    private Task _inputLoop = Task.CompletedTask;
    private readonly CancellationTokenSource _shutdown = new();

    // One decoder for the terminal's input stream: an escape sequence split across onData chunks is still decoded.
    private readonly TerminalInputDecoder _decoder = new();
    private TerminalMonitor? _terminalMonitor;
    private ConsoleLiveDisplayContext? _liveContext;
    private FocusManager.FocusSession? _focusSession;
    private IDisposable? _focusSubscription;
    private bool _disposed;
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private FramePresenter? _canvas;
    private Task? _initializationTask;
    public event Action<string>? SnapshotRendered;

    public RazorConsoleRenderer(string componentId, int cols, int rows)
    {
        _componentId = componentId;
        _initialCols = cols > 0 ? cols : 80;
        _initialRows = rows > 0 ? rows : 150;
        _initializationTask = InitializeAsync();
    }

    private Task EnsureInitializedAsync()
    {
        if (_initializationTask is null)
        {
            _initializationTask = InitializeAsync();
        }
        return _initializationTask;
    }

    /// <summary>
    /// Initializes the rendering pipeline so components can be materialized outside the console host.
    /// </summary>
    private async Task InitializeAsync()
    {
        if (_serviceProvider is not null)
        {
            throw new InvalidOperationException("The renderer has already been initialized.");
        }

        var services = new ServiceCollection();
        services.Configure<ConsoleAppOptions>(options =>
        {
            options.ConsoleLiveDisplayOptions.EnableMouseEvents = true;
        });
        services.AddRazorConsoleServices();
        services.AddSingleton(new TerminalMonitor(_initialCols, _initialRows));

        _serviceProvider = services.BuildServiceProvider();
        _consoleRenderer = _serviceProvider.GetRequiredService<ConsoleRenderer>();
        _input = _serviceProvider.GetRequiredService<InputRouter>();
        var focusManager = _serviceProvider.GetRequiredService<FocusManager>();
        var options = _serviceProvider.GetRequiredService<IOptions<ConsoleAppOptions>>().Value;

        var snapshot = await _consoleRenderer.MountComponentAsync<TComponent>(ParameterView.Empty, default).ConfigureAwait(false);
        _focusSubscription = _consoleRenderer.Subscribe(focusManager);

        var initialView = ConsoleViewResult.FromSnapshot(snapshot);
        var terminalMonitor = _serviceProvider.GetRequiredService<TerminalMonitor>();
        _terminalMonitor = terminalMonitor;
        // Every presented frame is written to the component's xterm.js terminal as ANSI.
        _terminal = new XTermTerminal(
            options.ConsoleLiveDisplayOptions,
            _componentId,
            _initialCols,
            _initialRows,
            text => SnapshotRendered?.Invoke(text),
            () => _disposed);
        _canvas = new FramePresenter(
            _terminal,
            _serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<FramePresenter>());

        // Pass null for initialView to the context. This forces the context to treat the
        // canvas as empty/dirty and perform an initial render of the snapshot.
        var consoleLiveDisplayContext = new ConsoleLiveDisplayContext(_canvas, _consoleRenderer, terminalMonitor);
        _liveContext = consoleLiveDisplayContext;

        // Pass the actual initialView to FocusManager so it knows about the initial focusable elements.
        var focusSession = focusManager.BeginSession(consoleLiveDisplayContext, initialView, CancellationToken.None);
        _focusSession = focusSession;
        await focusSession.InitializationTask.ConfigureAwait(false);
        _inputLoop = _input.RunAsync(input: null, _shutdown.Token);
    }

    /// <summary>
    /// Render Razor component to ANSI string that can be sent to xterm.js.
    /// </summary>
    /// <returns></returns>
    public async Task<string> RenderAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        if (_consoleRenderer is null)
        {
            throw new InvalidOperationException("The renderer has not been initialized.");
        }
        var sw = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Out = new AnsiConsoleOutput(sw)
        });

        console.Profile.Width = _initialCols;
        console.Profile.Height = _initialRows;

        var consoleOption = new RenderOptions(console.Profile.Capabilities, new Size(_initialCols, _initialRows));

        var snapshot = await _consoleRenderer.MountComponentAsync<TComponent>(ParameterView.Empty, CancellationToken.None);

        try
        {
            console.Write(snapshot.Source!.Paint().ToRenderable());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error rendering component: {ex.Message} {ex.StackTrace}");

            throw;
        }

        return sw.ToString();
    }

    public async Task HandleTerminalInputAsync(string data)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        await _inputGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_disposed && _input is not null)
            {
                // xterm.js sends an empty string after an idle interval, which resolves a standalone Escape.
                var events = TerminalInputEvents.Decode(_decoder, data, endOfInput: false);
                if (events.Count > 0)
                {
                    await _input.Submit(events).Completion.ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _inputGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        await EnsureInitializedAsync().ConfigureAwait(false);
        await _inputGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);
            await _inputLoop.ConfigureAwait(false);
            _focusSession?.Dispose();
            _focusSubscription?.Dispose();
            // Async: on the single-threaded browser runtime, a blocking dispose would wait on the frame loop forever.
            if (_liveContext is not null)
            {
                await _liveContext.DisposeAsync().ConfigureAwait(false);
            }

            _terminalMonitor?.Dispose();
            if (_serviceProvider is IAsyncDisposable services)
            {
                await services.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _inputGate.Release();
        }
    }

    /// <summary>
    /// Handles terminal resize events from the browser by updating console dimensions and triggering a re-render.
    /// </summary>
    public void HandleResize(int cols, int rows)
    {
        if (_disposed || _terminal is null || _canvas is null || cols < 1 || rows < 1)
        {
            return;
        }

        // The terminal lays out its ANSI for the new size; the monitor lays the app out again.
        _terminal.Resize(cols, rows);
        _terminalMonitor?.Resize(cols, rows);

        // Trigger a refresh to re-render with the new dimensions
        _canvas.Refresh();
    }
}
