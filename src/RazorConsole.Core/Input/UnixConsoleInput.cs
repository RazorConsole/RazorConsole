// Copyright (c) RazorConsole. All rights reserved.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RazorConsole.Core.Input;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class UnixConsoleInput : IConsoleInput, IDisposable
{
    // termios is opaque here; cfmakeraw uses the platform's actual layout.
    private readonly byte[] _original = new byte[256];
    private readonly byte[] _bytes = new byte[4096];
    private readonly char[] _chars = new char[4096];
    private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();
    private readonly TerminalInputDecoder _decoder = new();
    private bool _disposed;

    public UnixConsoleInput()
    {
        // Initialize the runtime's terminal bookkeeping before taking ownership.
        _ = Console.TreatControlCAsInput;
        if (tcgetattr(0, _original) != 0)
        {
            throw new Win32Exception();
        }
        var mode = (byte[])_original.Clone();
        cfmakeraw(mode);
        var flagSize = OperatingSystem.IsMacOS() ? 8 : 4;
        // Preserve output processing and signal handling (Ctrl+C/SIGINT).
        Array.Copy(_original, flagSize, mode, flagSize, flagSize);
        if (OperatingSystem.IsMacOS())
        {
            var localFlags = BitConverter.ToUInt64(mode, 24) | (BitConverter.ToUInt64(_original, 24) & 128UL);
            BitConverter.GetBytes(localFlags).CopyTo(mode, 24);
        }
        else
        {
            var localFlags = BitConverter.ToUInt32(mode, 12) | (BitConverter.ToUInt32(_original, 12) & 1U);
            BitConverter.GetBytes(localFlags).CopyTo(mode, 12);
        }
        if (tcsetattr(0, 0, mode) != 0)
        {
            throw new Win32Exception();
        }
        AppDomain.CurrentDomain.ProcessExit += OnExit;
    }
    public bool KeyAvailable { get { Pump(); return _decoder.KeyAvailable; } }
    public ConsoleKeyInfo ReadKey(bool intercept) => _decoder.ReadKey();
    public bool TryReadMouse(out TerminalMouseEvent value) { Pump(); return _decoder.TryReadMouse(out value); }
    private void Pump()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var descriptor = new PollDescriptor { Descriptor = 0, Events = 1 };
        if (poll(ref descriptor, 1, 0) > 0 && (descriptor.ReturnedEvents & 1) != 0)
        {
            var count = (int)read(0, _bytes, (nuint)_bytes.Length);
            if (count > 0)
            {
                var characters = _utf8.GetChars(_bytes, 0, count, _chars, 0);
                _decoder.Feed(_chars.AsSpan(0, characters), Environment.TickCount64);
            }
        }
        _decoder.FlushEscape(Environment.TickCount64);
    }
    private void OnExit(object? sender, EventArgs args) => Dispose();
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        tcsetattr(0, 0, _original);
        AppDomain.CurrentDomain.ProcessExit -= OnExit;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PollDescriptor { public int Descriptor; public short Events; public short ReturnedEvents; }
    [DllImport("libc", SetLastError = true)] private static extern int tcgetattr(int fd, [Out] byte[] state);
    [DllImport("libc", SetLastError = true)] private static extern int tcsetattr(int fd, int action, byte[] state);
    [DllImport("libc")] private static extern void cfmakeraw([In, Out] byte[] state);
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref PollDescriptor descriptor, nuint count, int timeout);
    [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, [Out] byte[] buffer, nuint count);
}
