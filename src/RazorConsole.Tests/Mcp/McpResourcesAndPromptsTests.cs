// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using RazorConsole.Cli.Mcp;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;

namespace RazorConsole.Tests.Mcp;

/// <summary>
/// The resources (session list, screen and tree of a session) and the two prompts.
/// </summary>
public sealed partial class McpResourcesAndPromptsTests
{
    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static CliResult Answer(CliRequest request) => request switch
    {
        ListRequest => CliResult.Success(null, Json("""{"sessions":[{"name":"s1","kind":"pty"},{"name":"a1","kind":"attached"}]}"""), null),
        ScreenRequest => CliResult.Success("s1", Json("""{"source":"pty","lines":["Hello   ","  World  ",""],"frame":4}"""), null),
        TreeRequest => CliResult.Success("s1", Json("""{"root":{"path":"","kind":"element","tag":"div"},"frame":4}"""), null),
        _ => CliResult.Failure("internal", "unexpected"),
    };

    [Fact]
    public async Task ResourceList_HasTheSessionsAndThePerSessionScreenAndTree()
    {
        await using var server = await McpTestServer.StartAsync(Answer);

        var resources = await server.Client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var templates = await server.Client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

        resources.Select(static resource => resource.Uri).ShouldBe(
        [
            "razorconsole://sessions",
            "razorconsole://session/s1/screen",
            "razorconsole://session/s1/tree",
            "razorconsole://session/a1/screen",
            "razorconsole://session/a1/tree",
        ]);
        templates.Select(static template => template.UriTemplate).ShouldBe(
        [
            "razorconsole://session/{name}/screen",
            "razorconsole://session/{name}/tree",
        ]);
    }

    [Fact]
    public async Task ResourceList_StillListsTheSessions_WhenTheHostCannotBeReached()
    {
        var operations = new FakeOperations((_, _, _) => throw new IOException("no host"));
        await using var server = await McpTestServer.StartAsync(operations);

        var resources = await server.Client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        resources.Select(static resource => resource.Uri).ShouldBe(["razorconsole://sessions"]);
    }

    [Fact]
    public async Task ReadSessions_IsTheListJson()
    {
        await using var server = await McpTestServer.StartAsync(Answer);

        var read = await server.Client.ReadResourceAsync("razorconsole://sessions", cancellationToken: TestContext.Current.CancellationToken);

        var contents = read.Contents.OfType<TextResourceContents>().Single();
        contents.MimeType.ShouldBe("application/json");
        contents.Text.ShouldBe(CliOutput.ToJson(Answer(new ListRequest())));
    }

    [Fact]
    public async Task ReadScreen_IsTheTextOfThatSession_WithTrailingSpacesTrimmed()
    {
        await using var server = await McpTestServer.StartAsync(Answer);

        var read = await server.Client.ReadResourceAsync("razorconsole://session/s1/screen", cancellationToken: TestContext.Current.CancellationToken);

        var contents = read.Contents.OfType<TextResourceContents>().Single();
        contents.MimeType.ShouldBe("text/plain");
        contents.Text.ShouldBe("Hello\n  World\n");
        var (request, options) = server.Operations.Requests.Single();
        request.ShouldBe(new ScreenRequest(ScreenFormat.Text, null));
        options.Session.ShouldBe("s1");
    }

    [Fact]
    public async Task ReadTree_IsTheTreeJsonOfThatSession()
    {
        await using var server = await McpTestServer.StartAsync(Answer);

        var read = await server.Client.ReadResourceAsync("razorconsole://session/s1/tree", cancellationToken: TestContext.Current.CancellationToken);

        var text = read.Contents.OfType<TextResourceContents>().Single().Text;
        Json(text).GetProperty("root").GetProperty("tag").GetString().ShouldBe("div");
        server.Operations.Requests.Single().Request.ShouldBe(new TreeRequest(null, McpResultLimiter.DefaultTreeDepth, true, true));
    }

