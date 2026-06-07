// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Utilities;
using Spectre.Console.Rendering;
using static RazorConsole.Core.Utilities.AnsiSequences;

namespace RazorConsole.Core.Renderables;

internal class DiffRenderable
    : Renderable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IRenderable _renderable;
    private readonly bool _hideCursor;
    private SegmentShape _shape = new(0, 0);
    private List<SegmentLine> _previousLines = new();
    private int _lastMaxWidth = -1;

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

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        _semaphore.Wait();
        try
        {
            yield return Segment.Control(RM(DECTCEM));

            bool widthChanged = _lastMaxWidth != -1 && _lastMaxWidth != maxWidth;
            _lastMaxWidth = maxWidth;

            var segments = _renderable.Render(options, maxWidth);
            var segmentLines = Segment.SplitLines(segments);
            var shape = SegmentShape.Calculate(options, segmentLines);

            var previousLines = _previousLines;
            var totalLines = segmentLines.Count;

            int renderFromLine;
            for (renderFromLine = 0; renderFromLine < totalLines; renderFromLine++)
            {
                var line = segmentLines[renderFromLine];
                var previousLine = renderFromLine < previousLines.Count
                    ? previousLines[renderFromLine]
                    : EmptyLine;
                if (!LinesAreEqual(line, previousLine))
                {
                    break;
                }
            }

            // Move cursor to the first different line in the viewport
            int linesToMoveUp = _shape.Height - renderFromLine;

            bool needFullClear = NeedsFullClear(linesToMoveUp, totalLines) || widthChanged;

            if (needFullClear)
            {
                // The previous content is larger than the current console height, OR resize happened.
                // We need to clear everything to avoid artifacts.
                yield return Segment.Control(ED(2) + ED(3) + CUP(1, 1));
                previousLines = EmptyLines;
                renderFromLine = 0;
            }
            else
            {
                for (var i = 0; i < linesToMoveUp; i++)
                {
                    var previousLineIndex = previousLines.Count - i;
                    if (previousLineIndex >= totalLines)
                    {
                        // The previous line is beyond the current total lines, move up and clear
                        yield return Segment.Control(EL(2) + CUU(1));
                    }
                    else
                    {
                        // just move up
                        yield return Segment.Control(CUU(1));
                    }
                }
            }

            // Render from the first different line
            for (var i = renderFromLine; i < totalLines; i++)
            {
                var line = segmentLines[i];
                var previousLine = i < previousLines.Count
                    ? previousLines[i]
                    : EmptyLine;

                if (!LinesAreEqual(line, previousLine))
                {
                    foreach (var segment in RenderLineDiff(line, previousLine))
                    {
                        yield return segment;
                    }
                }

                yield return Segment.Control(MoveToNextLine());
            }

            // Cleaning residual lines from below
            if (!needFullClear && previousLines.Count > totalLines)
            {
                var remaining = previousLines.Count - totalLines;
                for (var i = 0; i < remaining; i++)
                {
                    yield return Segment.Control(EL(2)); // Clean line
                    yield return Segment.Control(MoveToNextLine()); // Go to next line
                }

                yield return Segment.Control(CUU(remaining));
            }

            // Update the previous lines for next comparison
            _previousLines = CloneLines(segmentLines);
            _shape = shape;

            if (!_hideCursor)
            {
                yield return Segment.Control(SM(DECTCEM));
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool NeedsFullClear(int linesToMoveUp, int totalLines)
    {
        // Console.CursorTop is not supported in WebAssembly, always full clear
        if (OperatingSystem.IsBrowser())
        {
            return true;
        }

        // The viewport and cursor movement are closely linked in conhost
        // Therefore, it is also important to check whether the user has manually changed the visible area by scrolling.
        // https://learn.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences
        if (OperatingSystem.IsWindows() && ExtendedCapabilities.IsConhost)
        {
            var windowHeight = Console.WindowHeight;
            var windowTop = Console.WindowTop;
            // If rendered content is outside viewport -> need full clear
            var onlyMovesInViewport = linesToMoveUp < windowHeight;
            // If rendered normally and no user scroll -> console/cursor it a the bottom
            // If then only parts of the currently visable viewports change -> not full clear needed
            var isConsoleAtBottom = windowTop + windowHeight - 1 == totalLines;
            // If the console is however longer then the total lines 'isConsoleAtBottom' can never be 'true'
            // -> Additional check
            var contentFullyFitsAndNotScrolled = windowHeight - 1 >= totalLines && windowTop == 0;
            return !onlyMovesInViewport || (!isConsoleAtBottom && !contentFullyFitsAndNotScrolled);
        }

        return linesToMoveUp > Console.CursorTop;
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

    private static string MoveToNextLine()
    {
        // NEL is not supported in conhost prior to win11.
        if (OperatingSystem.IsWindows() && ExtendedCapabilities.IsLegacyConhost)
        {
            //Hard "scroll" without NEL
            return "\n";
        }
        return NEL();
    }
}
