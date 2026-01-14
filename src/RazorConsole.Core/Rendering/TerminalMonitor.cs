// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.InteropServices;
using Spectre.Console;

namespace RazorConsole.Core.Rendering;

internal sealed class TerminalMonitor : IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan CheckDebounce = TimeSpan.FromMilliseconds(100);
    private int _width;
    private int _height;
    private IDisposable? _posixRegistration;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _debounceCts;
    private bool _isStarted;
#if NET9_0_OR_GREATER
    private readonly Lock _sync = new();
#else
    private readonly object _sync = new();
#endif

    public event Action? OnResized;

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
                    OnResized?.Invoke();
                }
            }, token);
        }
    }

    private async Task PollResizeAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(token))
        {
            if (AnsiConsole.Console.Profile.Width != _width || AnsiConsole.Console.Profile.Height != _height)
            {
                _width = AnsiConsole.Console.Profile.Width;
                _height = AnsiConsole.Console.Profile.Height;
                OnResized?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        _posixRegistration?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