    [Theory]
    [InlineData("razorconsole://nothing")]
    [InlineData("razorconsole://session/s1/video")]
    [InlineData("razorconsole://session/../evil/screen")]
    [InlineData("https://example.com/")]
    public async Task ReadUnknownResource_IsAResourceNotFoundError(string uri)
    {
        await using var server = await McpTestServer.StartAsync(Answer);

        var exception = await Should.ThrowAsync<McpProtocolException>(async () =>
            await server.Client.ReadResourceAsync(uri, cancellationToken: TestContext.Current.CancellationToken));

        exception.ErrorCode.ShouldBe(McpErrorCode.ResourceNotFound);
    }

    [Fact]
    public async Task ReadResourceOfAMissingSession_KeepsTheCliErrorCodeInTheMessage()
    {
        await using var server = await McpTestServer.StartAsync(_ => CliResult.Failure("session_not_found", "There is no session named 'gone'."));

        var exception = await Should.ThrowAsync<McpProtocolException>(async () =>
            await server.Client.ReadResourceAsync("razorconsole://session/gone/screen", cancellationToken: TestContext.Current.CancellationToken));

        exception.ErrorCode.ShouldBe(McpErrorCode.ResourceNotFound);
        exception.Message.ShouldContain("[session_not_found]");
    }

    [Fact]
    public async Task Prompts_AreListed_WithAnOptionalSessionArgument()
    {
        await using var server = await McpTestServer.StartAsync(Answer);

        var prompts = await server.Client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        prompts.Select(static prompt => prompt.Name).ShouldBe(["check_keyboard_accessibility", "check_layout"]);
        foreach (var prompt in prompts)
        {
            var argument = prompt.ProtocolPrompt.Arguments!.Single();
            argument.Name.ShouldBe("session");
            argument.Required.ShouldNotBe(true);
        }
    }

    [Theory]
    [InlineData("check_keyboard_accessibility")]
    [InlineData("check_layout")]
    public async Task Prompts_TellTheAgentWhichToolsToUse_AndOnlyToolsThatExist(string name)
    {
        await using var server = await McpTestServer.StartAsync(Answer);

        var result = await server.Client.GetPromptAsync(name, cancellationToken: TestContext.Current.CancellationToken);

        var text = result.Messages.Single().Content.ShouldBeOfType<TextContentBlock>().Text;
        var tools = McpTools.All.Select(static tool => tool.Name).ToHashSet();
        var mentioned = ToolName().Matches(text).Select(static match => match.Value).Distinct().ToList();
        mentioned.ShouldNotBeEmpty();
        mentioned.ShouldAllBe(tool => tools.Contains(tool), "every tool a prompt names exists");
        text.ShouldContain("start_session");
        if (name == "check_keyboard_accessibility")
        {
            mentioned.ShouldBeSubsetOf(tools);
            mentioned.ShouldContain("get_focus");
            mentioned.ShouldContain("send_keys");
        }
        else
        {
            mentioned.ShouldContain("get_tree");
            mentioned.ShouldContain("resize_session");
        }
    }

    [Fact]
    public async Task PromptWithASession_NamesIt_AndAnInvalidOneIsRefused()
    {
        await using var server = await McpTestServer.StartAsync(Answer);

        var result = await server.Client.GetPromptAsync(
            "check_layout",
            new Dictionary<string, object?> { ["session"] = "demo.1" },
            cancellationToken: TestContext.Current.CancellationToken);
        result.Messages.Single().Content.ShouldBeOfType<TextContentBlock>().Text.ShouldContain("`demo.1`");

        var exception = await Should.ThrowAsync<McpProtocolException>(async () =>
            await server.Client.GetPromptAsync("check_layout", new Dictionary<string, object?> { ["session"] = "../x" }, cancellationToken: TestContext.Current.CancellationToken));
        exception.ErrorCode.ShouldBe(McpErrorCode.InvalidParams);

        var unknown = await Should.ThrowAsync<McpProtocolException>(async () =>
            await server.Client.GetPromptAsync("nope", cancellationToken: TestContext.Current.CancellationToken));
        unknown.ErrorCode.ShouldBe(McpErrorCode.InvalidParams);
    }

    // A snake_case word with at least one underscore: the tool names in a prompt.
    [GeneratedRegex("\\b[a-z]+(?:_[a-z]+)+\\b")]
    private static partial Regex ToolName();
}
