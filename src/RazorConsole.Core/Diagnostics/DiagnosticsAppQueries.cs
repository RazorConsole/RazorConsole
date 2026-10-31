// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Diagnostics;

/// <summary>
/// Answers the read-only endpoint methods from the app's diagnostics state. Every answer reads one published frame,
/// so tree, layout and focus keys always belong together.
/// </summary>
internal sealed class DiagnosticsAppQueries(
    DiagnosticsState state,
    FocusManager focusManager,
    NavigationManager navigationManager,
    ConsoleAppOptions options)
{
    private const int MaxAmbiguousCandidates = 20;

    public TreeResult Tree(TreeParams parameters, DiagnosticsFrame? frame = null)
    {
        if (parameters.Depth < 0)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, "depth must not be negative.");
        }

        var index = CreateIndex(frame ?? state.LatestFrame);
        var start = parameters.Selector is null
            ? index.Root is { } root ? index[root] : null
            : Single(index, DiagnosticsSelector.Parse(parameters.Selector), first: false);

        var node = start is null
            ? null
            : BuildNode(index, start, parameters.Depth ?? int.MaxValue, parameters.IncludeText ?? true, parameters.IncludeLayout ?? true);
        return new TreeResult(node, index.Frame);
    }

    public FindResult Find(FindParams parameters, DiagnosticsFrame? frame = null)
    {
        var selector = DiagnosticsSelector.Parse(parameters.Selector);
        var index = CreateIndex(frame ?? state.LatestFrame);
        return new FindResult(selector.Match(index).ConvertAll(index.ToMatch), index.Frame);
    }

    public InspectResult Inspect(InspectParams parameters, DiagnosticsFrame? frame = null)
    {
        var selector = DiagnosticsSelector.Parse(parameters.Selector);
        var index = CreateIndex(frame ?? state.LatestFrame);
        var entry = Single(index, selector, parameters.First ?? false);
        return new InspectResult(
            BuildNode(index, entry, depth: 1, includeText: true, includeLayout: true),
            index.ElementAncestors(entry).Select(index.ToMatch).ToArray(),
            index.Frame);
    }

    /// <summary>
    /// Hit-tests the layout: the element with the highest z-index, then the deepest, whose rectangle holds the cell.
    /// </summary>
    /// <remarks>
    /// Reads the frame on screen, so the stack and the painted cell match what the user sees. Before the first paint
    /// (and on the legacy pipeline) it reads the latest frame and returns no cell.
    /// </remarks>
    public InspectAtResult InspectAt(InspectAtParams parameters)
    {
        var screen = state.LatestScreen;
        var index = CreateIndex(screen?.Frame ?? state.LatestFrame);
        DiagnosticsTreeEntry? hit = null;
        DiagnosticsRect? hitRect = null;
        foreach (var entry in index.Nodes)
        {
            if (index.RectOf(entry.Node) is not { } rect
                || parameters.X < rect.Left || parameters.X >= rect.Left + rect.Width
                || parameters.Y < rect.Top || parameters.Y >= rect.Top + rect.Height)
            {
                continue;
            }

            if (hit is null || rect.ZIndex > hitRect!.ZIndex || (rect.ZIndex == hitRect.ZIndex && entry.Depth >= hit.Depth))
            {
                hit = entry;
                hitRect = rect;
            }
        }

        var stack = hit is null ? [] : index.ElementAncestors(hit).Prepend(hit).Select(index.ToMatch).ToArray();
        var cell = screen is null ? null : DiagnosticsScreenReader.CellAt(screen.Canvas, parameters.X, parameters.Y);
        return new InspectAtResult(parameters.X, parameters.Y, cell, stack, index.Frame);
    }

    public ScreenResult Screen(ScreenParams parameters) => DiagnosticsScreenReader.Read(state, options, parameters);

    public FocusResult Focus()
    {
        var targets = focusManager.GetFocusTargets();
        string? current = null;
        var results = new FocusTargetResult[targets.Length];
        for (var i = 0; i < targets.Length; i++)
        {
            results[i] = new FocusTargetResult(targets[i].Key, targets[i].VNodeId);
            if (targets[i].IsFocused)
            {
                current = targets[i].Key;
            }
        }

        return new FocusResult(current, results);
    }

    public RouteResult Route()
    {
        var uri = navigationManager.Uri;
        return new RouteResult(uri, navigationManager.ToBaseRelativePath(uri));
    }

    public ErrorsResult Errors(ErrorsParams parameters)
    {
        var entries = state.Errors.Read(AfterSequence(parameters.Since));
        var errors = Array.ConvertAll(entries, e => new ErrorResult(
            e.Sequence, e.Value.Timestamp, e.Value.Source, e.Value.Type, e.Value.Message, e.Value.Details));
        return new ErrorsResult(errors, Next(entries, parameters.Since));
    }

    public LogsResult Logs(LogsParams parameters)
    {
        var minimum = LogLevel.Trace;
        if (parameters.Level is not null
            && (!Enum.TryParse(parameters.Level, ignoreCase: true, out minimum) || !Enum.IsDefined(minimum)))
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"Unknown log level '{parameters.Level}'.");
        }

        var entries = state.Logs.Read(AfterSequence(parameters.Since));
        var results = new List<LogResult>(entries.Length);
        foreach (var (sequence, entry) in entries)
        {
            if (entry.Level < minimum
                || (parameters.Category is not null && !entry.Category.StartsWith(parameters.Category, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            results.Add(new LogResult(
                sequence, entry.Timestamp, entry.Level.ToString(), entry.Category, entry.EventId, entry.Message, entry.Exception));
        }

        return new LogsResult(results, Next(entries, parameters.Since));
    }

    public StatsResult Stats()
    {
        var counters = state.Counters.Read();
        return new StatsResult(
            counters.Frames,
            new RenderTimeResult(counters.LastRenderMs, counters.AverageRenderMs, counters.MaxRenderMs),
            counters.DroppedFrames,
            (long)state.Uptime.TotalMilliseconds,
            options.RenderingPipeline.ToString(),
            state.LatestFrame?.Sequence ?? 0);
    }

    private DiagnosticsTreeIndex CreateIndex(DiagnosticsFrame? frame) => new(frame, focusManager.GetFocusTargets());

    private static DiagnosticsTreeEntry Single(DiagnosticsTreeIndex index, DiagnosticsSelector selector, bool first)
    {
        var matches = selector.Match(index);
        if (matches.Count == 0)
        {
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.ElementNotFound,
                selector.IsText
                    ? "No node matches the selector. Text selectors match the text in the component tree; click --text and wait --text match the screen."
                    : "No node matches the selector.");
        }

        if (matches.Count > 1 && !first)
        {
            var candidates = new AmbiguousSelectorData(matches.Take(MaxAmbiguousCandidates).Select(index.ToMatch).ToArray());
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.AmbiguousSelector,
                $"{matches.Count} nodes match the selector.",
                errorData: JsonSerializer.SerializeToElement(candidates, DiagnosticsJsonContext.Default.AmbiguousSelectorData));
        }

        return matches[0];
    }

    private static DiagnosticsNode BuildNode(DiagnosticsTreeIndex index, DiagnosticsTreeEntry entry, int depth, bool includeText, bool includeLayout)
    {
        var node = entry.Node;
        var isElement = node.Kind == VNodeKind.Element;
        var focusKey = index.FocusKeyOf(node);

        List<DiagnosticsNode>? children = null;
        int? childCount = null;
        if (node.Children.Count > 0)
        {
            if (depth > 0)
            {
                children = [];
                foreach (var child in node.Children)
                {
                    if (includeText || child.Kind != VNodeKind.Text)
                    {
                        children.Add(BuildNode(index, index[child], depth - 1, includeText, includeLayout));
                    }
                }
            }
            else
            {
                childCount = node.Children.Count;
            }
        }

        return new DiagnosticsNode
        {
            Id = isElement ? node.ID : null,
            Path = entry.Path,
            Kind = DiagnosticsTreeIndex.KindName(node.Kind),
            Tag = node.TagName,
            Key = node.Key,
            Hook = node.Attributes.TryGetValue(IVNodeIdAccessor.HookAttributeName, out var hook) ? hook : null,
            Text = node.Kind == VNodeKind.Text ? node.Text : null,
            Attributes = isElement ? OwnAttributes(node) : null,
            Events = isElement && node.Events.Count > 0 ? node.Events.Select(e => e.Name).ToArray() : null,
            Rect = includeLayout ? index.RectOf(node) : null,
            FocusKey = focusKey,
            Focused = focusKey is null ? null : node.ID == index.FocusedId,
            ChildCount = childCount,
            Children = children is { Count: > 0 } ? children : null,
        };
    }

    // The cursor is inclusive and sequences start at 1.
    // The hook has its own field, so it is not repeated among the attributes.
    private static IReadOnlyDictionary<string, string?>? OwnAttributes(VNode node)
    {
        var attributes = node.Attributes;
        if (!attributes.ContainsKey(IVNodeIdAccessor.HookAttributeName))
        {
            return attributes.Count > 0 ? attributes : null;
        }

        return attributes.Count > 1
            ? attributes.Where(a => a.Key != IVNodeIdAccessor.HookAttributeName).ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal)
            : null;
    }

    private static long AfterSequence(long? since) => Math.Max((since ?? 1) - 1, 0);

    // Taken from what was read, not from the ring afterwards, so an entry added meanwhile is never skipped.
    private static long Next<T>(DiagnosticsRingEntry<T>[] entries, long? since)
        => entries.Length > 0 ? entries[^1].Sequence + 1 : Math.Max(since ?? 1, 1);
}
