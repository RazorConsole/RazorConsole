// Copyright (c) RazorConsole. All rights reserved.

namespace LLMAgentTUI.Services;

/// <summary>Offline session data for repeatable tests. It never executes tools.</summary>
public sealed class ScriptedAgentSession : AgentSession
{
    public ScriptedAgentSession(IEnumerable<AgentEntry>? initialEntries = null)
    {
        if (initialEntries is not null)
        {
            Entries.AddRange(initialEntries);
        }
    }
    private readonly List<string> _events = [];
    private int _step;
    public IReadOnlyList<string> Events => _events;
    public override string Model => "scripted-session";
    public override string WorkingDirectory => "~/repos/RazorConsole";
    public override IReadOnlyList<string> Notifications => ["Scripted session: responses and tool results are prerecorded. No tools are executed."];
    public override bool CanAdvance => true;

    public override Task SubmitAsync(string text)
    {
        if (Running || string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        Entries.Add(new(AgentEntryKind.User, text.Trim()));
        Running = true;
        _step = 0;
        Status = "Working";
        Record($"submit: {text.Trim()}");
        return Task.CompletedTask;
    }

    public override void Advance()
    {
        if (!Running || AwaitingApproval)
        {
            return;
        }

        switch (_step++)
        {
            case 0:
                Entries.Add(new(AgentEntryKind.Assistant, "I will inspect the layout and add a regression test."));
                break;
            case 1:
                Entries.Add(new(AgentEntryKind.Tool, "Explored", "Read README.md, FlexWidget.cs"));
                break;
            case 2:
                Entries.Add(new(AgentEntryKind.Patch, "Updated src/Layout.cs (+3 -1)", "    - var width = 80;\n    + var width = viewport.Width;"));
                break;
            case 3:
                AwaitingApproval = true;
                Status = "Waiting for approval";
                break;
            case 4:
                Entries.Add(new(AgentEntryKind.Tool, "Ran dotnet test", "Passed: 12, Failed: 0 (scripted result)\n    Passed LayoutTests.FillWidth\n    Passed LayoutTests.Resize\n    Passed LayoutTests.Clipping"));
                break;
            default:
                Entries.Add(new(AgentEntryKind.Assistant, "Added the layout regression test. All checks passed.\n  See https://example.com/layout for the fixture documentation."));
                Running = false;
                Status = "Ready";
                break;
        }
        Record($"advance: {_step}");
    }

    public override void Approve(bool approved)
    {
        if (!AwaitingApproval)
        {
            return;
        }

        AwaitingApproval = false;
        if (!approved)
        {
            Entries.Add(new(AgentEntryKind.Assistant, "Command declined. No command was executed."));
            Running = false;
        }
        Status = Running ? "Working" : "Ready";
        Record(approved ? "approval: accepted (simulated)" : "approval: declined");
    }

    public void SeedHistory(int turns)
    {
        for (var i = 1; i <= turns; i++)
        {
            Entries.Add(new(AgentEntryKind.User, $"Inspect layout scenario {i:00}"));
            Entries.Add(new(AgentEntryKind.Assistant, $"Scenario {i:00}: measured, arranged and painted the widget tree."));
        }
        Record($"seed: {turns}");
    }

    public override UsageReport GetUsage(string section, IReadOnlyDictionary<string, int> filters)
    {
        var count = filters.GetValueOrDefault("Range") == 0 ? 30 : 7;
        var multiplier = 1 + filters.GetValueOrDefault("Model") + filters.GetValueOrDefault("Metric");
        var rows = Enumerable.Range(1, count)
            .Select(i => new UsageMetric($"{section} {i:00}", i % 5 == 0 ? 0 : i * 17 * multiplier))
            .Where(row => filters.GetValueOrDefault("ZeroCredit") != 0 || row.Tokens != 0)
            .ToArray();
        return new(rows);
    }

    private void Record(string description)
    {
        _events.Add(description);
        Notify();
    }
}
