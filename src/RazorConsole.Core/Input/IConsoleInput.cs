// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

/// <summary>
/// Represents the type of console input event.
/// </summary>
internal enum ConsoleInputEventType
{
    /// <summary>
    /// A keyboard key press event.
    /// </summary>
    Key,

    /// <summary>
    /// A mouse button or movement event.
    /// </summary>
    Mouse,
}

/// <summary>
/// Represents the type of mouse action.
/// </summary>
internal enum MouseAction
{
    /// <summary>
    /// A mouse button was pressed.
    /// </summary>
    ButtonPressed,

    /// <summary>
    /// A mouse button was released.
    /// </summary>
    ButtonReleased,

    /// <summary>
    /// The mouse was moved.
    /// </summary>
    Moved,

    /// <summary>
    /// The mouse wheel was scrolled.
    /// </summary>
    WheelScrolled,
}

/// <summary>
/// Represents the mouse button involved in an event.
/// </summary>
internal enum MouseButton
{
    /// <summary>
    /// No button (used for movement events).
    /// </summary>
    None = 0,

    /// <summary>
    /// The left mouse button.
    /// </summary>
    Left = 1,

    /// <summary>
    /// The middle mouse button (wheel click).
    /// </summary>
    Middle = 2,

    /// <summary>
    /// The right mouse button.
    /// </summary>
    Right = 3,

    /// <summary>
    /// Scroll wheel up.
    /// </summary>
    WheelUp = 4,

    /// <summary>
    /// Scroll wheel down.
    /// </summary>
    WheelDown = 5,
}

/// <summary>
/// Contains information about a mouse input event.
/// </summary>
/// <param name="Action">The type of mouse action.</param>
/// <param name="Button">The mouse button involved.</param>
/// <param name="X">The X coordinate (column) of the mouse cursor.</param>
/// <param name="Y">The Y coordinate (row) of the mouse cursor.</param>
/// <param name="Modifiers">Any modifier keys held during the event.</param>
internal readonly record struct ConsoleMouseEventInfo(
    MouseAction Action,
    MouseButton Button,
    int X,
    int Y,
    ConsoleModifiers Modifiers);

/// <summary>
/// Represents a unified console input event that can be either a keyboard or mouse event.
/// </summary>
internal readonly struct ConsoleInputEvent
{
    /// <summary>
    /// Gets the type of this input event.
    /// </summary>
    public ConsoleInputEventType EventType { get; }

    /// <summary>
    /// Gets the keyboard event info. Only valid when <see cref="EventType"/> is <see cref="ConsoleInputEventType.Key"/>.
    /// </summary>
    public ConsoleKeyInfo KeyInfo { get; }

    /// <summary>
    /// Gets the mouse event info. Only valid when <see cref="EventType"/> is <see cref="ConsoleInputEventType.Mouse"/>.
    /// </summary>
    public ConsoleMouseEventInfo MouseInfo { get; }

    private ConsoleInputEvent(ConsoleKeyInfo keyInfo)
    {
        EventType = ConsoleInputEventType.Key;
        KeyInfo = keyInfo;
        MouseInfo = default;
    }

    private ConsoleInputEvent(ConsoleMouseEventInfo mouseInfo)
    {
        EventType = ConsoleInputEventType.Mouse;
        KeyInfo = default;
        MouseInfo = mouseInfo;
    }

    /// <summary>
    /// Creates a keyboard input event.
    /// </summary>
    public static ConsoleInputEvent FromKey(ConsoleKeyInfo keyInfo) => new(keyInfo);

    /// <summary>
    /// Creates a mouse input event.
    /// </summary>
    public static ConsoleInputEvent FromMouse(ConsoleMouseEventInfo mouseInfo) => new(mouseInfo);
}

