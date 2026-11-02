// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using RazorConsole.Cli.Mcp;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;

namespace RazorConsole.Tests.Mcp;

/// <summary>
/// Results that can grow are cut with an explicit marker, never silently, and the cut result is still valid JSON that
/// fits. Logs and errors page with their cursor.
/// </summary>
public sealed class McpResultLimiterTests
{
    private static readonly McpBound Anything = new(new StatsRequest(), new CliGlobalOptions(true, "s1", CliGlobalOptions.DefaultTimeout));

    private static JsonElement Run(McpShape shape, JsonNode data, McpBound? bound = null)
    {
        using var document = JsonDocument.Parse(data.ToJsonString());
        var result = McpResultLimiter.Apply(shape, bound ?? Anything, CliResult.Success("s1", document.RootElement.Clone(), null));
        var json = CliOutput.ToJson(result);
        json.Length.ShouldBeLessThanOrEqualTo(McpResultLimiter.MaxResultCharacters + 200);
        using var parsed = JsonDocument.Parse(json);
        return parsed.RootElement.Clone();
    }

    [Fact]
    public void SmallResults_AreNotTouched()
    {
        var data = new JsonObject { ["lines"] = new JsonArray("a", "b"), ["frame"] = 3 };

        var result = Run(McpShape.Screenshot, data);

        result.TryGetProperty("truncated", out _).ShouldBeFalse();
        result.GetProperty("lines").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void Screenshot_CutsCellRowsFromTheBottom_AndSaysSo()
    {
        var cells = new JsonArray();
        for (var row = 0; row < 60; row++)
        {
            var rowCells = new JsonArray();
            for (var column = 0; column < 200; column++)
            {
                rowCells.Add((JsonNode?)new JsonObject { ["text"] = "x", ["fg"] = "#aabbcc", ["bg"] = "#001122", ["decoration"] = new JsonArray("bold") });
            }

            cells.Add((JsonNode?)rowCells);
        }

        var result = Run(McpShape.Screenshot, new JsonObject { ["lines"] = new JsonArray("top"), ["cells"] = cells });

        var kept = result.GetProperty("cells").GetArrayLength();
        kept.ShouldBeInRange(1, 59);
        var mark = result.GetProperty("truncated").EnumerateArray().Single();
        mark.GetProperty("field").GetString().ShouldBe("cells");
        mark.GetProperty("kept").GetInt32().ShouldBe(kept);
        mark.GetProperty("total").GetInt32().ShouldBe(60);
        mark.GetProperty("hint").GetString()!.ShouldContain("region");
    }

    [Fact]
    public void Frames_KeepTheNewest()
    {
        var frames = new JsonArray();
        for (var number = 1; number <= 50; number++)
        {
            frames.Add((JsonNode?)new JsonObject { ["number"] = number, ["lines"] = new JsonArray(new string('x', 3000)) });
        }

        var result = Run(McpShape.Frames, new JsonObject { ["frames"] = frames });

        var kept = result.GetProperty("frames").EnumerateArray().Select(static frame => frame.GetProperty("number").GetInt32()).ToList();
        kept.ShouldNotBeEmpty();
        kept.Count.ShouldBeLessThan(50);
        kept[^1].ShouldBe(50);
        kept.ShouldBe(Enumerable.Range(50 - kept.Count + 1, kept.Count));
        result.GetProperty("truncated").EnumerateArray().Single().GetProperty("total").GetInt32().ShouldBe(50);
    }

    [Fact]
    public void Tree_IsCutByDepth_StaysAValidTree_AndShowsChildCounts()
    {
        JsonObject Node(int depth, string path)
        {
            var node = new JsonObject { ["path"] = path, ["kind"] = "element", ["tag"] = "div", ["text"] = new string('t', 200) };
            if (depth > 0)
            {
                var children = new JsonArray();
                for (var i = 0; i < 8; i++)
                {
                    children.Add((JsonNode?)Node(depth - 1, $"{path}/{i}"));
                }

                node["children"] = children;
            }

            return node;
        }

        // 8^5 leaves: far more than fits.
        var result = Run(McpShape.Tree, new JsonObject { ["root"] = Node(5, ""), ["frame"] = 9 });

        var mark = result.GetProperty("truncated").EnumerateArray().Single();
        mark.GetProperty("field").GetString().ShouldBe("root");
        mark.GetProperty("hint").GetString()!.ShouldContain("selector");
        var root = result.GetProperty("root");
        var leaves = new List<JsonElement>();
        void Walk(JsonElement node)
        {
            if (node.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    Walk(child);
                }
            }
            else
            {
                leaves.Add(node);
            }
        }

        Walk(root);
        leaves.ShouldNotBeEmpty();
        leaves.ShouldAllBe(leaf => leaf.GetProperty("childCount").GetInt32() == 8);
    }

