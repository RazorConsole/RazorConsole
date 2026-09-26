// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering.Markdown;
using Spectre.Console;

namespace LLMAgentTUI.Services;

public sealed class AgentViewState
{
    public TextSelectionState Draft { get; } = new();
    public TextSelectionState Display { get; } = new();
    public bool FollowOutput { get; set; } = true;
    public int ContinuationIndentAt(int start)
    {
        var end = Display.Text.IndexOf('\n', start);
        var line = Display.Text[start..(end < 0 ? Display.Text.Length : end)];
        var match = System.Text.RegularExpressions.Regex.Match(line, @"^ *(?:[•›]|\d+\.) +");
        return match.Success ? TextSelectionState.CellWidth(match.Value) : 2;
    }
    private readonly HashSet<int> _expandedTools = [];
    private readonly List<(int Start, int End, int Entry)> _toolHeaders = [];
    private readonly List<(int Start, int End, Style Style)> _styles = [];
    public Style StyleAt(int position) => _styles.FirstOrDefault(s => position >= s.Start && position < s.End).Style ?? Style.Plain;
    public bool IsToolHeader(int position) => _toolHeaders.Any(h => position >= h.Start && position < h.End);
    public void ToggleTool(int position = -1)
    {
        if (_toolHeaders.Count == 0)
        {
            return;
        }

        var header = position < 0 ? _toolHeaders[^1] : _toolHeaders.FirstOrDefault(h => position >= h.Start && position < h.End);
        if (!_expandedTools.Add(header.Entry))
        {
            _expandedTools.Remove(header.Entry);
        }
    }

    public void UpdateTranscript(AgentSession session, int width = 80)
    {
        _toolHeaders.Clear();
        _styles.Clear();
        var blocks = new List<string>();
        var offset = 0;
        var transcript = session.Transcript;
        for (var i = 0; i < transcript.Count; i++)
        {
            var entry = transcript[i] with
            {
                Text = TerminalText.Safe(transcript[i].Text),
                Detail = transcript[i].Detail is { } detail ? TerminalText.Safe(detail) : null,
            };
            var block = entry.Kind switch
            {
                AgentEntryKind.User => $" \n› {entry.Text.Replace("\n", "\n  ")}\n ",
                AgentEntryKind.Tool => $"• {entry.Text}\n  └ {(_expandedTools.Contains(i) ? entry.Detail?.Replace("\n", "\n    ") : string.Join("\n    ", (entry.Detail ?? "").Split('\n').Take(3)))}\n  {(_expandedTools.Contains(i) ? "− Hide details" : "+ Show details")}",
                AgentEntryKind.Patch => $"• {entry.Text}\n{entry.Detail}",
                AgentEntryKind.Error => $"■ {entry.Text.Replace("\n", "\n  ")}",
                _ => $"• {entry.Text}",
            };
            if (entry.Kind == AgentEntryKind.Assistant)
            {
                var markdown = MarkdownText.Parse(entry.Text, "  ", width);
                block = "• " + markdown.Text;
                foreach (var span in markdown.Styles)
                {
                    _styles.Add((offset + 2 + span.Start, offset + 2 + span.Start + span.Length, span.Style));
                }
            }
            if (entry.Kind == AgentEntryKind.Tool)
            {
                _toolHeaders.Add((offset, offset + entry.Text.Length + 2, i));
                var disclosure = block.LastIndexOf('\n') + 1;
                _toolHeaders.Add((offset + disclosure, offset + block.Length, i));
                _styles.Add((offset, offset + entry.Text.Length + 2, new Style(decoration: Decoration.Bold)));
                _styles.Add((offset + disclosure, offset + block.Length, new Style(new Color(17, 96, 220), decoration: Decoration.Bold)));
            }
            if (entry.Kind == AgentEntryKind.User)
            {
                _styles.Add((offset, offset + block.Length, new Style(background: new Color(239, 239, 241))));
            }
            if (entry.Kind == AgentEntryKind.Error)
            {
                _styles.Add((offset, offset + block.Length, new Style(Color.Red)));
            }

            blocks.Add(block);
            offset += block.Length + 2;
        }
        Display.SetText(string.Join("\n\n", blocks));
    }
}
