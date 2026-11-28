// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Rendering;

/// <summary>
/// A terminal backend: where painted frames go.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="FramePresenter"/> calls a terminal, one call at a time from its frame loop, so an implementation
/// needs no locking of its own. The presenter disposes the terminal after its frame loop stopped, so
/// <see cref="IAsyncDisposable.DisposeAsync"/> never overlaps <see cref="PresentAsync"/>.
/// </para>
/// <para>
/// Input, size and terminal actions move here in later steps of issue #425.
/// </para>
/// </remarks>
internal interface ITerminal : IAsyncDisposable
{
    /// <summary>
    /// Whether the last presented frame stays visible after the terminal is disposed. When it does not (an alternate
    /// screen buffer is restored, for example), the presenter skips a frame that was requested while it was being
    /// disposed.
    /// </summary>
    bool KeepsLastFrameAfterDispose { get; }

    /// <summary>
    /// Shows <paramref name="frame"/>. The frame is complete and never changes, so the terminal may keep it to diff the
    /// next one against. The returned task completes once the frame was handed to the output.
    /// </summary>
    /// <remarks>
    /// A terminal that fails to present a frame throws, and must show the next frame in full, because it cannot tell
    /// how much of the failed frame reached the output.
    /// </remarks>
    ValueTask PresentAsync(TerminalFrame frame, CancellationToken cancellationToken = default);
}
