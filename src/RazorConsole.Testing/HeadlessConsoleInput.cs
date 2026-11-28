// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;

namespace RazorConsole.Testing;

/// <summary>
/// Console input that never has a key, registered so nothing in a headless terminal can read the real console (which
/// would also switch it to raw mode). Headless input reaches the app through the <see cref="InputRouter"/>.
/// </summary>
internal sealed class HeadlessConsoleInput : IConsoleInput
{
    public bool KeyAvailable => false;

    public ConsoleKeyInfo ReadKey(bool intercept)
        => throw new InvalidOperationException("A headless terminal has no console input.");
}
