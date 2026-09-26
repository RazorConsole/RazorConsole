// Copyright (c) RazorConsole. All rights reserved.

namespace LLMAgentTUI.Services;

public enum AgentEntryKind { User, Assistant, Tool, Patch, Error }

public sealed record AgentEntry(AgentEntryKind Kind, string Text, string? Detail = null);
public sealed record UsageMetric(string Name, int Tokens);
public sealed record UsageReport(IReadOnlyList<UsageMetric> Rows, string? Notice = null);

/// <summary>The UI consumes session data, independently of its provider.</summary>
public abstract class AgentSession
{
    protected readonly List<AgentEntry> Entries = [];
    public event Action? Changed;
    public IReadOnlyList<AgentEntry> Transcript
    {
        get
        {
            lock (Entries)
            {
                return Entries.ToArray();
            }
        }
    }
    public bool Running { get; protected set; }
    public bool AwaitingApproval { get; protected set; }
    public string Status { get; protected set; } = "Ready";
    public abstract string Model { get; }
    public virtual string WorkingDirectory => Directory.GetCurrentDirectory();
    public virtual IReadOnlyList<string> Notifications => [];
    public virtual bool CanAdvance => false;
    public virtual string ApprovalDescription => "dotnet test (simulated; never executed)";
    public virtual Task Completion => Task.CompletedTask;
    public ToolPermission Permission { get; private set; } = ToolPermission.AskForApproval;
    public bool TrySetPermission(ToolPermission permission)
    {
        lock (Entries)
        {
            if (Running || AwaitingApproval || !Enum.IsDefined(permission))
            {
                return false;
            }
            Permission = permission;
            Notify();
            return true;
        }
    }
    public abstract Task SubmitAsync(string text);
    public virtual void Advance() { }
    public virtual void Approve(bool approved) { }
    public virtual void Cancel()
    {
        Running = false;
        AwaitingApproval = false;
        Status = "Interrupted";
        Notify();
    }
    public virtual UsageReport GetUsage(string section, IReadOnlyDictionary<string, int> filters)
        => new([], "Usage reporting is not available for this provider.");
    protected void Notify() => Changed?.Invoke();
}
