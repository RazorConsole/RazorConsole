// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Website;

internal interface IRazorConsoleRenderer
{
    Task HandleKeyboardEventAsync(string xtermKey, string domKey, bool ctrlKey, bool altKey, bool shiftKey);
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
    private IAnsiConsole? _ansiConsole;
    private readonly StringWriter _sw = new StringWriter();
    private KeyboardEventManager? _keyboardEventManager;
    private LiveDisplayCanvas? _canvas;
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
        services.Configure<ConsoleAppOptions>(_ => { });
        services.AddRazorConsoleServices();

        _serviceProvider = services.BuildServiceProvider();
        _consoleRenderer = _serviceProvider.GetRequiredService<ConsoleRenderer>();
        _keyboardEventManager = _serviceProvider.GetRequiredService<KeyboardEventManager>();
        var focusManager = _serviceProvider.GetRequiredService<FocusManager>();

        _ansiConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(_sw)
        });

        _ansiConsole.Profile.Capabilities.Unicode = true;

        _ansiConsole.Profile.Width = _initialCols;
        _ansiConsole.Profile.Height = _initialRows;
        var snapshot = await _consoleRenderer.MountComponentAsync<TComponent>(ParameterView.Empty, default).ConfigureAwait(false);
        _consoleRenderer.Subscribe(focusManager);

        var initialView = ConsoleViewResult.FromSnapshot(snapshot);
        var terminalMonitor = _serviceProvider.GetRequiredService<TerminalMonitor>();
        _canvas = new LiveDisplayCanvas(_ansiConsole);

        // Subscribe to Refreshed BEFORE creating the context, so we catch the initial render.
        _canvas.Refreshed += () =>
        {
            var output = _sw.ToString();
            SnapshotRendered?.Invoke(output);
            XTermInterop.WriteToTerminal(_componentId, output);
            _sw.GetStringBuilder().Clear();
        };

        // Pass null for initialView to the context. This forces the context to treat the
        // canvas as empty/dirty and perform an initial render of the snapshot.
        var consoleLiveDisplayContext = new ConsoleLiveDisplayContext(_canvas, _consoleRenderer, terminalMonitor);

        // Pass the actual initialView to FocusManager so it knows about the initial focusable elements.
        var focusSession = focusManager.BeginSession(consoleLiveDisplayContext, initialView, CancellationToken.None);
        await focusSession.InitializationTask.ConfigureAwait(false);
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
            console.Write(snapshot.Renderable!);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error rendering component: {ex.Message} {ex.StackTrace}");

            throw;
        }

        return sw.ToString();
    }

    /// <summary>
    /// Processes a keyboard event from the browser.
    /// </summary>
    public async Task HandleKeyboardEventAsync(string xtermKey, string domKey, bool ctrlKey, bool altKey, bool shiftKey)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        if (_keyboardEventManager is null)
        {
            return;
        }

        var keyInfo = ParseKeyFromBrowser(xtermKey, domKey, ctrlKey, altKey, shiftKey);
        Console.WriteLine($"Parsed ConsoleKeyInfo: KeyChar='{keyInfo.KeyChar}', Key={keyInfo.Key}, Modifiers={keyInfo.Modifiers}");
        await _keyboardEventManager.HandleKeyAsync(keyInfo, CancellationToken.None).ConfigureAwait(false);
    }

    private static ConsoleKeyInfo ParseKeyFromBrowser(string xtermKey, string domKey, bool ctrlKey, bool altKey, bool shiftKey)
    {
        var modifiers = ConsoleModifiers.None;
        if (ctrlKey)
        {
            modifiers |= ConsoleModifiers.Control;
        }
        if (altKey)
        {
            modifiers |= ConsoleModifiers.Alt;
        }
        if (shiftKey)
        {
            modifiers |= ConsoleModifiers.Shift;
        }

        // Use domKey for key identification (cleaner names like "Enter", "Tab", etc.)
        var consoleKey = domKey switch
        {
            // Navigation keys
            "Enter" => ConsoleKey.Enter,
            "Tab" => ConsoleKey.Tab,
            "Backspace" => ConsoleKey.Backspace,
            "Escape" => ConsoleKey.Escape,
            "ArrowUp" => ConsoleKey.UpArrow,
            "ArrowDown" => ConsoleKey.DownArrow,
            "ArrowLeft" => ConsoleKey.LeftArrow,
            "ArrowRight" => ConsoleKey.RightArrow,

            // Additional navigation and editing keys
            "Home" => ConsoleKey.Home,
            "End" => ConsoleKey.End,
            "PageUp" => ConsoleKey.PageUp,
            "PageDown" => ConsoleKey.PageDown,
            "Insert" => ConsoleKey.Insert,
            "Delete" => ConsoleKey.Delete,

            // Function keys (F1-F12)
            "F1" => ConsoleKey.F1,
            "F2" => ConsoleKey.F2,
            "F3" => ConsoleKey.F3,
            "F4" => ConsoleKey.F4,
            "F5" => ConsoleKey.F5,
            "F6" => ConsoleKey.F6,
            "F7" => ConsoleKey.F7,
            "F8" => ConsoleKey.F8,
            "F9" => ConsoleKey.F9,
            "F10" => ConsoleKey.F10,
            "F11" => ConsoleKey.F11,
            "F12" => ConsoleKey.F12,

            // Modifier keys (standalone)
            "Shift" => ConsoleKey.None,
            "Control" => ConsoleKey.None,
            "Alt" => ConsoleKey.None,
            "Meta" => ConsoleKey.None,

            // Lock keys
            "CapsLock" => ConsoleKey.None,
            "NumLock" => ConsoleKey.None,
            "ScrollLock" => ConsoleKey.None,

            // Other special keys
            // "ContextMenu" maps to Applications key (right-click menu key on Windows keyboards)
            "ContextMenu" => ConsoleKey.Applications,
            "Pause" => ConsoleKey.Pause,
            "PrintScreen" => ConsoleKey.PrintScreen,

            // Clear key (Numpad 5 when NumLock is off)
            "Clear" => ConsoleKey.Clear,

            _ when domKey.Length == 1 => ParseSingleChar(domKey[0]),
            _ => ConsoleKey.None
        };

        // Use xtermKey for the actual character (better for printable characters)
        // For special keys, xtermKey contains escape sequences, so use domKey mapping
        var keyChar = domKey switch
        {
            "Enter" => '\r',
            "Tab" => '\t',
            "Backspace" => '\b',
            "Escape" => '\x1b',
            "Delete" => '\x7f',
            _ when xtermKey.Length == 1 => xtermKey[0],
            _ => '\0'
        };

        // Handle control key combinations for letters (Ctrl+A through Ctrl+Z)
        // When Ctrl is pressed with a letter, xterm sends the corresponding control character
        // (ASCII 1-26). We override the consoleKey and keyChar here because the switch above
        // cannot properly identify the letter from a control character.
        if (ctrlKey && !altKey && xtermKey.Length == 1)
        {
            var c = xtermKey[0];
            if (c >= '\x01' && c <= '\x1a')
            {
                // Control character: Ctrl+A = 0x01, ..., Ctrl+Z = 0x1A
                consoleKey = ConsoleKey.A + (c - '\x01');
                keyChar = c;
            }
        }

        return new ConsoleKeyInfo(keyChar, consoleKey, shiftKey, altKey, ctrlKey);
    }

    private static ConsoleKey ParseSingleChar(char c)
    {
        return c switch
        {
            // Letters
            >= 'a' and <= 'z' => ConsoleKey.A + (c - 'a'),
            >= 'A' and <= 'Z' => ConsoleKey.A + (c - 'A'),

            // Digit row keys
            >= '0' and <= '9' => ConsoleKey.D0 + (c - '0'),

            // Spacebar
            ' ' => ConsoleKey.Spacebar,

            // Punctuation and symbol keys (common US keyboard layout)
            '-' or '_' => ConsoleKey.OemMinus,
            '=' or '+' => ConsoleKey.OemPlus,
            '[' or '{' => ConsoleKey.Oem4,
            ']' or '}' => ConsoleKey.Oem6,
            '\\' or '|' => ConsoleKey.Oem5,
            ';' or ':' => ConsoleKey.Oem1,
            '\'' or '"' => ConsoleKey.Oem7,
            ',' or '<' => ConsoleKey.OemComma,
            '.' or '>' => ConsoleKey.OemPeriod,
            '/' or '?' => ConsoleKey.Oem2,
            '`' or '~' => ConsoleKey.Oem3,

            // Math operators (from numpad or Shift combinations)
            '*' => ConsoleKey.Multiply,

            _ => ConsoleKey.None
        };
    }

    /// <summary>
    /// Handles terminal resize events from the browser by updating console dimensions and triggering a re-render.
    /// </summary>
    public void HandleResize(int cols, int rows)
    {
        if (_ansiConsole is null || _canvas is null)
        {
            return;
        }

        // Update the console profile dimensions
        _ansiConsole.Profile.Width = cols;
        _ansiConsole.Profile.Height = rows;

        // Trigger a refresh to re-render with the new dimensions
        _canvas.Refresh();
    }
}
