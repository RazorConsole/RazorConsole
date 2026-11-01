// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using RazorConsole.Cli.Operations;

namespace RazorConsole.Cli.Mcp;

/// <summary>
/// Keeps the results of the MCP tools small. A result that can grow is cut, and the cut is never silent: the result
/// gets a <c>truncated</c> array saying which field was cut, how much was kept and what to do about it. The CLI's own
/// output is not touched; this only runs on the MCP side.
/// </summary>
internal static class McpResultLimiter
{
    /// <summary>
    /// The most characters of JSON one tool result may have (about 25k tokens).
    /// </summary>
    public const int MaxResultCharacters = 100_000;

    /// <summary>
    /// The size a tree is cut to when the caller did not ask for a depth (about 5k tokens); an explicit depth may use
    /// <see cref="MaxResultCharacters"/>.
    /// </summary>
    public const int DefaultTreeBudget = 20_000;

    /// <summary>
    /// The depth of a tree when the caller gives none.
    /// </summary>
    public const int DefaultTreeDepth = 3;

    public const int DefaultLogLimit = 100;
    public const int MaxLogLimit = 1000;
    public const int MaxMatches = 100;
    public const int MaxMessageCharacters = 2000;
    public const int MaxExceptionCharacters = 4000;
    public const int DefaultOutputBytes = 64 * 1024;
    public const int MaxOutputBytes = 1024 * 1024;

    // Room for the truncated array itself.
    private const int MarkerAllowance = 1024;

    public static CliResult Apply(McpShape shape, McpBound bound, CliResult result)
    {
        if (result.Error is not null || result.Data is not { ValueKind: JsonValueKind.Object } data)
        {
            return result;
        }

        var node = JsonNode.Parse(data.GetRawText())!.AsObject();
        var marks = new List<JsonObject>();
        switch (shape)
        {
            case McpShape.Screenshot:
                FitArray(node, "cells", keepNewest: false, "cells", "Pass `region` (x,y,width,height) to read less of the screen.", marks);
                FitArray(node, "lines", keepNewest: false, "lines", "Pass `region` (x,y,width,height) to read less of the screen.", marks);
                FitString(node, "ansi", "ansi", "Pass `region` (x,y,width,height) to read less of the screen.", marks);
                break;

            case McpShape.Frames:
                FitArray(node, "frames", keepNewest: true, "frames", "The oldest frames were dropped. Pass a smaller `last`.", marks);
                break;

            case McpShape.Diff:
                FitArray(node, "changes", keepNewest: false, "changes", "Later changed lines were dropped. Use screenshot with a `region`.", marks);
                break;

            case McpShape.Output:
                LimitOutput(node, bound.MaxBytes, marks);
                break;

            case McpShape.Tree:
                PruneTree(node, marks, bound.TreeBudget > 0 ? bound.TreeBudget : DefaultTreeBudget);
                break;

            case McpShape.Find:
                LimitMatches(node, marks);
                FitArray(node, "matches", keepNewest: false, "matches", "Use a narrower selector.", marks);
                break;

            case McpShape.Logs:
                LimitEntries(node, "entries", bound.Limit, MaxExceptionCharacters, marks);
                break;

            case McpShape.Errors:
                LimitEntries(node, "errors", bound.Limit, MaxExceptionCharacters, marks);
                break;
        }

        // Whatever is still too big (a shape not listed above, or a single huge field) is replaced, so the result
        // stays valid JSON and says what happened.
        if (Size(node) + MarkerAllowance > MaxResultCharacters)
        {
            var total = Size(node);
            node = new JsonObject();
            marks.Add(Mark("result", null, total, "The result is too large. Ask for less: a selector, a region, a smaller limit."));
        }

        if (marks.Count > 0)
        {
            var array = new JsonArray();
            foreach (var mark in marks)
            {
                array.Add((JsonNode?)mark);
            }

            node["truncated"] = array;
        }

        return result with { Data = Parse(node) };
    }

    /// <summary>
    /// Cuts a tree from the bottom until it fits: the deepest level loses its children, and the nodes it cut show their
    /// <c>childCount</c>, like a tree read with <c>depth</c>. The tree stays valid.
    /// </summary>
    public static void PruneTree(JsonObject node, List<JsonObject> marks, int budget)
    {
        if (Size(node) + MarkerAllowance <= budget || node["root"] is not JsonObject root)
        {
            return;
        }

        var depth = Depth(root);
        var kept = depth;
        while (kept > 0 && Size(node) + MarkerAllowance > budget)
        {
            CutLevel(root, 0, kept - 1);
            kept--;
        }

        marks.Add(Mark("root", kept + 1, depth + 1, "The tree was cut to " + (kept + 1) + " level(s); cut nodes show `childCount`. Pass `selector` for a subtree, or a larger `depth` for more."));
    }

    private static int Depth(JsonObject node)
        => node["children"] is JsonArray { Count: > 0 } children ? 1 + children.OfType<JsonObject>().Max(Depth) : 0;

