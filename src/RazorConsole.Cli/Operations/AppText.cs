// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text;
using System.Text.Json;
using RazorConsole.Core.Diagnostics.Protocol;

namespace RazorConsole.Cli.Operations;

/// <summary>
/// The human-readable form of the app-layer results. <c>--json</c> prints the app's own JSON instead.
/// </summary>
internal static class AppText
{
    public static string Tree(TreeResult result)
        => result.Root is null ? "(nothing rendered yet)" : new StringBuilder().Also(builder => AppendNode(builder, result.Root, 0)).ToString().TrimEnd('\r', '\n');

    public static string Find(FindResult result)
    {
        if (result.Matches.Count == 0)
        {
            return "no matches";
        }

        var text = new StringBuilder();
        foreach (var match in result.Matches)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(Describe(match.Tag ?? match.Kind, match.Id, null, null, match.FocusKey, null, match.Text, match.Rect));
        }

        return text.ToString();
    }

    public static string Inspect(InspectResult result)
    {
        var text = new StringBuilder();
        AppendNode(text, result.Node, 0);
        foreach (var ancestor in result.Ancestors)
        {
            text.Append("in ").AppendLine(Describe(ancestor.Tag ?? ancestor.Kind, ancestor.Id, null, null, ancestor.FocusKey, null, null, ancestor.Rect));
        }

        return text.ToString().TrimEnd('\r', '\n');
    }

    public static string InspectAt(InspectAtResult result)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"({result.X},{result.Y})");
        if (result.Cell is { } cell)
        {
            text.Append(CultureInfo.InvariantCulture, $" cell '{cell.Text}'");
            if (cell.Fg is not null)
            {
                text.Append(" fg ").Append(cell.Fg);
            }

            if (cell.Bg is not null)
            {
                text.Append(" bg ").Append(cell.Bg);
            }
        }

        if (result.Stack.Count == 0)
        {
            text.Append("\nno element");
        }

        foreach (var match in result.Stack)
        {
            text.Append('\n').Append(Describe(match.Tag ?? match.Kind, match.Id, null, null, match.FocusKey, null, match.Text, match.Rect));
        }

        return text.ToString();
    }

    public static string Focus(FocusResult result)
    {
        if (result.Targets.Count == 0)
        {
            return "no focus targets";
        }

        return string.Join('\n', result.Targets.Select(target => (string.Equals(target.Key, result.Current, StringComparison.Ordinal) ? "* " : "  ") + target.Key));
    }

    public static string FocusChange(FocusChangeResult result)
        => (result.Changed ? "focus moved to " : "focus stays on ") + (result.Current is null ? "nothing" : $"'{result.Current}'");

    public static string Route(RouteResult result) => result.Uri;

    public static string Navigated(NavigateResult result) => "navigated to " + result.Uri;

    public static string Stats(StatsResult result)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"pipeline {result.Pipeline}, frame {result.Frame}, {result.Frames} frame(s) rendered, {result.DroppedFrames} dropped, render {result.RenderMs.Last:0.##} ms (avg {result.RenderMs.Avg:0.##}, max {result.RenderMs.Max:0.##}), up {result.UptimeMs / 1000.0:0.#} s");

    /// <summary>
    /// One error of an <c>errors</c> result, from its JSON.
    /// </summary>
    public static string ErrorLine(JsonElement error)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"[{Number(error, "seq")}] {Text(error, "time")} {Text(error, "source")} {Text(error, "type")}: {Text(error, "message")}");

    /// <summary>
    /// One entry of a <c>logs</c> result, from its JSON.
    /// </summary>
    public static string LogLine(JsonElement entry)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"[{Number(entry, "seq")}] {Text(entry, "time")} {Text(entry, "level")} {Text(entry, "category")}: {Text(entry, "message")}");
        return entry.TryGetProperty("exception", out var exception) && exception.ValueKind == JsonValueKind.String
            ? line + "\n" + exception.GetString()
            : line;
    }

    private static string Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : "?";

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    private static StringBuilder Also(this StringBuilder builder, Action<StringBuilder> action)
    {
        action(builder);
        return builder;
    }

    private static void AppendNode(StringBuilder text, DiagnosticsNode node, int depth)
    {
        text.Append(' ', depth * 2)
            .AppendLine(Describe(node.Tag ?? node.Kind, node.Id, node.Key, node.Hook, node.FocusKey, node.Focused, node.Text, node.Rect));
        if (node.Children is { } children)
        {
            foreach (var child in children)
            {
                // Razor's whitespace between elements is a text node of its own; --json keeps it.
                if (IsBlankText(child))
                {
                    continue;
                }

                AppendNode(text, child, depth + 1);
            }
        }
        else if (node.ChildCount is > 0)
        {
            text.Append(' ', (depth + 1) * 2).AppendLine(string.Create(CultureInfo.InvariantCulture, $"… {node.ChildCount} child(ren)"));
        }
    }

    private static bool IsBlankText(DiagnosticsNode node)
        => string.Equals(node.Kind, "text", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(node.Text);

    /// <summary>
    /// The text in quotes on one line. A bare carriage return would move the terminal's cursor and overwrite the start
    /// of the line, so every control character is escaped.
    /// </summary>
    internal static string Quote(string text)
    {
        var quoted = new StringBuilder(text.Length + 2).Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\':
                    quoted.Append("\\\\");
                    break;
                case '"':
                    quoted.Append("\\\"");
                    break;
                case '\n':
                    quoted.Append("\\n");
                    break;
                case '\r':
                    quoted.Append("\\r");
                    break;
                case '\t':
                    quoted.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        quoted.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    }
                    else
                    {
                        quoted.Append(c);
                    }

                    break;
            }
        }

        return quoted.Append('"').ToString();
    }

    private static string Describe(string label, string? id, string? key, string? hook, string? focusKey, bool? focused, string? text, DiagnosticsRect? rect)
    {
        var line = new StringBuilder(label);
        if (id is not null)
        {
            line.Append('#').Append(id);
        }

        if (key is not null)
        {
            line.Append(" key=").Append(key);
        }

        if (hook is not null)
        {
            line.Append(" hook=").Append(hook);
        }

        if (focusKey is not null)
        {
            line.Append(" focus=").Append(focusKey);
            if (focused == true)
            {
                line.Append('*');
            }
        }

        if (!string.IsNullOrEmpty(text))
        {
            line.Append(' ').Append(Quote(text));
        }

        if (rect is not null)
        {
            line.Append(CultureInfo.InvariantCulture, $" @{rect.Left},{rect.Top} {rect.Width}x{rect.Height}");
        }

        return line.ToString();
    }
}
