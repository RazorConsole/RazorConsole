// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;

namespace RazorConsole.Cli.Mcp;

/// <summary>
/// Builds the JSON schema of a tool's arguments. The schemas are written out, not reflected from a delegate, so the
/// server stays trim and AOT safe, and every schema is <c>additionalProperties: false</c>.
/// </summary>
internal sealed class McpSchema
{
    private readonly List<(string Name, bool Required, Action<Utf8JsonWriter> Write)> _properties = [];

    public McpSchema String(string name, string description, bool required = false, params string[] allowed)
        => Add(name, required, writer =>
        {
            writer.WriteString("type", "string");
            writer.WriteString("description", description);
            if (allowed.Length > 0)
            {
                writer.WriteStartArray("enum");
                foreach (var value in allowed)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
            }
        });

    public McpSchema Integer(string name, string description, bool required = false, int? minimum = null, int? maximum = null)
        => Add(name, required, writer =>
        {
            writer.WriteString("type", "integer");
            writer.WriteString("description", description);
            if (minimum is { } min)
            {
                writer.WriteNumber("minimum", min);
            }

            if (maximum is { } max)
            {
                writer.WriteNumber("maximum", max);
            }
        });

    /// <summary>
    /// A duration: a string with a unit such as <c>500ms</c>, <c>10s</c> or <c>2m</c>, or a number of seconds, not milliseconds.
    /// </summary>
    public McpSchema Duration(string name, string description)
        => Add(name, false, writer =>
        {
            writer.WriteStartArray("oneOf");
            writer.WriteStartObject();
            writer.WriteString("type", "string");
            writer.WriteString("description", "A string with a unit: 500ms, 10s or 2m.");
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("type", "number");
            writer.WriteString("description", "A number of seconds (e.g. 30), not milliseconds.");
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteString("description", "A number is seconds (e.g. 30), not milliseconds; or a string with a unit: 500ms, 10s, 2m. " + description);
        });

    public McpSchema Boolean(string name, string description)
        => Add(name, false, writer =>
        {
            writer.WriteString("type", "boolean");
            writer.WriteString("description", description);
        });

    public McpSchema StringArray(string name, string description, bool required = false)
        => Add(name, required, writer =>
        {
            writer.WriteString("type", "array");
            writer.WriteString("description", description);
            writer.WriteStartObject("items");
            writer.WriteString("type", "string");
            writer.WriteEndObject();
        });

    public McpSchema StringMap(string name, string description)
        => Add(name, false, writer =>
        {
            writer.WriteString("type", "object");
            writer.WriteString("description", description);
            writer.WriteStartObject("additionalProperties");
            writer.WriteString("type", "string");
            writer.WriteEndObject();
        });

    /// <summary>
    /// The names of the properties, for tests.
    /// </summary>
    public IReadOnlyList<string> Names => _properties.ConvertAll(static property => property.Name);

    public bool Has(string name) => _properties.Exists(property => property.Name == name);

    public JsonElement Build()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");
            foreach (var (name, _, write) in _properties)
            {
                writer.WriteStartObject(name);
                write(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            var required = _properties.Where(static property => property.Required).Select(static property => property.Name).ToList();
            if (required.Count > 0)
            {
                writer.WriteStartArray("required");
                foreach (var name in required)
                {
                    writer.WriteStringValue(name);
                }

                writer.WriteEndArray();
            }

            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private McpSchema Add(string name, bool required, Action<Utf8JsonWriter> write)
    {
        _properties.Add((name, required, write));
        return this;
    }
}
