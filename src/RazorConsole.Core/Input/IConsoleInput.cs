// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

internal interface IConsoleInput
{
    bool KeyAvailable { get; }

    ConsoleKeyInfo ReadKey(bool intercept);
}

internal sealed class ConsoleInput : IConsoleInput
{
    public bool KeyAvailable => Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
}
