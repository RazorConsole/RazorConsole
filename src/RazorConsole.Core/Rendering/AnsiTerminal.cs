// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Renderables;
using Spectre.Console;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// A terminal backend that writes frames as ANSI to a Spectre.Console <see cref="IAnsiConsole"/>.
/// </summary>
/// <remarks>
/// It enters the alternate screen buffer (and mouse reporting) with the first frame and leaves it when disposed. Frames
/// are written through <see cref="DiffRenderable"/>, which writes only the lines that changed since the last frame.
/// </remarks>
internal sealed class AnsiTerminal : ITerminal
{
    private readonly ConsoleLiveDisplayOptions _options;
    private readonly IAnsiConsole _ansiConsole;
    private readonly bool _useAlternateScreenBuffer;
    private DiffRenderable? _current;
    private bool _alternateScreenBufferActive;

    public AnsiTerminal(ConsoleLiveDisplayOptions options, IAnsiConsole ansiConsole)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ansiConsole = ansiConsole ?? throw new ArgumentNullException(nameof(ansiConsole));
        _useAlternateScreenBuffer = options.UseAlternateScreenBuffer || options.EnableMouseEvents;
    }

    /// <summary>
    /// Without the alternate screen buffer the last frame stays on the terminal after the app exits; with it, leaving
    /// the buffer restores the screen.
    /// </summary>
    public bool KeepsLastFrameAfterDispose => !_useAlternateScreenBuffer;

    public ValueTask PresentAsync(TerminalFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var renderable = frame.Canvas.ToRenderable();
        if (_current is null)
        {
            _current = new DiffRenderable(renderable, hideCursor: _options.HideCursor);
        }
        else
        {
            _current.UpdateRenderable(renderable);
        }

        EnterAlternateScreenBufferIfNeeded();
        WriteCurrent();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Leaves the alternate screen buffer and turns mouse reporting off, if the first frame turned them on.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        LeaveAlternateScreenBuffer();
        return ValueTask.CompletedTask;
    }

    private void WriteCurrent()
    {
        try
        {
            try
            {
                if (_options.EnableSynchronizedUpdates)
                {
                    _ansiConsole.Write(new ControlCode(SM(SynchronizedOutput)));
                }

                _ansiConsole.Write(_current!);
            }
            finally
            {
                if (_options.EnableSynchronizedUpdates)
                {
                    _ansiConsole.Write(new ControlCode(RM(SynchronizedOutput)));
                }
            }
        }
        catch
        {
            // Part of the frame may have reached the terminal: redraw everything with the next frame.
            _current?.Invalidate();
            throw;
        }
    }

    private void EnterAlternateScreenBufferIfNeeded()
    {
        if (!_useAlternateScreenBuffer || _alternateScreenBufferActive)
        {
            return;
        }

        _ansiConsole.Write(new ControlCode(SM(DECALTSCR)));
        if (_options.EnableMouseEvents)
        {
            _ansiConsole.Write(new ControlCode("\u001b[?1003h\u001b[?1006h"));
        }
        _alternateScreenBufferActive = true;
    }

    private void LeaveAlternateScreenBuffer()
    {
        if (!_alternateScreenBufferActive)
        {
            return;
        }

        if (_options.EnableMouseEvents)
        {
            _ansiConsole.Write(new ControlCode("\u001b[?1003l\u001b[?1006l"));
        }
        _ansiConsole.Write(new ControlCode(RM(DECALTSCR)));
        _alternateScreenBufferActive = false;
    }
}
