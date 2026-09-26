// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace LLMAgentTUI.Services;

/// <summary>Preserves DeepSeek reasoning on assistant messages sent back after tool calls.</summary>
public sealed class DeepSeekChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => base.GetResponseAsync(WithReasoning(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(WithReasoning(messages), options, cancellationToken);

    private static IEnumerable<ChatMessage> WithReasoning(IEnumerable<ChatMessage> messages)
    {
        var history = messages.ToArray();
        var approvalReasoning = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in history)
        {
            var reasoning = string.Concat(entry.Contents.OfType<TextReasoningContent>().Select(content => content.Text));
            if (reasoning.Length == 0)
            {
                continue;
            }
            foreach (var approval in entry.Contents.OfType<ToolApprovalRequestContent>())
            {
                if (approval.ToolCall is FunctionCallContent call)
                {
                    approvalReasoning[call.CallId] = reasoning;
                }
            }
        }
        foreach (var message in JoinAssistantMessages(history))
        {
            var reasoning = string.Concat(message.Contents.OfType<TextReasoningContent>().Select(content => content.Text));
            if (reasoning.Length == 0 && message.Role == ChatRole.Assistant)
            {
                // Cancellation may place a real user turn between the original
                // approval and its denied tool call. Recover only by exact call ID.
                reasoning = string.Concat(message.Contents.OfType<FunctionCallContent>()
                    .Where(call => approvalReasoning.ContainsKey(call.CallId))
                    .Select(call => approvalReasoning[call.CallId]).Distinct(StringComparer.Ordinal));
            }
            if (message.Role != ChatRole.Assistant || reasoning.Length == 0)
            {
                yield return message;
                continue;
            }
            // MEAI 10.10 parses reasoning_content on receive, but does not serialize
            // TextReasoningContent in Chat Completions requests. Keep its standard
            // mapping of text/tool calls, then add the provider-specific field.
            var copy = message.Clone();
            copy.RawRepresentation = null;
            var raw = new[] { copy }.AsOpenAIChatMessages().OfType<AssistantChatMessage>().Single();
#pragma warning disable SCME0001 // JsonPatch is required for this provider extension.
            raw.Patch.Set("$.reasoning_content"u8, reasoning);
#pragma warning restore SCME0001
            copy.RawRepresentation = raw;
            yield return copy;
        }
    }

    private static IEnumerable<ChatMessage> JoinAssistantMessages(IEnumerable<ChatMessage> messages)
    {
        // Native tool approval splits the original assistant response into its
        // reasoning/text and, after approval, its tool calls. DeepSeek requires
        // those calls and their original reasoning in the same wire message.
        ChatMessage? pending = null;
        foreach (var message in messages)
        {
            // Approval responses are framework control data, not a new user
            // turn. They are already consumed before reaching this client.
            if (message.Contents.Count > 0 && message.Contents.All(content => content is ToolApprovalResponseContent))
            {
                continue;
            }
            if (message.Role == ChatRole.Assistant)
            {
                if (pending is null)
                {
                    pending = message.Clone();
                    pending.Contents = [.. message.Contents];
                    pending.RawRepresentation = null;
                }
                else
                {
                    foreach (var content in message.Contents)
                    {
                        pending.Contents.Add(content);
                    }
                }
            }
            else
            {
                if (pending is not null)
                {
                    yield return pending;
                    pending = null;
                }
                yield return message;
            }
        }
        if (pending is not null)
        {
            yield return pending;
        }
    }
}
