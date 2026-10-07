// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.InteropServices;
using Spectre.Console;

namespace RazorConsole.Core.Rendering;

internal sealed class TerminalMonitor : ITerminalViewport, IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan CheckDebounce = TimeSpan.FromMilliseconds(100);
    private int _width;
    private int _height;
    private IDisposable? _posixRegistration;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _debounceCts;
    private bool _isStarted;
    private readonly Lock _sync = new();

    public event Action? OnResized;

    public int Width => _width;

    public int Height => _height;

    public TerminalMonitor()
    {
        if (!OperatingSystem.IsBrowser())
        {
            _width = AnsiConsole.Console.Profile.Width;
            _height = AnsiConsole.Console.Profile.Height;
        }
        else
        {
            _width = 0;
            _height = 0;
        }
    }

    internal TerminalMonitor(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Terminal width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Terminal height must be positive.");
        }

        _width = width;
        _height = height;
    }

    internal void Resize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Terminal width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Terminal height must be positive.");
        }

        if (_width == width && _height == height)
        {
            return;
        }

        _width = width;
        _height = height;
        OnResized?.Invoke();
    }

    public void Start(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_isStarted)
            {
                return;
            }
        }

        if (OperatingSystem.IsBrowser())
        {
            _isStarted = true;
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _posixRegistration = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ =>
            {
                RequestDebouncedResize();
            });
            _isStarted = true;
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = PollResizeAsync(_cts.Token);
        _isStarted = true;
    }

    private void RequestDebouncedResize()
    {
        lock (_sync)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();

            var token = _debounceCts.Token;

            Task.Delay(CheckDebounce, token).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    UpdateCurrentSize();
                    OnResized?.Invoke();
                }
            }, token);
        }
    }

    private async Task PollResizeAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (AnsiConsole.Console.Profile.Width != _width || AnsiConsole.Console.Profile.Height != _height)
                {
                    UpdateCurrentSize();
                    OnResized?.Invoke();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateCurrentSize()
    {
        if (OperatingSystem.IsBrowser())
        {
            _width = 0;
            _height = 0;
            return;
        }

        _width = AnsiConsole.Console.Profile.Width;
        _height = AnsiConsole.Console.Profile.Height;
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        CancellationTokenSource? debounceCts;
        IDisposable? registration;
        lock (_sync)
        {
            cts = _cts;
            _cts = null;
            debounceCts = _debounceCts;
            _debounceCts = null;
            registration = _posixRegistration;
            _posixRegistration = null;
        }

        registration?.Dispose();
        DisposeSource(cts);
        DisposeSource(debounceCts);
    }

    private static void DisposeSource(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        source.Dispose();
    }
}
