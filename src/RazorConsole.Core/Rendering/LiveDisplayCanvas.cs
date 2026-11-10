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
    private int _refreshRequested;
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
                WriteCurrent();
                Refreshed?.Invoke();
            }
            else if (_current is not null && renderable is not null)
            {
                _current.UpdateRenderable(renderable);
                WriteCurrent();
                Refreshed?.Invoke();
            }
        }
        finally
        {
            _semaphore.Release();
        }

        WriteRequestedRefreshes();
    }

    /// <summary>
    /// Writes the current target again without blocking the caller.
    /// </summary>
    /// <remarks>
    /// When another write holds the canvas, the request is recorded and the thread holding it writes
    /// one more frame after it finishes, so overlapping refreshes collapse into one write.
    /// </remarks>
    public void Refresh()
    {
        Volatile.Write(ref _refreshRequested, 1);
        WriteRequestedRefreshes();
    }

    private void WriteRequestedRefreshes()
    {
        while (Volatile.Read(ref _refreshRequested) == 1 && _semaphore.Wait(0))
        {
            try
            {
                if (Interlocked.Exchange(ref _refreshRequested, 0) == 1 && _current is not null && !_disposed)
                {
                    EnterAlternateScreenBufferIfNeeded();
                    WriteCurrent();
                    Refreshed?.Invoke();
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    private void WriteCurrent()
    {
        try
        {
            try
            {
                if (options.EnableSynchronizedUpdates)
                {
                    ansiConsole.Write(new ControlCode(SM(SynchronizedOutput)));
                }

                ansiConsole.Write(_current!);
            }
            finally
            {
                if (options.EnableSynchronizedUpdates)
                {
                    ansiConsole.Write(new ControlCode(RM(SynchronizedOutput)));
                }
            }
        }
        catch
        {
            _current?.Invalidate();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_alternateScreenBufferActive)
        {
            if (options.EnableMouseEvents)
            {
                ansiConsole.Write(new ControlCode("\u001b[?1003l\u001b[?1006l"));
            }
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
        if (options.EnableMouseEvents)
        {
            ansiConsole.Write(new ControlCode("\u001b[?1003h\u001b[?1006h"));
        }
        _alternateScreenBufferActive = true;
    }
}
