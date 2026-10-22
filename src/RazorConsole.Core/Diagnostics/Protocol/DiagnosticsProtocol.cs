// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Diagnostics.Protocol;

/// <summary>
/// Constants shared by the diagnostics endpoint, the CLI session host and the CLI client.
/// </summary>
internal static class DiagnosticsProtocol
{
    /// <summary>
    /// Wire protocol version. Additive fields keep the version; breaking changes bump it.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Largest accepted message: one UTF-8 JSON line, excluding the newline.
    /// </summary>
    public const int MaxMessageBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Most received messages a connection holds before its owner reads them. A client keeps a handful of requests in
    /// flight (a command, its cancel) and notifications are consumed as they arrive, so normal use stays in single
    /// digits. Only a stuck reader or a flooding peer reaches this, and the connection is then closed.
    /// </summary>
    public const int MaxQueuedMessages = 1024;

    /// <summary>
    /// Most received bytes a connection holds before its owner reads them: four maximal messages. Normal results
    /// (a tree, a screen) are kilobytes to a few MiB. This bounds what a peer can make the app allocate.
    /// </summary>
    public const int MaxQueuedBytes = 4 * MaxMessageBytes;

    public const string HelloMethod = "hello";
    public const string ProgressMethod = "progress";
    public const string CancelMethod = "cancel";
}

/// <summary>
/// The closed set of error codes carried in <see cref="DiagnosticsError.Code"/>.
/// </summary>
internal static class DiagnosticsErrorCodes
{
    // Protocol
    public const string InvalidRequest = "invalid_request";
    public const string MethodNotFound = "method_not_found";
    public const string InvalidParams = "invalid_params";
    public const string MessageTooLarge = "message_too_large";
    public const string ProtocolMismatch = "protocol_mismatch";
    public const string QueueOverflow = "queue_overflow";
    public const string TooManyConnections = "too_many_connections";

    // Commands
    public const string Usage = "usage";
    public const string AssertionFailed = "assertion_failed";
    public const string SessionNotFound = "session_not_found";
    public const string ElementNotFound = "element_not_found";
    public const string AmbiguousSelector = "ambiguous_selector";
    public const string ElementNotVisible = "element_not_visible";
    public const string HostUnavailable = "host_unavailable";
    public const string HostVersionMismatch = "host_version_mismatch";
    public const string AppLayerUnavailable = "app_layer_unavailable";
    public const string InputUnavailable = "input_unavailable";
    public const string ScreenUnavailable = "screen_unavailable";
    public const string Timeout = "timeout";
    public const string ProcessExited = "process_exited";
    public const string PtyUnsupported = "pty_unsupported";
    public const string Internal = "internal";
}
