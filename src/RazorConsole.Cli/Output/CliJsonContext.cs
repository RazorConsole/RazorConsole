// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json.Serialization;
using RazorConsole.Cli.Operations;

namespace RazorConsole.Cli.Output;

/// <summary>
/// The <c>data</c> of a <c>usage</c> error: every problem the parser found.
/// </summary>
/// <param name="Errors">The messages, in order.</param>
internal sealed record UsageErrorData(IReadOnlyList<string> Errors);

/// <summary>
/// Source-generated JSON metadata for the CLI's own results, so the tool stays trim and AOT safe.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UsageErrorData))]
[JsonSerializable(typeof(DoctorResult))]
internal sealed partial class CliJsonContext : JsonSerializerContext
{
}