    // Removes the children of every node at `level`.
    private static void CutLevel(JsonObject node, int current, int level)
    {
        if (node["children"] is not JsonArray children)
        {
            return;
        }

        if (current == level)
        {
            node["childCount"] = children.Count;
            node.Remove("children");
            return;
        }

        foreach (var child in children.OfType<JsonObject>())
        {
            CutLevel(child, current + 1, level);
        }
    }

    private static void LimitMatches(JsonObject node, List<JsonObject> marks)
    {
        if (node["matches"] is JsonArray { Count: > MaxMatches } matches)
        {
            var total = matches.Count;
            for (var i = total - 1; i >= MaxMatches; i--)
            {
                matches.RemoveAt(i);
            }

            marks.Add(Mark("matches", MaxMatches, total, "More elements match. Use a narrower selector."));
        }
    }

    private static void LimitOutput(JsonObject node, int maxBytes, List<JsonObject> marks)
    {
        if (maxBytes <= 0 || node["offset"]?.GetValue<long>() is not { } offset || node["next"]?.GetValue<long>() is not { } next)
        {
            return;
        }

        var returned = next - offset;
        if (returned >= maxBytes)
        {
            marks.Add(Mark("data", returned, null, "There may be more output. Call get_output again with since=" + next + "."));
        }
    }

    // errors and logs: the first `limit` entries, long fields cut, and `next` moved back to just after the last kept
    // entry so paging with it loses nothing.
    private static void LimitEntries(JsonObject node, string property, int limit, int maxException, List<JsonObject> marks)
    {
        if (node[property] is not JsonArray entries)
        {
            return;
        }

        var total = entries.Count;
        foreach (var entry in entries.OfType<JsonObject>())
        {
            CutText(entry, "message", MaxMessageCharacters);
            CutText(entry, "exception", maxException);
            CutText(entry, "details", maxException);
            CutText(entry, "stack", maxException);
        }

        var keep = limit > 0 ? Math.Min(limit, total) : total;
        var overhead = Size(node) - Size(entries);
        var budget = MaxResultCharacters - overhead - MarkerAllowance;
        var used = 0;
        var fitting = 0;
        foreach (var entry in entries)
        {
            used += Size(entry) + 1;
            if (used > budget)
            {
                break;
            }

            fitting++;
        }

        keep = Math.Min(keep, Math.Max(fitting, Math.Min(1, total)));
        if (keep >= total)
        {
            return;
        }

        for (var i = total - 1; i >= keep; i--)
        {
            entries.RemoveAt(i);
        }

        if (keep > 0 && entries[keep - 1] is JsonObject last && last["seq"]?.GetValue<long>() is { } seq)
        {
            node["next"] = seq + 1;
        }

        marks.Add(Mark(property, keep, total, "More entries follow. Call again with since=" + node["next"] + "."));
    }

    private static void CutText(JsonObject entry, string property, int maximum)
    {
        if (entry[property] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > maximum)
        {
            entry[property] = text[..maximum] + $"…[+{text.Length - maximum} chars]";
        }
    }

    private static void FitString(JsonObject node, string property, string field, string hint, List<JsonObject> marks)
    {
        if (Size(node) + MarkerAllowance <= MaxResultCharacters || node[property] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return;
        }

        var budget = Math.Max(0, MaxResultCharacters - (Size(node) - Size(value)) - MarkerAllowance);
        budget = Math.Min(budget, text.Length);
        node[property] = text[..budget];
        marks.Add(Mark(field, budget, text.Length, hint));
    }

    // Keeps the first (or last) items that fit in what the rest of the result leaves of the budget.
    private static void FitArray(JsonObject node, string property, bool keepNewest, string field, string hint, List<JsonObject> marks)
    {
        if (Size(node) + MarkerAllowance <= MaxResultCharacters || node[property] is not JsonArray array)
        {
            return;
        }

        var total = array.Count;
        var budget = MaxResultCharacters - (Size(node) - Size(array)) - MarkerAllowance;
        var used = 0;
        var keep = 0;
        for (var i = 0; i < total; i++)
        {
            var item = array[keepNewest ? total - 1 - i : i];
            used += Size(item) + 1;
            if (used > budget)
            {
                break;
            }

            keep++;
        }

        if (keep == total)
        {
            return;
        }

        if (keepNewest)
        {
            for (var i = total - keep - 1; i >= 0; i--)
            {
                array.RemoveAt(i);
            }
        }
        else
        {
            for (var i = total - 1; i >= keep; i--)
            {
                array.RemoveAt(i);
            }
        }

        marks.Add(Mark(field, keep, total, hint));
    }

    private static JsonObject Mark(string field, long? kept, long? total, string hint)
    {
        var mark = new JsonObject { ["field"] = field };
        if (kept is { } k)
        {
            mark["kept"] = k;
        }

        if (total is { } t)
        {
            mark["total"] = t;
        }

        mark["hint"] = hint;
        return mark;
    }

    private static int Size(JsonNode? node) => node?.ToJsonString().Length ?? 0;

    private static JsonElement Parse(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
