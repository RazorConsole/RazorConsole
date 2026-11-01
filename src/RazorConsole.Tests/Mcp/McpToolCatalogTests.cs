// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using RazorConsole.Cli.Mcp;
using RazorConsole.Cli.Operations;

namespace RazorConsole.Tests.Mcp;

/// <summary>
/// What an agent sees when it lists the server: the exact tools, their schemas, annotations and descriptions.
/// </summary>
public sealed class McpToolCatalogTests
{
    private static readonly string[] ExpectedTools =
    [
        // Sessions
        "start_session", "list_sessions", "session_info", "stop_session", "restart_session", "resize_session", "attach_app", "detach_session",
        // Input
        "send_keys", "type_text", "paste_text", "send_raw", "click", "mouse",
        // Screen
        "screenshot", "get_frames", "diff_frames", "get_output",
        // Wait and assert
        "wait_for", "assert",
        // App layer
        "get_tree", "find_elements", "inspect_element", "inspect_at", "get_focus", "set_focus", "get_route", "navigate", "get_errors", "get_logs", "get_stats",
        // Environment
        "doctor",
    ];

    private static readonly string[] Destructive = ["stop_session", "restart_session", "send_keys", "type_text", "paste_text", "send_raw", "click", "mouse", "navigate"];

    private static readonly string[] Changes = ["start_session", "resize_session", "attach_app", "detach_session", "set_focus", "doctor"];

    private static readonly string[] Unscoped = ["start_session", "list_sessions", "attach_app", "doctor"];

    [Fact]
    public async Task ToolsList_IsExactlyTheDocumentedCatalog()
    {
        await using var server = await McpTestServer.StartAsync(static _ => CliResult.Success(null, null, null));

        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(static tool => tool.Name).ShouldBe(ExpectedTools);
        McpTools.All.Select(static tool => tool.Name).ShouldBe(ExpectedTools);
    }

