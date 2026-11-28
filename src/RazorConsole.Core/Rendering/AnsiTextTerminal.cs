// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using Spectre.Console;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// A terminal backend for hosts that are not a console, such as a terminal emulator in a web page: it renders each
/// frame as ANSI text the way <see cref="AnsiTerminal"/> writes it to a console, and hands that text to a callback.
/// </summary>
/// <remarks>
/// The callback gets the text of one frame at a time, on the presenter's frame loop. When it throws, the text is kept
/// and sent again before the next frame's text, so the host never misses part of the screen.
/// </remarks>
internal sealed class AnsiTextTerminal : ITerminal
{
    private readonly StringBuilder _buffer = new();
    private readonly IAnsiConsole _console;
    private readonly AnsiTerminal _ansi;
    private readonly Action<string> _write;

    public AnsiTextTerminal(ConsoleLiveDisplayOptions options, int width, int height, Action<string> write)
    {
        ArgumentNullException.ThrowIfNull(options);
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(new StringWriter(_buffer)),
        });
        _console.Profile.Capabilities.Unicode = true;
        Resize(width, height);
        _ansi = new AnsiTerminal(options, _console);
    }

    public bool KeepsLastFrameAfterDispose => _ansi.KeepsLastFrameAfterDispose;

    public async ValueTask PresentAsync(TerminalFrame frame, CancellationToken cancellationToken = default)
    {
        await _ansi.PresentAsync(frame, cancellationToken).ConfigureAwait(false);
        Flush();
    }

    /// <summary>
    /// Sets the size the host's terminal has, which the ANSI output is laid out for. A different size redraws the next
    /// frame in full.
    /// </summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        _console.Profile.Width = width;
        _console.Profile.Height = height;
    }

    /// <summary>
    /// Leaves the alternate screen buffer in the ANSI output. That text is not handed to the callback: the host's
    /// terminal goes away with the app, and the same terminal can be given to a new app right away.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _ansi.DisposeAsync().ConfigureAwait(false);
        _buffer.Clear();
    }

    private void Flush()
    {
        if (_buffer.Length == 0)
        {
            return;
        }

        // Cleared only after the callback returned, so text it failed to take goes out with the next frame.
        _write(_buffer.ToString());
        _buffer.Clear();
    }
}
