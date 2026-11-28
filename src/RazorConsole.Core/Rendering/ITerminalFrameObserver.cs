// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Rendering;

/// <summary>
/// Reads the frames a <see cref="FramePresenter"/> presents, without being a terminal itself: diagnostics, recording
/// and tooling.
/// </summary>
/// <remarks>
/// Called on the frame loop after the terminal presented a frame, so it must be quick and must not block. The frame
/// never changes, so an observer may keep it. A throwing observer is logged and does not stop the frame loop or the
/// other observers.
/// </remarks>
internal interface ITerminalFrameObserver
{
    void OnFramePresented(TerminalFrame frame);
}
