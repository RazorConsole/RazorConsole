// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Records the frames the display presents in <see cref="DiagnosticsState"/>, so diagnostics read the display's cells
/// instead of painting every frame a second time.
/// </summary>
/// <remarks>It does nothing when the options opt out of diagnostics (<paramref name="state"/> is <see langword="null"/> then).</remarks>
internal sealed class DiagnosticsFrameObserver(DiagnosticsState? state) : ITerminalFrameObserver
{
    public void OnFramePresented(TerminalFrame frame) => state?.RecordPresentedFrame(frame);
}