    private static JsonObject WideTree(int depth, string path = "")
    {
        var node = new JsonObject { ["path"] = path, ["kind"] = "element", ["tag"] = "div", ["text"] = new string('t', 100) };
        if (depth > 0)
        {
            var children = new JsonArray();
            for (var i = 0; i < 6; i++)
            {
                children.Add((JsonNode?)WideTree(depth - 1, $"{path}/{i}"));
            }

            node["children"] = children;
        }

        return node;
    }

    [Fact]
    public void Tree_WithoutADepth_IsCutToTheSmallDefaultBudget_AndTheHintSaysHowToGetMore()
    {
        var data = new JsonObject { ["root"] = WideTree(5), ["frame"] = 1 };
        var bound = Anything with { TreeBudget = McpResultLimiter.DefaultTreeBudget };

        var result = Run(McpShape.Tree, data, bound);

        result.GetRawText().Length.ShouldBeLessThanOrEqualTo(McpResultLimiter.DefaultTreeBudget + 300);
        var hint = result.GetProperty("truncated").EnumerateArray().Single().GetProperty("hint").GetString()!;
        hint.ShouldContain("selector");
        hint.ShouldContain("depth");
    }

    [Fact]
    public void Tree_WithAnExplicitDepth_MayUseMoreThanTheDefaultBudget_UpToTheHardCap()
    {
        var data = new JsonObject { ["root"] = WideTree(6), ["frame"] = 1 };

        var small = Run(McpShape.Tree, data, Anything with { TreeBudget = McpResultLimiter.DefaultTreeBudget });
        var large = Run(McpShape.Tree, data, Anything with { TreeBudget = McpResultLimiter.MaxResultCharacters });

        large.GetRawText().Length.ShouldBeGreaterThan(small.GetRawText().Length);
        large.GetRawText().Length.ShouldBeLessThanOrEqualTo(McpResultLimiter.MaxResultCharacters + 300);
        large.GetProperty("truncated").EnumerateArray().Single().GetProperty("field").GetString().ShouldBe("root");
    }

    [Fact]
    public void Tree_ThatFitsTheBudget_IsNotTouched()
    {
        var result = Run(McpShape.Tree, new JsonObject { ["root"] = WideTree(2), ["frame"] = 1 }, Anything with { TreeBudget = McpResultLimiter.DefaultTreeBudget });

        result.TryGetProperty("truncated", out _).ShouldBeFalse();
    }

    [Fact]
    public void Find_ReturnsAtMost100Matches_AndTheTotal()
    {
        var matches = new JsonArray();
        for (var i = 0; i < 150; i++)
        {
            matches.Add((JsonNode?)new JsonObject { ["path"] = $"/{i}" });
        }

        var result = Run(McpShape.Find, new JsonObject { ["matches"] = matches });

        result.GetProperty("matches").GetArrayLength().ShouldBe(100);
        var mark = result.GetProperty("truncated").EnumerateArray().Single();
        (mark.GetProperty("kept").GetInt32(), mark.GetProperty("total").GetInt32()).ShouldBe((100, 150));
    }

    [Fact]
    public void Output_SaysWhenMoreMayFollow_AndWhereToContinue()
    {
        var full = Run(McpShape.Output, new JsonObject { ["offset"] = 0, ["next"] = 1000, ["data"] = new string('a', 1000) }, Anything with { MaxBytes = 1000 });
        var mark = full.GetProperty("truncated").EnumerateArray().Single();
        mark.GetProperty("field").GetString().ShouldBe("data");
        mark.GetProperty("hint").GetString()!.ShouldContain("since=1000");

        var shortOutput = Run(McpShape.Output, new JsonObject { ["offset"] = 0, ["next"] = 10, ["data"] = "aaaaaaaaaa" }, Anything with { MaxBytes = 1000 });
        shortOutput.TryGetProperty("truncated", out _).ShouldBeFalse();
    }