/// <summary>
/// Provides data for console input events.
/// </summary>
internal sealed class ConsoleInputEventArgs : EventArgs
{
    /// <summary>
    /// Gets the input event data.
    /// </summary>
    public ConsoleInputEvent InputEvent { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the event has been handled.
    /// When set to <c>true</c>, the event will not be processed further by default handlers.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleInputEventArgs"/> class.
    /// </summary>
    /// <param name="inputEvent">The input event data.</param>
    public ConsoleInputEventArgs(ConsoleInputEvent inputEvent)
    {
        InputEvent = inputEvent;
    }
}

/// <summary>
/// Represents the method that will handle console input events.
/// </summary>
/// <param name="sender">The source of the event.</param>
/// <param name="e">The event data.</param>
internal delegate void ConsoleInputEventHandler(object? sender, ConsoleInputEventArgs e);

/// <summary>
/// Abstraction for console input, supporting keyboard and mouse events.
/// </summary>
internal interface IConsoleInput
{
    /// <summary>
    /// Occurs when an input event is received.
    /// </summary>
    event ConsoleInputEventHandler? InputReceived;

    /// <summary>
    /// Gets a value indicating whether a key press is available in the input stream.
    /// </summary>
    bool KeyAvailable { get; }

    /// <summary>
    /// Gets a value indicating whether an input event (key or mouse) is available.
    /// </summary>
    bool InputAvailable { get; }

    /// <summary>
    /// Gets a value indicating whether mouse input is supported and enabled.
    /// </summary>
    bool MouseEnabled { get; }

    /// <summary>
    /// Reads the next key from the input stream.
    /// </summary>
    /// <param name="intercept">Whether to intercept the key (not display it).</param>
    /// <returns>The key information.</returns>
    ConsoleKeyInfo ReadKey(bool intercept);

    /// <summary>
    /// Reads the next input event (keyboard or mouse) from the input stream.
    /// </summary>
    /// <param name="intercept">Whether to intercept keyboard input (not display it).</param>
    /// <returns>The input event.</returns>
    ConsoleInputEvent ReadInput(bool intercept);

    /// <summary>
    /// Enables mouse input tracking in the terminal.
    /// </summary>
    /// <returns><c>true</c> if mouse tracking was successfully enabled; otherwise, <c>false</c>.</returns>
    bool EnableMouse();

    /// <summary>
    /// Disables mouse input tracking in the terminal.
    /// </summary>
    void DisableMouse();
}

/// <summary>
/// Default implementation of <see cref="IConsoleInput"/> that wraps <see cref="Console"/>.
/// </summary>
internal sealed class ConsoleInput : IConsoleInput
{
    // SGR mouse tracking escape sequences
    private const string EnableMouseSequence = "\x1b[?1000h\x1b[?1006h"; // Enable button tracking + SGR extended mode
    private const string DisableMouseSequence = "\x1b[?1006l\x1b[?1000l";

    private volatile bool _mouseEnabled;

    /// <inheritdoc />
    public event ConsoleInputEventHandler? InputReceived;

    /// <inheritdoc />
    public bool KeyAvailable => Console.KeyAvailable;

    /// <inheritdoc />
    public bool InputAvailable => Console.KeyAvailable;

    /// <inheritdoc />
    public bool MouseEnabled => _mouseEnabled;

    /// <inheritdoc />
    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);

    /// <inheritdoc />
    public ConsoleInputEvent ReadInput(bool intercept)
    {
        var keyInfo = Console.ReadKey(intercept);

        ConsoleInputEvent inputEvent;

        // Check if this is the start of an escape sequence (potential mouse event)
        if (_mouseEnabled && keyInfo.Key == ConsoleKey.Escape && Console.KeyAvailable)
        {
            var mouseEvent = TryParseMouseEvent(keyInfo);
            if (mouseEvent.HasValue)
            {
                inputEvent = ConsoleInputEvent.FromMouse(mouseEvent.Value);
            }
            else
            {
                inputEvent = ConsoleInputEvent.FromKey(keyInfo);
            }
        }
        else
        {
            inputEvent = ConsoleInputEvent.FromKey(keyInfo);
        }

        // Raise the event
        OnInputReceived(inputEvent);

        return inputEvent;
    }

    /// <summary>
    /// Raises the <see cref="InputReceived"/> event.
    /// </summary>
    /// <param name="inputEvent">The input event to raise.</param>
    /// <returns>The event args, which includes whether the event was handled.</returns>
    private ConsoleInputEventArgs OnInputReceived(ConsoleInputEvent inputEvent)
    {
        var args = new ConsoleInputEventArgs(inputEvent);
        InputReceived?.Invoke(this, args);
        return args;
    }

