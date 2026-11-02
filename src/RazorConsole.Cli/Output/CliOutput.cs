// Copyright (c) RazorConsole. All rights reserved.

using System.Buffers;
using System.Text;
using System.Text.Json;
using RazorConsole.Cli.Operations;

namespace RazorConsole.Cli.Output;

/// <summary>
/// Writes a <see cref="CliResult"/>. With <c>--json</c>, success and failure are both one JSON line on stdout:
/// <c>{"ok":true,"session":"s1",…result fields}</c> or <c>{"ok":false,"error":{"code","message","data"}}</c>. A failure
/// that also has result fields (doctor's <c>checks</c>) writes them at the top level, next to <c>error</c>.
/// Without it, the result text goes to stdout and errors to stderr.
/// </summary>
internal static class CliOutput
{
    public static void Write(CliResult result, bool json, TextWriter output, TextWriter error)
    {
        if (json)
        {
            output.WriteLine(ToJson(result));
            return;
        }

        if (result.Error is { } failure)
        {
            // A failure can still carry a result people read first, such as doctor's checklist.
            if (!string.IsNullOrEmpty(result.Text))
            {
                output.WriteLine(result.Text);
            }

            error.WriteLine($"razorconsole: {failure.Message} [{failure.Code}]");

            // A timed-out wait and a failed assertion carry the screen; people need it more than the code.
            if (failure.Data is { ValueKind: JsonValueKind.Object } data
                && data.TryGetProperty("screen", out var screen)
                && screen.ValueKind == JsonValueKind.String)
            {
                error.WriteLine("--- screen ---");
                error.WriteLine(screen.GetString());
            }

            // An exited process explains itself here: what its app last reported, and what the terminal last showed.
            if (failure.Data is { ValueKind: JsonValueKind.Object } exited)
            {
                if (exited.TryGetProperty("lastErrors", out var lastErrors) && lastErrors.ValueKind == JsonValueKind.Array && lastErrors.GetArrayLength() > 0)
                {
                    error.WriteLine("--- last errors ---");
                    foreach (var item in lastErrors.EnumerateArray())
                    {
                        error.WriteLine(AppText.ErrorLine(item));
                    }
                }

                if (exited.TryGetProperty("outputTail", out var outputTail) && outputTail.ValueKind == JsonValueKind.String)
                {
                    error.WriteLine("--- output tail ---");
                    error.WriteLine(outputTail.GetString());
                }
            }

            return;
        }

        if (!string.IsNullOrEmpty(result.Text))
        {
            output.WriteLine(result.Text);
        }
    }

    public static string ToJson(CliResult result)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", result.Ok);
            if (result.Session is not null)
            {
                writer.WriteString("session", result.Session);
            }

            if (result.Error is { } failure)
            {
                writer.WriteStartObject("error");
                writer.WriteString("code", failure.Code);
                writer.WriteString("message", failure.Message);
                if (failure.Data is { } errorData)
                {
                    writer.WritePropertyName("data");
                    errorData.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            if (result.Data is { ValueKind: JsonValueKind.Object } data)
            {
                foreach (var property in data.EnumerateObject())
                {
                    // The envelope's own fields (ok, session, error) win over a result field of the same name.
                    if (property.NameEquals("ok") || property.NameEquals("session") || property.NameEquals("error"))
                    {
                        continue;
                    }

                    property.WriteTo(writer);
                }
            }
            else if (result.Data is { } value)
            {
                writer.WritePropertyName("result");
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
