// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.CompilerServices;
using LLMAgentTUI.Components;
using LLMAgentTUI.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RazorConsole.Core.Input;
using RazorConsole.Tests.Integration.Infrastructure;

namespace RazorConsole.Tests.Integration;

public sealed class ChatClientAgentIntegrationTests
{
    [Theory]
    [InlineData(ToolPermission.AskForApproval, "read", false)]
    [InlineData(ToolPermission.WorkspaceFiles, "read", true)]
    [InlineData(ToolPermission.WorkspaceFiles, "bash", false)]
    [InlineData(ToolPermission.FullAccess, "bash", true)]
    [InlineData(ToolPermission.FullAccess, "unknown", false)]
    public async Task Permissions_ControlNativeApproval(ToolPermission permission, string toolName, bool automatic)
    {
        var executions = 0;
        using var client = new TestChatClient(
            new(ChatRole.Assistant, [new FunctionCallContent("one", toolName, new Dictionary<string, object?>())]),
            new(ChatRole.Assistant, "Finished."));
        var tool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => ++executions, toolName));
        await using var controller = new ChatClientAgentController(new ChatClientAgent(client, tools: [tool]), "test", "/workspace");
        controller.TrySetPermission(permission).ShouldBeTrue();
        await controller.SubmitAsync("execute");
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        executions.ShouldBe(automatic ? 1 : 0);
        controller.AwaitingApproval.ShouldBe(!automatic);
        if (!automatic)
        {
            controller.TrySetPermission(ToolPermission.FullAccess).ShouldBeFalse();
            controller.Cancel();
        }
        else
        {
            controller.Status.ShouldBe("Ready", string.Join('\n', controller.Transcript));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeApproval_ControlsRealFunctionExecutionAndUpdatesTui(bool approve)
    {
        var executions = 0;
        using var client = new TestChatClient(
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "inspect", new Dictionary<string, object?>())]),
            new(ChatRole.Assistant, [new TextContent("All "), new TextContent("done."), new UsageContent(new() { InputTokenCount = 12, OutputTokenCount = 3 })]),
            new(ChatRole.Assistant, "Follow-up."));
        var function = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => { executions++; return "file contents"; }, "inspect"));
        var agent = new ChatClientAgent(client, name: "Test agent", tools: [function]);
        await using var controller = new ChatClientAgentController(agent, "test-model", "/workspace");
        var view = new AgentViewState();
        await using var terminal = await TestTerminal.StartAsync<App>(100, 40,
            new Dictionary<string, object?> { [nameof(App.Session)] = controller, [nameof(App.ViewState)] = view },
            TestContext.Current.CancellationToken);
        view.Draft.SetText("Inspect a file");
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        controller.AwaitingApproval.ShouldBeTrue();
        executions.ShouldBe(0);
        controller.ApprovalDescription.ShouldContain("inspect");
        var hook = approve ? "approve-tool" : "deny-tool";
        var approvalFrame = await terminal.WaitUntilAsync(s => s.Layouts.ContainsKey(hook), cancellationToken: TestContext.Current.CancellationToken);
        var button = approvalFrame.Layouts[hook];
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, button.Left!.Value, button.Top!.Value), TestContext.Current.CancellationToken);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        executions.ShouldBe(approve ? 1 : 0);
        controller.AwaitingApproval.ShouldBeFalse();
        controller.Status.ShouldBe("Ready", string.Join('\n', controller.Transcript));
        controller.Transcript.ShouldContain(e => e.Kind == AgentEntryKind.Assistant && e.Text == "All done.");
        controller.GetUsage("Overview", new Dictionary<string, int>()).Rows.ShouldContain(new UsageMetric("Input tokens", 12));
        if (approve)
        {
            controller.Transcript.ShouldContain(e => e.Kind == AgentEntryKind.Tool && e.Detail!.Contains("file contents"));
        }
        await controller.SubmitAsync("Follow up");
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        client.Requests.Last().ShouldContain(m => m.Role == ChatRole.User && m.Text == "Inspect a file");
        client.Requests.Last().ShouldContain(m => m.Role == ChatRole.Assistant && m.Text.Contains("All done."));
    }

    [Fact]
    public async Task Streaming_DoesNotBlockInput_AndEscapeCancelsNativeRun()
    {
        using var client = new TestChatClient(new ChatMessage(ChatRole.Assistant, "partial")) { BlockAfterContent = true };
        await using var controller = new ChatClientAgentController(new ChatClientAgent(client), "test-model", "/workspace");
        var view = new AgentViewState();
        await using var terminal = await TestTerminal.StartAsync<App>(100, 40,
            new Dictionary<string, object?> { [nameof(App.Session)] = controller, [nameof(App.ViewState)] = view },
            TestContext.Current.CancellationToken);
        view.Draft.SetText("Start streaming");
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await client.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        controller.Running.ShouldBeTrue();
        controller.Transcript.ShouldContain(e => e.Text == "partial");
        await terminal.SendKeyAsync(ConsoleKey.N, 'n', cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        view.Draft.Text.ShouldBe("n");
        await terminal.SendKeyAsync(ConsoleKey.Escape, cancellationToken: TestContext.Current.CancellationToken);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        controller.Status.ShouldBe("Interrupted");
        controller.Running.ShouldBeFalse();
    }

    [Fact]
    public async Task MultipleApprovals_AreDecidedIndividually_AndOnlyApprovedCallRuns()
    {
        var executed = new List<string>();
        using var client = new TestChatClient(
            new(ChatRole.Assistant, [
                new FunctionCallContent("one", "inspect", new Dictionary<string, object?> { ["path"] = "one" }),
                new FunctionCallContent("two", "inspect", new Dictionary<string, object?> { ["path"] = "two" })]),
            new(ChatRole.Assistant, "Finished."));
        var tool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create((string path) => { executed.Add(path); return path; }, "inspect"));
        await using var controller = new ChatClientAgentController(new ChatClientAgent(client, tools: [tool]), "test", "/workspace");
        await controller.SubmitAsync("inspect");
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        controller.Approve(true);
        controller.AwaitingApproval.ShouldBeTrue();
        controller.ApprovalDescription.ShouldContain("two");
        executed.ShouldBeEmpty();
        controller.Approve(false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        executed.ShouldBe(["one"]);
        controller.Status.ShouldBe("Ready");
    }

    [Fact]
    public async Task CancelPendingApproval_DoesNotRunToolOnNextTurn()
    {
        var executions = 0;
        using var client = new TestChatClient(
            new(ChatRole.Assistant, [new FunctionCallContent("one", "inspect", new Dictionary<string, object?>())]),
            new(ChatRole.Assistant, "Cancelled."));
        var tool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => ++executions, "inspect"));
        await using var controller = new ChatClientAgentController(new ChatClientAgent(client, tools: [tool]), "test", "/workspace");
        await controller.SubmitAsync("inspect");
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        controller.Cancel();
        controller.AwaitingApproval.ShouldBeFalse();
        await controller.SubmitAsync("Do not inspect");
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        executions.ShouldBe(0);
        controller.Status.ShouldBe("Ready", string.Join('\n', controller.Transcript));
    }

    [Fact]
    public async Task ProviderFailure_IsVisibleAndAllowsAnotherTurn()
    {
        using var client = new TestChatClient();
        await using var controller = new ChatClientAgentController(new ChatClientAgent(client), "test", "/workspace");
        await controller.SubmitAsync("first");
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        controller.Status.ShouldBe("Failed");
        controller.Transcript.ShouldContain(entry => entry.Kind == AgentEntryKind.Error);
        await controller.SubmitAsync("retry");
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        controller.Transcript.Count(entry => entry.Kind == AgentEntryKind.User).ShouldBe(2);
        controller.Running.ShouldBeFalse();
    }

    private sealed class TestChatClient(params ChatMessage[] responses) : IChatClient
    {
        private readonly Queue<ChatMessage> _responses = new(responses);
        public List<List<ChatMessage>> Requests { get; } = [];
        public bool BlockAfterContent { get; init; }
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            var response = _responses.Dequeue();
            var id = Guid.NewGuid().ToString();
            foreach (var content in response.Contents)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return new() { Role = response.Role, MessageId = id, ResponseId = id, Contents = [content] };
            }
            if (BlockAfterContent)
            {
                Blocked.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            yield return new() { Role = response.Role, MessageId = id, ResponseId = id, FinishReason = response.Contents.OfType<FunctionCallContent>().Any() ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop };
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Tests exercise streaming only.");
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
