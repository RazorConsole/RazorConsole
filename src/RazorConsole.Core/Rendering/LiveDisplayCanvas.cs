// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Core;

internal sealed class LiveDisplayCanvas(ConsoleLiveDisplayOptions options, IAnsiConsole ansiConsole) : ConsoleLiveDisplayContext.ILiveDisplayCanvas, IDisposable
{
    private DiffRenderable? _current;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly bool _useAlternateScreenBuffer = options.UseAlternateScreenBuffer || options.EnableMouseEvents;
    private bool _alternateScreenBufferActive;
    private bool _disposed;

    public event Action? Refreshed;

    public void UpdateTarget(IRenderable? renderable)
    {
        if (_current is null && renderable is null)
        {
            return;
        }

        if (_current is not null && renderable is not null && ReferenceEquals(_current, renderable))
        {
            return;
        }

        if (!_semaphore.Wait(100))
        {
            return;
        }
        try
        {
            EnterAlternateScreenBufferIfNeeded();

            if (_current is null && renderable is not null)
            {
                _current = new DiffRenderable(renderable, hideCursor: options.HideCursor);
                ansiConsole.Write(_current);
                Refreshed?.Invoke();
            }
            else if (_current is not null && renderable is not null)
            {
                _current.UpdateRenderable(renderable);
                ansiConsole.Write(_current);
                Refreshed?.Invoke();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }


    public void Refresh()
    {
        if (_current is not null)
        {
            EnterAlternateScreenBufferIfNeeded();
            ansiConsole.Write(new ControlCode(string.Empty));
            ansiConsole.Write(_current);
            Refreshed?.Invoke();
        }
    }

    public bool TryReplaceNode(IReadOnlyList<int> path, IRenderable renderable)
        => false;

    public bool TryUpdateText(IReadOnlyList<int> path, string? text)
        => false;

    public bool TryUpdateAttributes(IReadOnlyList<int> path, IReadOnlyDictionary<string, string?> attributes)
        => false;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_alternateScreenBufferActive)
        {
            ansiConsole.Write(new ControlCode(RM(DECALTSCR)));
            _alternateScreenBufferActive = false;
        }

        _disposed = true;
    }

    private void EnterAlternateScreenBufferIfNeeded()
    {
        if (!_useAlternateScreenBuffer || _alternateScreenBufferActive)
        {
            return;
        }

        ansiConsole.Write(new ControlCode(SM(DECALTSCR)));
        _alternateScreenBufferActive = true;
    }
}
