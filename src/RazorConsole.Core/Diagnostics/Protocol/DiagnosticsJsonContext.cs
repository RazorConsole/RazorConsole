// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json.Serialization;

namespace RazorConsole.Core.Diagnostics.Protocol;

/// <summary>
/// Source-generated JSON metadata for the diagnostics protocol, so it stays trim and AOT safe.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiagnosticsMessage))]
[JsonSerializable(typeof(HelloParams))]
[JsonSerializable(typeof(HelloResult))]
[JsonSerializable(typeof(CancelParams))]
internal sealed partial class DiagnosticsJsonContext : JsonSerializerContext
{
}