    /// <inheritdoc />
    public bool EnableMouse()
    {
        if (OperatingSystem.IsBrowser())
        {
            return false;
        }

        try
        {
            Console.Write(EnableMouseSequence);
            _mouseEnabled = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void DisableMouse()
    {
        if (_mouseEnabled)
        {
            try
            {
                Console.Write(DisableMouseSequence);
            }
            catch
            {
                // Ignore errors during cleanup
            }

            _mouseEnabled = false;
        }
    }

    /// <summary>
    /// Attempts to parse a mouse event from the escape sequence.
    /// SGR format: ESC [ &lt; Cb ; Cx ; Cy M (press) or m (release)
    /// </summary>
    private static ConsoleMouseEventInfo? TryParseMouseEvent(ConsoleKeyInfo escapeKey)
    {
        // We've already read ESC, now we need to read the rest of the sequence
        // Expected: [ < Cb ; Cx ; Cy M/m
        if (!Console.KeyAvailable)
        {
            return null;
        }

        var bracket = Console.ReadKey(true);
        if (bracket.KeyChar != '[')
        {
            return null;
        }

        if (!Console.KeyAvailable)
        {
            return null;
        }

        var lessThan = Console.ReadKey(true);
        if (lessThan.KeyChar != '<')
        {
            return null;
        }

        // Read until we get M or m (the terminator)
        var buffer = new System.Text.StringBuilder();
        while (Console.KeyAvailable)
        {
            var ch = Console.ReadKey(true);
            if (ch.KeyChar == 'M' || ch.KeyChar == 'm')
            {
                return ParseSgrMouseSequence(buffer.ToString(), ch.KeyChar == 'M');
            }

            buffer.Append(ch.KeyChar);

            // Safety limit to prevent infinite loops on malformed input
            if (buffer.Length > 20)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses the SGR mouse sequence parameters: Cb;Cx;Cy
    /// </summary>
    private static ConsoleMouseEventInfo? ParseSgrMouseSequence(string parameters, bool isPress)
    {
        var parts = parameters.Split(';');
        if (parts.Length != 3)
        {
            return null;
        }

        if (!int.TryParse(parts[0], out var cb) ||
            !int.TryParse(parts[1], out var cx) ||
            !int.TryParse(parts[2], out var cy))
        {
            return null;
        }

        // Decode the button code (Cb)
        // Bits 0-1: button (0=left, 1=middle, 2=right, 3=release)
        // Bit 2: shift
        // Bit 3: meta/alt
        // Bit 4: control
        // Bits 5-6: 64=wheel up, 65=wheel down, 32=motion

        var modifiers = ConsoleModifiers.None;
        if ((cb & 4) != 0)
        {
            modifiers |= ConsoleModifiers.Shift;
        }

        if ((cb & 8) != 0)
        {
            modifiers |= ConsoleModifiers.Alt;
        }

        if ((cb & 16) != 0)
        {
            modifiers |= ConsoleModifiers.Control;
        }

        var buttonCode = cb & 3;
        var isMotion = (cb & 32) != 0;
        var isWheel = (cb & 64) != 0;

        MouseAction action;
        MouseButton button;

        if (isWheel)
        {
            action = MouseAction.WheelScrolled;
            button = buttonCode == 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
        }
        else if (isMotion)
        {
            action = MouseAction.Moved;
            button = buttonCode switch
            {
                0 => MouseButton.Left,
                1 => MouseButton.Middle,
                2 => MouseButton.Right,
                _ => MouseButton.None,
            };
        }
        else
        {
            action = isPress ? MouseAction.ButtonPressed : MouseAction.ButtonReleased;
            button = buttonCode switch
            {
                0 => MouseButton.Left,
                1 => MouseButton.Middle,
                2 => MouseButton.Right,
                _ => MouseButton.None,
            };
        }

        // Terminal coordinates are 1-based, convert to 0-based
        return new ConsoleMouseEventInfo(action, button, cx - 1, cy - 1, modifiers);
    }
}
