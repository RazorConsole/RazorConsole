// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Input;

/// <summary>
/// Where a terminal's input comes from: it reads the terminal and posts decoded events to the <see cref="InputRouter"/>.
/// </summary>
internal interface ITerminalInput
{
    /// <summary>
    /// Reads input and posts every event to <paramref name="post"/>, in order, until <paramref name="cancellationToken"/>
    /// is cancelled or the input ends (the terminal went away). Posting never blocks.
    /// </summary>
    Task RunAsync(Action<TerminalInputEvent> post, CancellationToken cancellationToken);
}
