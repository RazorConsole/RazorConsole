// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

internal interface IConsoleInput
{
    bool KeyAvailable { get; }
    bool DecodesTerminalSequences => false;

    ConsoleKeyInfo ReadKey(bool intercept);
    bool TryReadMouse(out TerminalMouseEvent value) { value = default; return false; }
}

internal sealed class ConsoleInput : IConsoleInput, IDisposable
{
    private readonly IConsoleInput? _native;
    public ConsoleInput(ConsoleAppOptions options)
    {
        if (OperatingSystem.IsWindows() && options.ConsoleLiveDisplayOptions.EnableMouseEvents && !Console.IsInputRedirected)
        {
            _native = new WindowsConsoleInput();
        }
        else if ((OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) && options.ConsoleLiveDisplayOptions.EnableMouseEvents && !Console.IsInputRedirected)
        {
            _native = new UnixConsoleInput();
        }
    }
    public bool KeyAvailable => _native?.KeyAvailable ?? Console.KeyAvailable;
    public bool DecodesTerminalSequences => _native is not null;

    public ConsoleKeyInfo ReadKey(bool intercept) => _native?.ReadKey(intercept) ?? Console.ReadKey(intercept);
    public bool TryReadMouse(out TerminalMouseEvent value)
    {
        if (_native is not null)
        {
            return _native.TryReadMouse(out value);
        }
        value = default;
        return false;
    }
    public void Dispose() => (_native as IDisposable)?.Dispose();
}