    [Fact]
    public void Logs_AreLimited_WithTheCursorMovedBackToTheLastKeptEntry_AndLongTextCut()
    {
        var entries = new JsonArray();
        for (var seq = 1; seq <= 250; seq++)
        {
            entries.Add((JsonNode?)new JsonObject { ["seq"] = seq, ["level"] = "Information", ["message"] = seq == 1 ? new string('m', 5000) : $"entry {seq}" });
        }

        var result = Run(McpShape.Logs, new JsonObject { ["entries"] = entries, ["next"] = 251 }, Anything with { Limit = 100 });

        result.GetProperty("entries").GetArrayLength().ShouldBe(100);
        result.GetProperty("next").GetInt64().ShouldBe(101);
        var message = result.GetProperty("entries")[0].GetProperty("message").GetString()!;
        message.Length.ShouldBeLessThan(2100);
        message.ShouldEndWith("…[+3000 chars]");
        var mark = result.GetProperty("truncated").EnumerateArray().Single();
        (mark.GetProperty("kept").GetInt32(), mark.GetProperty("total").GetInt32()).ShouldBe((100, 250));
        mark.GetProperty("hint").GetString()!.ShouldContain("since=101");
    }

    [Fact]
    public async Task Logs_PageThroughTheTool_WithoutLosingOrRepeatingAnEntry()
    {
        // The host's behavior: entries from `since` (inclusive), and next = the last one read + 1.
        var all = Enumerable.Range(1, 250).ToList();
        var operations = new FakeOperations((request, _, _) =>
        {
            var since = ((LogsRequest)request).Since ?? 1;
            var read = all.Where(seq => seq >= since).ToList();
            var entries = new JsonArray();
            foreach (var seq in read)
            {
                entries.Add((JsonNode?)new JsonObject { ["seq"] = seq, ["level"] = "Information", ["message"] = $"entry {seq}" });
            }

            var data = new JsonObject { ["entries"] = entries, ["next"] = read.Count > 0 ? read[^1] + 1 : since };
            return Task.FromResult(CliResult.Success("s1", JsonDocument.Parse(data.ToJsonString()).RootElement.Clone(), null));
        });
        await using var server = await McpTestServer.StartAsync(operations);

        var seen = new List<int>();
        long? since = null;
        for (var page = 0; page < 10; page++)
        {
            var body = McpTestServer.Body(await server.CallAsync("get_logs", since is null ? """{"session":"s1","limit":100}""" : $$"""{"session":"s1","limit":100,"since":{{since}}}"""));
            var entries = body.GetProperty("entries").EnumerateArray().Select(static entry => entry.GetProperty("seq").GetInt32()).ToList();
            if (entries.Count == 0)
            {
                break;
            }

            seen.AddRange(entries);
            since = body.GetProperty("next").GetInt64();
        }

        seen.ShouldBe(all);
    }

    [Fact]
    public void Errors_UseTheSameLimitAndCursor()
    {
        var errors = new JsonArray();
        for (var seq = 1; seq <= 5; seq++)
        {
            errors.Add((JsonNode?)new JsonObject { ["seq"] = seq, ["message"] = "boom", ["details"] = new string('d', 6000) });
        }

        var result = Run(McpShape.Errors, new JsonObject { ["errors"] = errors, ["next"] = 6 }, Anything with { Limit = 2 });

        result.GetProperty("errors").GetArrayLength().ShouldBe(2);
        result.GetProperty("next").GetInt64().ShouldBe(3);
        result.GetProperty("errors")[0].GetProperty("details").GetString()!.Length.ShouldBeLessThan(4100);
    }

    [Fact]
    public void AnythingStillTooBig_IsReplacedByAValidResult_ThatSaysWhat()
    {
        var result = Run(McpShape.Plain, new JsonObject { ["blob"] = new string('z', 300_000) });

        result.TryGetProperty("blob", out _).ShouldBeFalse();
        var mark = result.GetProperty("truncated").EnumerateArray().Single();
        mark.GetProperty("field").GetString().ShouldBe("result");
        mark.GetProperty("total").GetInt32().ShouldBeGreaterThan(300_000);
    }

    [Fact]
    public void Failures_AreNeverCut()
    {
        var failure = CliResult.Failure("timeout", new string('x', 200_000), session: "s1");

        McpResultLimiter.Apply(McpShape.Plain, Anything, failure).ShouldBeSameAs(failure);
    }
}
