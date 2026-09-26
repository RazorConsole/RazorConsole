// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FrameworkSession = Microsoft.Agents.AI.AgentSession;

namespace LLMAgentTUI.Services;

/// <summary>Projects native ChatClientAgent streaming content and approvals into TUI state.</summary>
public sealed class ChatClientAgentController(ChatClientAgent agent, string model, string workingDirectory) : AgentSession, IAsyncDisposable
{
    private FrameworkSession? _conversation;
    private CancellationTokenSource? _runCancellation;
    private Task _completion = Task.CompletedTask;
    private readonly List<ToolApprovalRequestContent> _approvals = [];
    private readonly List<AIContent> _decisions = [];
    private readonly Dictionary<string, int> _toolEntries = [];
    private bool _disposed;
    private long _inputTokens;
    private long _outputTokens;

    public ChatClientAgent Agent => agent;
    public override string Model => model;
    public override string WorkingDirectory => workingDirectory;
    public override Task Completion => _completion;
    public override IReadOnlyList<string> Notifications => [$"Tool permissions: {Permission}. Shell commands run as your user, without an OS sandbox."];
    public override string ApprovalDescription
    {
        get
        {
            lock (Entries)
            {
                return _approvals.FirstOrDefault()?.ToolCall is FunctionCallContent call
                    ? TerminalText.Safe($"{call.Name}\n{JsonSerializer.Serialize(call.Arguments, new JsonSerializerOptions { WriteIndented = true })}") : "Tool execution";
            }
        }
    }

