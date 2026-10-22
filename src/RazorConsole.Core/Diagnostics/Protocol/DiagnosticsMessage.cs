// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RazorConsole.Core.Diagnostics.Protocol;

/// <summary>
/// One NDJSON line. A subset of JSON-RPC 2.0 without the <c>jsonrpc</c> member:
/// a request has <see cref="Id"/> and <see cref="Method"/>, a notification only <see cref="Method"/>,
/// and a response <see cref="Id"/> with either <see cref="Result"/> or <see cref="Error"/>.
/// </summary>
internal sealed record DiagnosticsMessage
{
    public long? Id { get; init; }

    public string? Method { get; init; }

    public JsonElement? Params { get; init; }

    public JsonElement? Result { get; init; }

    public DiagnosticsError? Error { get; init; }

    [JsonIgnore]
    public bool IsRequest => Id is not null && Method is not null;

    [JsonIgnore]
    public bool IsNotification => Id is null && Method is not null;

    [JsonIgnore]
    public bool IsResponse => Id is not null && Method is null;

    public static DiagnosticsMessage Request<T>(long id, string method, T parameters, JsonTypeInfo<T> typeInfo)
        => new() { Id = id, Method = method, Params = JsonSerializer.SerializeToElement(parameters, typeInfo) };

    public static DiagnosticsMessage Notification<T>(string method, T parameters, JsonTypeInfo<T> typeInfo)
        => new() { Method = method, Params = JsonSerializer.SerializeToElement(parameters, typeInfo) };

    public static DiagnosticsMessage Success<T>(long id, T result, JsonTypeInfo<T> typeInfo)
        => new() { Id = id, Result = JsonSerializer.SerializeToElement(result, typeInfo) };

    public static DiagnosticsMessage Failure(long? id, DiagnosticsError error)
        => new() { Id = id, Error = error };

    public T? GetParams<T>(JsonTypeInfo<T> typeInfo)
        => Read(Params, typeInfo);

    public T? GetResult<T>(JsonTypeInfo<T> typeInfo)
        => Read(Result, typeInfo);

    private static T? Read<T>(JsonElement? element, JsonTypeInfo<T> typeInfo)
    {
        if (element is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } value)
        {
            return default;
        }

        try
        {
            return value.Deserialize(typeInfo);
        }
        catch (JsonException ex)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, ex.Message, ex);
        }
    }
}

/// <summary>
/// A structured error: a code from <see cref="DiagnosticsErrorCodes"/>, a human message and optional details.
/// </summary>
internal sealed record DiagnosticsError(string Code, string Message, JsonElement? Data = null);

/// <summary>
/// Parameters of <see cref="DiagnosticsProtocol.HelloMethod"/>, the first request on every connection.
/// </summary>
internal sealed record HelloParams(int Protocol, string Client, IReadOnlyList<string>? Features = null);

/// <summary>
/// Result of <see cref="DiagnosticsProtocol.HelloMethod"/>.
/// </summary>
internal sealed record HelloResult(int Protocol, string Server, int ProcessId, IReadOnlyList<string>? Features = null);

/// <summary>
/// Parameters of a <see cref="DiagnosticsProtocol.CancelMethod"/> notification.
/// </summary>
internal sealed record CancelParams(long Id);

/// <summary>
/// Raised when a peer breaks the protocol or a request fails. <see cref="Code"/> is a <see cref="DiagnosticsErrorCodes"/> value.
/// </summary>
internal sealed class DiagnosticsProtocolException(string code, string message, Exception? innerException = null, JsonElement? errorData = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;

    /// <summary>
    /// Details sent as <see cref="DiagnosticsError.Data"/>, such as the candidates of an ambiguous selector.
    /// </summary>
    public JsonElement? ErrorData { get; } = errorData;

    public DiagnosticsError ToError() => new(Code, Message, ErrorData);
}
