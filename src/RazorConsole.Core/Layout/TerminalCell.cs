// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;

namespace RazorConsole.Core.Layout;

public readonly record struct TerminalCell(string Text, Style? Style)
{
    public static TerminalCell Empty { get; } = new(" ", null);
}
