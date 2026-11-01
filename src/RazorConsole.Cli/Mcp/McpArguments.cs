// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text.Json;
using RazorConsole.Cli.Commands;

namespace RazorConsole.Cli.Mcp;

/// <summary>
/// Reads the arguments of one tool call. Problems are collected, not thrown, so one reply can list all of them:
/// a wrong type, a value the CLI would refuse, and any property the tool does not have.
/// </summary>
internal sealed class McpArguments
{
    private readonly IDictionary<string, JsonElement> _values;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);

    public McpArguments(IDictionary<string, JsonElement>? values)
    {
        _values = values ?? new Dictionary<string, JsonElement>();
    }

    public List<string> Errors { get; } = [];

    public bool Has(string name) => TryGet(name, out _);

    public string? String(string name, bool required = false)
    {
        if (!TryGet(name, out var value))
        {
            return Missing<string>(name, required);
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            Errors.Add($"{name}: must be a string.");
            return null;
        }

        return value.GetString();
    }

    public string? Text(string name, bool required = false)
    {
        var value = String(name, required);
        if (value is { Length: 0 } && required)
        {
            Errors.Add($"{name}: must not be empty.");
        }

        return value;
    }

    public int? Integer(string name, int minimum = int.MinValue, int maximum = int.MaxValue, bool required = false)
    {
        if (!TryGet(name, out var value))
        {
            return Missing<int?>(name, required);
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            Errors.Add($"{name}: must be a whole number.");
            return null;
        }

        if (number < minimum || number > maximum)
        {
            Errors.Add(maximum == int.MaxValue ? $"{name}: must be at least {minimum}." : $"{name}: must be between {minimum} and {maximum}.");
            return null;
        }

        return number;
    }

    public long? Long(string name, long minimum = 0)
    {
        if (!TryGet(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            Errors.Add($"{name}: must be a whole number.");
            return null;
        }

        if (number < minimum)
        {
            Errors.Add($"{name}: must be at least {minimum.ToString(CultureInfo.InvariantCulture)}.");
            return null;
        }

        return number;
    }

    public bool Boolean(string name, bool defaultValue = false)
    {
        if (!TryGet(name, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        Errors.Add($"{name}: must be true or false.");
        return defaultValue;
    }

    public IReadOnlyList<string>? StringArray(string name, bool required = false)
    {
        if (!TryGet(name, out var value))
        {
            return Missing<IReadOnlyList<string>>(name, required);
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            Errors.Add($"{name}: must be an array of strings.");
            return null;
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                Errors.Add($"{name}: must be an array of strings.");
                return null;
            }

            items.Add(item.GetString()!);
        }

        if (required && items.Count == 0)
        {
            Errors.Add($"{name}: must not be empty.");
        }

        return items;
    }

    public IReadOnlyDictionary<string, string> StringMap(string name)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryGet(name, out var value))
        {
            return map;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            Errors.Add($"{name}: must be an object of strings.");
            return map;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                Errors.Add($"{name}.{property.Name}: must be a string.");
                continue;
            }

            if (property.Name.Contains('=', StringComparison.Ordinal))
            {
                Errors.Add($"{name}: '{property.Name}' is not a variable name.");
                continue;
            }

            if (CliValueParsers.TryParseEnvironmentVariable($"{property.Name}={property.Value.GetString()}", out var variable, out var error))
            {
                map[variable.Key] = variable.Value;
            }
            else
            {
                Errors.Add($"{name}: {error}");
            }
        }

        return map;
    }

    /// <summary>
    /// A duration: a string with a unit (<c>500ms</c>, <c>10s</c>, <c>2m</c>), or a number of seconds. Every message about
    /// a bad value states that rule and says how to fix the value, because an agent that reads milliseconds into a number
    /// can only correct itself from the message.
    /// </summary>
    /// <param name="name">The argument.</param>
    /// <param name="maximum">The longest value allowed, or <see langword="null"/> for any. With a maximum, a value
    /// must also be greater than 0.</param>
    public TimeSpan? Duration(string name, TimeSpan? maximum = null)
    {
        if (!TryGet(name, out var value))
        {
            return null;
        }

        string text;
        double? seconds = null;
        if (value.ValueKind == JsonValueKind.Number)
        {
            text = value.GetRawText();
            seconds = value.TryGetDouble(out var number) ? number : double.PositiveInfinity;
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            text = value.GetString()!;

            // A number sent as text is read the same way: "10" is 10 seconds.
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                seconds = number;
            }
        }
        else
        {
            Errors.Add($"{name}: must be a duration. {DurationRule}");
            return null;
        }

        if (seconds is { } given)
        {
            return CheckSeconds(name, text, given, maximum);
        }

        if (!CliValueParsers.TryParseDuration(text, out var parsed, out _))
        {
            Errors.Add($"{name}: '{text}' is not a duration. {DurationRule}");
            return null;
        }

        if (maximum is not null && parsed <= TimeSpan.Zero)
        {
            Errors.Add($"{name}: '{text}' must be greater than 0. {DurationRule}");
            return null;
        }

        if (maximum is { } upper && parsed > upper)
        {
            Errors.Add($"{name}: '{text}' is above the {McpTools.Describe(upper)} limit. Use a shorter duration such as \"{McpTools.Describe(upper)}\" or less, or a number of seconds.");
            return null;
        }

        return parsed;
    }

    private const string DurationRule = "A number is seconds (e.g. 30), not milliseconds; or a string with a unit: 500ms, 10s, 2m.";

    private TimeSpan? CheckSeconds(string name, string text, double seconds, TimeSpan? maximum)
    {
        if (!(seconds > 0))
        {
            Errors.Add($"{name}: {text} is read as seconds and must be greater than 0. {Fix(10)}");
            return null;
        }

        var span = TimeSpan.FromSeconds(Math.Min(seconds, MaxSeconds));
        if (maximum is { } limit && span > limit)
        {
            // A number in a plausible millisecond range (60000) is most likely milliseconds: suggest it as seconds.
            var suggestion = seconds >= 1000 && seconds / 1000 <= limit.TotalSeconds ? seconds / 1000 : limit.TotalSeconds;
            Errors.Add($"{name}: {text} is read as seconds ({text}s), above the {McpTools.Describe(limit)} limit. {Fix(suggestion)}");
            return null;
        }

        return span;
    }

    private static string Fix(double seconds)
    {
        var number = seconds.ToString("0.###", CultureInfo.InvariantCulture);
        return $"Numbers are seconds; use {number} or \"{number}s\", or \"500ms\" for milliseconds.";
    }

    public void AddError(string message) => Errors.Add(message);

    /// <summary>
    /// Reports every argument that no one read: the tool does not have it.
    /// </summary>
    public void RejectUnknown()
    {
        foreach (var name in _values.Keys.Where(name => !_read.Contains(name)).Order(StringComparer.Ordinal))
        {
            Errors.Add($"{name}: this tool has no such argument.");
        }
    }

    // Far above any limit; keeps TimeSpan.FromSeconds from overflowing so the limit check can report it.
    private const double MaxSeconds = 1_000_000;

    private bool TryGet(string name, out JsonElement value)
    {
        _read.Add(name);
        return _values.TryGetValue(name, out value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    private T? Missing<T>(string name, bool required)
    {
        if (required)
        {
            Errors.Add($"{name}: is required.");
        }

        return default;
    }
}
