// Copyright (c) RazorConsole. All rights reserved.

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using LLMAgentTUI.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace RazorConsole.Tests.Integration;

public sealed class DeepSeekChatClientTests
{
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public async Task RealSdk_StreamsReasoning_AndReplaysItAfterApprovalAndOnNextTurn(bool includeText, bool approve, bool cancel)
    {
        using var handler = new DeepSeekHandler(includeText);
        using var http = new HttpClient(handler);
        using var client = new DeepSeekChatClient(new OpenAIClient(new ApiKeyCredential("dummy"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://deepseek.invalid"),
            Transport = new HttpClientPipelineTransport(http),
        }).GetChatClient("deepseek-flash").AsIChatClient());
        var invoked = 0;
        var tool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => { invoked++; return "file.txt"; }, "list_files"));
        await using var controller = new ChatClientAgentController(new ChatClientAgent(client, tools: [tool]), "deepseek-flash", "/workspace");
        await controller.SubmitAsync("list files");
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        controller.AwaitingApproval.ShouldBeTrue(string.Join('\n', controller.Transcript));
        invoked.ShouldBe(0);
        if (cancel)
        {
            controller.Cancel();
            await controller.SubmitAsync("Continue without executing the tool");
        }
        else
        {
            controller.Approve(approve);
        }
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        invoked.ShouldBe(approve ? 1 : 0);
        controller.Status.ShouldBe("Ready", string.Join('\n', controller.Transcript));
        await controller.SubmitAsync("another turn");
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        handler.Requests.Count.ShouldBe(3);
        using var afterTool = JsonDocument.Parse(handler.Requests[1]);
        var assistant = afterTool.RootElement.GetProperty("messages").EnumerateArray().Single(m => m.GetProperty("role").GetString() == "assistant" && m.TryGetProperty("tool_calls", out _));
        assistant.TryGetProperty("reasoning_content", out var reasoning).ShouldBeTrue(handler.Requests[1]);
        reasoning.GetString().ShouldBe("inspect workspace");
        assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString().ShouldBe("call-1");
        using var nextTurn = JsonDocument.Parse(handler.Requests[2]);
        var history = nextTurn.RootElement.GetProperty("messages").EnumerateArray().Where(m => m.GetProperty("role").GetString() == "assistant").ToArray();
        history.Length.ShouldBe(cancel ? 3 : 2, handler.Requests[2]);
        history.Single(m => m.TryGetProperty("tool_calls", out _)).GetProperty("reasoning_content").GetString().ShouldBe("inspect workspace");
        history[^1].GetProperty("reasoning_content").GetString().ShouldBe("summarize result");
        controller.Transcript.ShouldNotContain(e => e.Text.Contains("inspect workspace"));
    }

    private sealed class DeepSeekHandler(bool includeText) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var turn = Requests.Count;
            string Chunk(object delta, string? finish = null) => "data: " + JsonSerializer.Serialize(new
            {
                id = $"response-{turn}",
                @object = "chat.completion.chunk",
                created = 1,
                model = "deepseek-flash",
                choices = new[] { new { index = 0, delta, finish_reason = finish } },
            }) + "\n\n";
            var body = Chunk(new { role = "assistant", reasoning_content = turn == 1 ? "inspect " : "summarize result" });
            if (turn == 1)
            {
                body += Chunk(new { reasoning_content = "workspace" });
                body += Chunk(new { content = includeText ? "I will list files." : null, tool_calls = new[] { new { index = 0, id = "call-1", type = "function", function = new { name = "list_files", arguments = "{}" } } } });
            }
            else
            {
                body += Chunk(new { content = "file.txt" });
            }
            body += Chunk(new { }, turn == 1 ? "tool_calls" : "stop") + "data: [DONE]\n\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        }
    }
}
