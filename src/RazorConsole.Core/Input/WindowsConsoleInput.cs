// Copyright (c) RazorConsole. All rights reserved.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RazorConsole.Core.Input;

/// <summary>Read native mouse records without allowing Console.ReadKey to discard them.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsConsoleInput : IConsoleInput, IDisposable
{
    private readonly nint _input = GetStdHandle(-10);
    private readonly nint _output = GetStdHandle(-11);
    private readonly uint _originalMode;
    private readonly Queue<(ConsoleKeyInfo? Key, TerminalMouseEvent? Mouse)> _pending = [];
    private uint _buttons;
    private bool _disposed;

    public WindowsConsoleInput()
    {
        if (!GetConsoleMode(_input, out _originalMode))
        {
            throw new Win32Exception();
        }
        // Enable window/mouse events and disable QuickEdit, line and echo input.
        if (!SetConsoleMode(_input, (_originalMode | 0x0098u) & ~0x0246u))
        {
            throw new Win32Exception();
        }
    }

    public bool KeyAvailable
    {
        get { Pump(); return _pending.TryPeek(out var next) && next.Key.HasValue; }
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        Pump();
        if (!_pending.TryPeek(out var next) || next.Key is not { } key)
        {
            throw new InvalidOperationException("No keyboard event is available.");
        }
        _pending.Dequeue();
        return key;
    }

    public bool TryReadMouse(out TerminalMouseEvent value)
    {
        Pump();
        if (_pending.TryPeek(out var next) && next.Mouse is { } mouse)
        {
            _pending.Dequeue();
            value = mouse;
            return true;
        }
        value = default;
        return false;
    }

    private void Pump()
    {
        while (_pending.Count == 0 && GetNumberOfConsoleInputEvents(_input, out var available) && available > 0)
        {
            if (!ReadConsoleInputW(_input, out var record, 1, out var read) || read == 0)
            {
                throw new Win32Exception();
            }
            if (record.Type == 1 && record.KeyDown != 0)
            {
                var modifiers = Modifiers(record.KeyModifiers);
                var key = new ConsoleKeyInfo((char)record.Character, (ConsoleKey)record.VirtualKey,
                    modifiers.HasFlag(ConsoleModifiers.Shift), modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));
                for (var repeat = 0; repeat < Math.Max(1, (int)record.Repeat); repeat++)
                {
                    _pending.Enqueue((key, null));
                }
            }
            else if (record.Type == 2)
            {
                var buttons = record.Buttons & 0xffff;
                var changed = buttons ^ _buttons;
                var kind = record.MouseFlags == 4 ? TerminalMouseKind.Wheel
                    : record.MouseFlags == 1 ? TerminalMouseKind.Move
                    : (buttons & changed) != 0 || record.MouseFlags == 2 ? TerminalMouseKind.Down : TerminalMouseKind.Up;
                // No horizontal-wheel handler is advertised.
                if (record.MouseFlags == 8)
                {
                    continue;
                }
                var active = kind == TerminalMouseKind.Up ? _buttons & changed : buttons;
                var button = (active & 1) != 0 ? 0 : (active & 2) != 0 ? 2 : (active & 4) != 0 ? 1 : -1;
                _buttons = buttons;
                var x = record.X;
                var y = record.Y;
                if (GetConsoleScreenBufferInfo(_output, out var info))
                {
                    x -= info.WindowLeft;
                    y -= info.WindowTop;
                }
                var delta = kind == TerminalMouseKind.Wheel ? ((short)(record.Buttons >> 16) > 0 ? -3 : 3) : 0;
                _pending.Enqueue((null, new(kind, x, y, button, delta, Modifiers(record.MouseModifiers))));
            }
        }
    }

    private static ConsoleModifiers Modifiers(uint flags)
        => ((flags & 0x10) != 0 ? ConsoleModifiers.Shift : 0)
            | ((flags & 0x0c) != 0 ? ConsoleModifiers.Control : 0)
            | ((flags & 0x03) != 0 ? ConsoleModifiers.Alt : 0);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        SetConsoleMode(_input, _originalMode);
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(8)] public ushort Repeat;
        [FieldOffset(10)] public ushort VirtualKey;
        [FieldOffset(14)] public ushort Character;
        [FieldOffset(16)] public uint KeyModifiers;
        [FieldOffset(4)] public short X;
        [FieldOffset(6)] public short Y;
        [FieldOffset(8)] public uint Buttons;
        [FieldOffset(12)] public uint MouseModifiers;
        [FieldOffset(16)] public uint MouseFlags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 22)]
    private struct ScreenBufferInfo
    {
        [FieldOffset(10)] public short WindowLeft;
        [FieldOffset(12)] public short WindowTop;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GetStdHandle(int handle);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetConsoleMode(nint handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleMode(nint handle, uint mode);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNumberOfConsoleInputEvents(nint handle, out uint count);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadConsoleInputW(nint handle, out InputRecord record, uint length, out uint read);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetConsoleScreenBufferInfo(nint handle, out ScreenBufferInfo info);
}