    [Fact]
    public void EverySchema_IsAnObjectThatRejectsUnknownArguments_AndRequiresOnlyItsOwnProperties()
    {
        foreach (var tool in McpTools.All)
        {
            var schema = tool.Schema.Build();
            schema.GetProperty("type").GetString().ShouldBe("object", tool.Name);
            schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse(tool.Name);

            var properties = schema.GetProperty("properties");
            foreach (var property in properties.EnumerateObject())
            {
                (property.Value.TryGetProperty("type", out _) || property.Value.TryGetProperty("oneOf", out _)).ShouldBeTrue($"{tool.Name}.{property.Name} has a type");
                property.Value.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace($"{tool.Name}.{property.Name} has a description");
            }

            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var name in required.EnumerateArray())
                {
                    properties.TryGetProperty(name.GetString()!, out _).ShouldBeTrue($"{tool.Name} requires {name}");
                }
            }
        }
    }

    [Fact]
    public void Annotations_MarkInspectionReadOnly_AndInputNavigateStopAndRestartDestructive()
    {
        foreach (var tool in McpTools.All)
        {
            var annotations = tool.ToProtocol().Annotations!;
            annotations.OpenWorldHint.ShouldBe(false, tool.Name);
            if (Destructive.Contains(tool.Name))
            {
                annotations.ReadOnlyHint.ShouldBe(false, tool.Name);
                annotations.DestructiveHint.ShouldBe(true, tool.Name);
            }
            else if (Changes.Contains(tool.Name))
            {
                annotations.ReadOnlyHint.ShouldBe(false, tool.Name);
                annotations.DestructiveHint.ShouldBe(false, tool.Name);
            }
            else
            {
                annotations.ReadOnlyHint.ShouldBe(true, tool.Name);
            }
        }

        Destructive.Concat(Changes).Concat(McpTools.All.Where(static tool => tool.Kind == McpToolKind.ReadOnly).Select(static tool => tool.Name))
            .Order().ShouldBe(ExpectedTools.Order());
    }

    [Fact]
    public void Descriptions_AreShort_AndEverySessionToolTellsTheAgentToPassSession()
    {
        foreach (var tool in McpTools.All)
        {
            tool.Description.ShouldNotBeNullOrWhiteSpace(tool.Name);
            tool.Description.Length.ShouldBeLessThan(900, tool.Name);
            tool.Description.ShouldNotContain("TODO", Case.Insensitive, tool.Name);

            var schema = tool.Schema;
            if (Unscoped.Contains(tool.Name))
            {
                schema.Has("session").ShouldBeFalse(tool.Name);
                tool.Description.ShouldNotContain("Always pass `session`", Case.Sensitive, tool.Name);
            }
            else
            {
                schema.Has("session").ShouldBeTrue(tool.Name);
                tool.Description.ShouldContain("Always pass `session`", Case.Sensitive, tool.Name);
            }

            schema.Has("timeout").ShouldBeTrue(tool.Name);
        }
    }

    [Fact]
    public void WaitAndAssert_AlwaysHaveATimeout_ThatIsBounded()
    {
        foreach (var name in new[] { "wait_for", "assert" })
        {
            var tool = McpTools.Find(name)!;
            tool.Schema.Has("timeout").ShouldBeTrue();
            tool.MaxTimeout.ShouldBe(TimeSpan.FromMinutes(5));

            var bound = tool.TryBind(Arguments("""{"text":"x"}"""), new CliGlobalOptions(false, null, CliGlobalOptions.DefaultTimeout), out _);
            bound!.Options.Timeout.ShouldBe(CliGlobalOptions.DefaultTimeout);
        }
    }

    [Fact]
    public void EveryToolThatTakesASelector_CallsItSelector_AndNoneCallsItElement()
    {
        string[] withSelector = ["send_keys", "click", "wait_for", "assert", "get_tree", "find_elements", "inspect_element"];
        foreach (var tool in McpTools.All)
        {
            tool.Schema.Has("element").ShouldBeFalse($"{tool.Name} must name a selector `selector`");
            tool.Schema.Has("selector").ShouldBe(withSelector.Contains(tool.Name), tool.Name);
        }
    }

    [Fact]
    public void EveryDuration_AcceptsAStringOrANumberOfSeconds_InTheSchemaAndTheDescription()
    {
        foreach (var tool in McpTools.All)
        {
            var properties = tool.Schema.Build().GetProperty("properties");
            var durations = new List<string> { "timeout" };
            if (tool.Name is "wait_for" or "assert")
            {
                durations.Add("idle");
            }

            foreach (var name in durations)
            {
                var property = properties.GetProperty(name);
                property.GetProperty("oneOf").EnumerateArray().Select(static option => option.GetProperty("type").GetString()).ShouldBe(["string", "number"], $"{tool.Name}.{name}");
                property.GetProperty("description").GetString()!.ShouldContain("A number is seconds (e.g. 30), not milliseconds; or a string with a unit: 500ms, 10s, 2m.", Case.Sensitive, $"{tool.Name}.{name}");
            }
        }
    }

    [Fact]
    public void GetTree_TellsTheAgentToStartSmall_AndToUseFindElements()
    {
        var description = McpTools.Find("get_tree")!.Description;

        description.ShouldContain("small `depth` or a `selector`");
        description.ShouldContain("find_elements");
    }

    [Fact]
    public async Task Server_SaysWhoItIs_AndTellsTheAgentToAlwaysPassSession()
    {
        await using var server = await McpTestServer.StartAsync(static _ => CliResult.Success(null, null, null));

        server.Client.ServerInfo.ShouldNotBeNull().Name.ShouldBe("razorconsole");
        server.Client.ServerInstructions.ShouldNotBeNull().ShouldContain("Always pass `session`");
        server.Client.ServerCapabilities.Tools.ShouldNotBeNull();
        server.Client.ServerCapabilities.Resources.ShouldNotBeNull();
        server.Client.ServerCapabilities.Prompts.ShouldNotBeNull();
    }

    internal static IDictionary<string, JsonElement> Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value.Clone());
    }
}
