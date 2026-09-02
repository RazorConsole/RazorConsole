// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Renderables;
using RazorConsole.Core.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Core;

internal sealed class LiveDisplayCanvas(ConsoleLiveDisplayOptions options, IAnsiConsole ansiConsole) : ConsoleLiveDisplayContext.ILiveDisplayCanvas
{
    private DiffRenderable? _current;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

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
        var current = _current;
        if (current is null)
        {
            return;
        }

        // Spectre's live rendering is not thread safe. Refresh runs on the animation timer
        // and the resize handler, while UpdateTarget may be mutating this same renderable
        // on the render dispatcher — so share its gate. Dropping a repaint of unchanged
        // content is harmless; the next frame or tick paints it.
        if (!_semaphore.Wait(100))
        {
            return;
        }

        try
        {
            ansiConsole.Write(new ControlCode(string.Empty));
            ansiConsole.Write(current);
            Refreshed?.Invoke();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool TryReplaceNode(IReadOnlyList<int> path, IRenderable renderable)
        => false;

    public bool TryUpdateText(IReadOnlyList<int> path, string? text)
        => false;

    public bool TryUpdateAttributes(IReadOnlyList<int> path, IReadOnlyDictionary<string, string?> attributes)
        => false;
}
