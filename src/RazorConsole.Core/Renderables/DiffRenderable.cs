// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console.Rendering;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Core.Renderables;

internal class DiffRenderable
    : Renderable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IRenderable _renderable;
    private readonly bool _hideCursor;
    private List<SegmentLine> _previousLines = new();
    private int _lastMaxWidth = -1;
    private int _lastHeight = -1;

    /// <summary>
    /// Initializes a new instance of the DiffRenderable class to display the differences between two renderable objects
    /// using the specified console.
    /// </summary>
    public DiffRenderable(IRenderable renderable, bool hideCursor)
    {
        _renderable = renderable;
        _hideCursor = hideCursor;
    }

    public void UpdateRenderable(IRenderable renderable)
    {
        _semaphore.Wait();
        try
        {
            _renderable = renderable;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // The live canvas owns the viewport. Never query the terminal for its cursor:
    // input belongs exclusively to the input decoder, including protocol replies.
    public void Invalidate()
    {
        _semaphore.Wait();
        try
        {
            _lastMaxWidth = -1;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        _semaphore.Wait();
        try
        {
            yield return Segment.Control(RM(DECTCEM) + RM(DECAWM));
            var height = Math.Max(1, options.ConsoleSize.Height);
            var fullRedraw = _lastMaxWidth != maxWidth || _lastHeight != height;
            var lines = Segment.SplitLines(_renderable.Render(options, maxWidth)).Take(height).ToList();
            var previous = fullRedraw ? EmptyLines : _previousLines;
            if (fullRedraw)
            {
                // Erase only the visible viewport, never the user's scrollback.
                yield return Segment.Control(ED(2) + CUP(1, 1));
            }

            for (var row = 0; row < lines.Count; row++)
            {
                var before = row < previous.Count ? previous[row] : EmptyLine;
                if (!LinesAreEqual(lines[row], before))
                {
                    yield return Segment.Control(CUP(row + 1, 1));
                    foreach (var segment in RenderLineDiff(lines[row], before))
                    {
                        yield return segment;
                    }
                }
            }
            for (var row = lines.Count; row < previous.Count; row++)
            {
                yield return Segment.Control(CUP(row + 1, 1) + EL(2));
            }

            // Absolute addressing also recovers from cursor movement outside the
            // renderer. No newline at the bottom edge: it would scroll the screen.
            yield return Segment.Control(CUP(Math.Max(1, lines.Count), 1));
            yield return Segment.Control("\r");
            _previousLines = CloneLines(lines);
            _lastMaxWidth = maxWidth;
            _lastHeight = height;
            yield return Segment.Control(SM(DECAWM) + (_hideCursor ? string.Empty : SM(DECTCEM)));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static bool LinesAreEqual(SegmentLine line1, SegmentLine line2)
    {
        if (line1.Count != line2.Count)
        {
            return false;
        }

        for (var i = 0; i < line1.Count; i++)
        {
            var segment1 = line1[i];
            var segment2 = line2[i];

            if (!SegmentsAreEqual(segment1, segment2))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SegmentsAreEqual(Segment segment1, Segment segment2)
    {
        return string.Equals(segment1.Text, segment2.Text, StringComparison.Ordinal)
               && Equals(segment1.Style, segment2.Style);
    }

    internal static IEnumerable<Segment> RenderLineDiff(SegmentLine line, SegmentLine previousLine)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        if (previousLine is null)
        {
            throw new ArgumentNullException(nameof(previousLine));
        }

        var minSegmentCount = Math.Min(line.Count, previousLine.Count);
        var firstDifferentSegmentIndex = 0;
        for (; firstDifferentSegmentIndex < minSegmentCount; firstDifferentSegmentIndex++)
        {
            if (!SegmentsAreEqual(line[firstDifferentSegmentIndex], previousLine[firstDifferentSegmentIndex]))
            {
                break;
            }
        }

        var prefixWidth = firstDifferentSegmentIndex > 0
            ? Segment.CellCount(line.GetRange(0, firstDifferentSegmentIndex))
            : 0;

        if (prefixWidth > 0)
        {
            yield return Segment.Control(CUF(prefixWidth));
        }

        for (var segmentIndex = firstDifferentSegmentIndex; segmentIndex < line.Count; segmentIndex++)
        {
            yield return line[segmentIndex];
        }

        var currentLineWidth = Segment.CellCount(line);
        var previousLineWidth = Segment.CellCount(previousLine);
        if (currentLineWidth < previousLineWidth)
        {
            yield return Segment.Control(EL(0));
        }
    }

    private static List<SegmentLine> CloneLines(List<SegmentLine> source)
    {
        var result = new List<SegmentLine>(source.Count);
        foreach (var line in source)
        {
            result.Add(new SegmentLine(line));
        }

        return result;
    }

    private static readonly List<SegmentLine> EmptyLines = new(0);
    private static readonly SegmentLine EmptyLine = new();
}
