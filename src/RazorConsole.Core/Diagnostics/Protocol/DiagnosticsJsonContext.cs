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
[JsonSerializable(typeof(DiagnosticsDiscoveryInfo))]
[JsonSerializable(typeof(TreeParams))]
[JsonSerializable(typeof(TreeResult))]
[JsonSerializable(typeof(FindParams))]
[JsonSerializable(typeof(FindResult))]
[JsonSerializable(typeof(InspectParams))]
[JsonSerializable(typeof(InspectResult))]
[JsonSerializable(typeof(InspectAtParams))]
[JsonSerializable(typeof(InspectAtResult))]
[JsonSerializable(typeof(FocusResult))]
[JsonSerializable(typeof(RouteResult))]
[JsonSerializable(typeof(FocusSetParams))]
[JsonSerializable(typeof(FocusChangeResult))]
[JsonSerializable(typeof(NavigateParams))]
[JsonSerializable(typeof(NavigateResult))]
[JsonSerializable(typeof(ErrorsParams))]
[JsonSerializable(typeof(ErrorsResult))]
[JsonSerializable(typeof(LogsParams))]
[JsonSerializable(typeof(LogsResult))]
[JsonSerializable(typeof(StatsResult))]
[JsonSerializable(typeof(AmbiguousSelectorData))]
[JsonSerializable(typeof(DiagnosticsUnavailableData))]
[JsonSerializable(typeof(ScreenParams))]
[JsonSerializable(typeof(ScreenResult))]
[JsonSerializable(typeof(InputParams))]
[JsonSerializable(typeof(InputResult))]
[JsonSerializable(typeof(InputTimeoutData))]
internal sealed partial class DiagnosticsJsonContext : JsonSerializerContext
{
}
