// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering;

namespace RazorConsole.Website;

/// <summary>
/// The terminal backend of a component hosted in the page: frames are rendered as ANSI and written to the xterm.js
/// terminal of the component.
/// </summary>
internal sealed class XTermTerminal : ITerminal
{
    private readonly AnsiTextTerminal _ansi;

    /// <param name="options">The live display options of the hosted app.</param>
    /// <param name="componentId">The id of the component's xterm.js terminal in the page.</param>
    /// <param name="cols">The terminal's width in columns.</param>
    /// <param name="rows">The terminal's height in rows.</param>
    /// <param name="written">Raised with the text of every frame, right before it is written to the terminal.</param>
    /// <param name="isClosed">Returns whether the component was closed; nothing is written after that.</param>
    public XTermTerminal(
        ConsoleLiveDisplayOptions options,
        string componentId,
        int cols,
        int rows,
        Action<string>? written,
        Func<bool> isClosed)
    {
        ArgumentNullException.ThrowIfNull(componentId);
        ArgumentNullException.ThrowIfNull(isClosed);
        _ansi = new AnsiTextTerminal(options, cols, rows, text =>
        {
            if (isClosed())
            {
                return;
            }

            written?.Invoke(text);
            XTermInterop.WriteToTerminal(componentId, text);
        });
    }

    public bool KeepsLastFrameAfterDispose => _ansi.KeepsLastFrameAfterDispose;

    public ValueTask PresentAsync(TerminalFrame frame, CancellationToken cancellationToken = default)
        => _ansi.PresentAsync(frame, cancellationToken);

    /// <summary>Sets the size of the xterm.js terminal, after the page resized it.</summary>
    public void Resize(int cols, int rows) => _ansi.Resize(cols, rows);

    public ValueTask DisposeAsync() => _ansi.DisposeAsync();
}
