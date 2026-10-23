// Copyright (c) RazorConsole. All rights reserved.

using System.Buffers;
using System.Text;
using System.Text.Json;
using RazorConsole.Cli.Operations;

namespace RazorConsole.Cli.Output;

/// <summary>
/// Writes a <see cref="CliResult"/>. With <c>--json</c>, success and failure are both one JSON line on stdout:
/// <c>{"ok":true,"session":"s1",…result fields}</c> or <c>{"ok":false,"error":{"code","message","data"}}</c>.
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
            error.WriteLine($"razorconsole: {failure.Message} [{failure.Code}]");
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
            else if (result.Data is { ValueKind: JsonValueKind.Object } data)
            {
                foreach (var property in data.EnumerateObject())
                {
                    // The envelope's own fields win over a result field of the same name.
                    if (property.NameEquals("ok") || property.NameEquals("session"))
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
