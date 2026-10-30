// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Output;

/// <summary>
/// Process exit codes. Each error code maps to exactly one exit code, so scripts can branch without parsing JSON.
/// </summary>
internal static class CliExitCodes
{
    public const int Success = 0;
    public const int AssertionFailed = 1;
    public const int Usage = 2;
    public const int NotFound = 3;
    public const int AppLayerUnavailable = 4;
    public const int Timeout = 5;
    public const int ProcessExited = 6;
    public const int PlatformUnsupported = 7;
    public const int HostUnavailable = 8;
    public const int Internal = 10;

    public static int For(string errorCode) => errorCode switch
    {
        DiagnosticsErrorCodes.AssertionFailed or DiagnosticsErrorCodes.ChecksFailed => AssertionFailed,
        DiagnosticsErrorCodes.Usage or DiagnosticsErrorCodes.InvalidParams => Usage,
        DiagnosticsErrorCodes.SessionNotFound
            or DiagnosticsErrorCodes.ElementNotFound
            or DiagnosticsErrorCodes.AmbiguousSelector
            or DiagnosticsErrorCodes.ElementNotVisible
            or DiagnosticsErrorCodes.ElementNotFocusable => NotFound,
        DiagnosticsErrorCodes.AppLayerUnavailable
            or DiagnosticsErrorCodes.InputUnavailable
            or DiagnosticsErrorCodes.ScreenUnavailable
            or DiagnosticsErrorCodes.TerminalLayerUnavailable => AppLayerUnavailable,
        DiagnosticsErrorCodes.Timeout => Timeout,
        DiagnosticsErrorCodes.ProcessExited => ProcessExited,
        DiagnosticsErrorCodes.PtyUnsupported => PlatformUnsupported,
        DiagnosticsErrorCodes.HostUnavailable or DiagnosticsErrorCodes.HostVersionMismatch => HostUnavailable,
        _ => Internal,
    };
}