    // The input event must return promptly so Escape and approval input remain usable.
    public override Task SubmitAsync(string text)
    {
        lock (Entries)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Running || AwaitingApproval || string.IsNullOrWhiteSpace(text))
            {
                return Task.CompletedTask;
            }
            Entries.Add(new(AgentEntryKind.User, text));
            var messages = TakeDecisions();
            messages.Add(new(ChatRole.User, text));
            Start(messages);
        }
        return Task.CompletedTask;
    }

    public override void Approve(bool approved)
    {
        lock (Entries)
        {
            if (Running || _approvals.Count == 0 || _disposed)
            {
                return;
            }
            var request = _approvals[0];
            _approvals.RemoveAt(0);
            _decisions.Add(request.CreateResponse(approved, approved ? "Approved in LLMAgentTUI" : "Declined in LLMAgentTUI"));
            AwaitingApproval = _approvals.Count > 0;
            if (!AwaitingApproval)
            {
                Start(TakeDecisions());
            }
            else
            {
                Notify();
            }
        }
    }

    public override void Cancel()
    {
        lock (Entries)
        {
            _runCancellation?.Cancel();
            RejectPendingApprovals();
            Status = "Interrupted";
            Notify();
        }
    }

    private void RejectPendingApprovals()
    {
        foreach (var request in _approvals)
        {
            _decisions.Add(request.CreateResponse(false, "User cancelled"));
        }
        _approvals.Clear();
        AwaitingApproval = false;
    }

    private List<ChatMessage> TakeDecisions()
    {
        var messages = new List<ChatMessage>();
        if (_decisions.Count > 0)
        {
            messages.Add(new(ChatRole.User, _decisions.ToList()));
            _decisions.Clear();
        }
        return messages;
    }

    private void Start(List<ChatMessage> messages)
    {
        _runCancellation?.Dispose();
        _runCancellation = new();
        var cancellationToken = _runCancellation.Token;
        Running = true;
        Status = "Working";
        _completion = Task.Run(() => RunAsync(messages, cancellationToken), CancellationToken.None);
        Notify();
    }

    private async Task RunAsync(List<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var assistantEntry = -1;
        string? messageId = null;
        try
        {
            _conversation ??= await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                await foreach (var update in agent.RunStreamingAsync(messages, _conversation, cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (Entries)
                    {
                        foreach (var content in update.Contents)
                        {
                            switch (content)
                            {
                                case TextContent text when !string.IsNullOrEmpty(text.Text):
                                    if (assistantEntry < 0 || messageId != update.MessageId)
                                    {
                                        assistantEntry = Entries.Count;
                                        messageId = update.MessageId;
                                        Entries.Add(new(AgentEntryKind.Assistant, ""));
                                    }
                                    Entries[assistantEntry] = Entries[assistantEntry] with { Text = Entries[assistantEntry].Text + text.Text };
                                    Status = "Responding";
                                    break;
                                case ToolApprovalRequestContent request:
                                    _approvals.Add(request);
                                    assistantEntry = -1;
                                    break;
                                case FunctionCallContent call:
                                    assistantEntry = -1;
                                    if (!_toolEntries.ContainsKey(call.CallId))
                                    {
                                        _toolEntries[call.CallId] = Entries.Count;
                                        var label = call.Arguments?.TryGetValue("command", out var command) == true ? command?.ToString() : call.Name;
                                        Entries.Add(new(AgentEntryKind.Tool, $"Running {label}", JsonSerializer.Serialize(call.Arguments)));
                                    }
                                    Status = $"Running {call.Name}";
                                    break;
                                case FunctionResultContent result:
                                    if (_toolEntries.TryGetValue(result.CallId, out var index))
                                    {
                                        Entries[index] = Entries[index] with
                                        {
                                            Text = (result.Exception is null ? "Ran " : "Failed ") + Entries[index].Text["Running ".Length..],
                                            Detail = result.Exception?.Message ?? result.Result?.ToString() ?? "(no output)",
                                        };
                                    }
                                    break;
                                case UsageContent usage:
                                    _inputTokens += usage.Details.InputTokenCount ?? 0;
                                    _outputTokens += usage.Details.OutputTokenCount ?? 0;
                                    break;
                            }
                        }
                        Notify();
                    }
                }
                lock (Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var request in _approvals.ToArray())
                    {
                        if (request.ToolCall is FunctionCallContent call && ToolPermissionPolicy.CanAutoApprove(Permission, call.Name))
                        {
                            _decisions.Add(request.CreateResponse(true, $"Session permission: {Permission}"));
                            _approvals.Remove(request);
                        }
                    }
                    AwaitingApproval = _approvals.Count > 0;
                    Status = AwaitingApproval ? "Approval required" : "Ready";
                    if (!AwaitingApproval && _decisions.Count > 0)
                    {
                        messages = TakeDecisions();
                        assistantEntry = -1;
                        Status = "Working";
                        continue;
                    }
                }
                break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (Entries)
            {
                RejectPendingApprovals();
                Status = "Interrupted";
                MarkUnfinishedTools("Interrupted");
            }
        }
        catch (Exception exception)
        {
            lock (Entries)
            {
                RejectPendingApprovals();
                Entries.Add(new(AgentEntryKind.Error, exception.Message));
                Status = "Failed";
                MarkUnfinishedTools("Failed");
            }
        }
        finally
        {
            lock (Entries)
            {
                Running = false;
                Notify();
            }
        }
    }

    public override UsageReport GetUsage(string section, IReadOnlyDictionary<string, int> filters)
    {
        lock (Entries)
        {
            return new([new("Input tokens", (int)Math.Min(int.MaxValue, _inputTokens)), new("Output tokens", (int)Math.Min(int.MaxValue, _outputTokens))],
                "Provider-reported totals for this session; unavailable usage is not estimated.");
        }
    }

    private void MarkUnfinishedTools(string status)
    {
        foreach (var index in _toolEntries.Values)
        {
            if (Entries[index].Text.StartsWith("Running ", StringComparison.Ordinal))
            {
                Entries[index] = Entries[index] with { Text = status + " " + Entries[index].Text[8..] };
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (Entries)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _runCancellation?.Cancel();
        }
        await _completion.ConfigureAwait(false);
        _runCancellation?.Dispose();
    }
}
