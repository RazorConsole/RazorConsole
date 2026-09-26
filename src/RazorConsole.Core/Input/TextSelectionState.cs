// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Input;

/// <summary>Source offsets are UTF-16 boundaries; pointer coordinates are display cells.</summary>
public sealed class TextSelectionState
{
    public string Text { get; private set; } = string.Empty;
    public int Cursor { get; private set; }
    public int Anchor { get; private set; }
    public int Offset { get; set; }
    public bool Dragging { get; private set; }
    public string Selection => Text[Math.Min(Anchor, Cursor)..Math.Max(Anchor, Cursor)];
    public int SelectionStart => Math.Min(Anchor, Cursor);
    public int SelectionEnd => Math.Max(Anchor, Cursor);
    private int _unit;
    private int _originStart;
    private int _originEnd;

    public void SetText(string text)
    {
        Text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        Cursor = Math.Min(Cursor, Text.Length);
        Anchor = Math.Min(Anchor, Text.Length);
    }

    public void Insert(string text)
    {
        var start = SelectionStart;
        Text = Text[..start] + text + Text[SelectionEnd..];
        Cursor = Anchor = start + text.Length;
    }

    public void Backspace()
    {
        if (Selection.Length == 0 && Cursor > 0)
        {
            Anchor = StringInfo.ParseCombiningCharacters(Text[..Cursor]).Last();
        }
        Insert(string.Empty);
    }

    public void Delete()
    {
        if (Selection.Length == 0 && Cursor < Text.Length)
        {
            Cursor += StringInfo.GetNextTextElement(Text, Cursor).Length;
        }
        Insert(string.Empty);
    }

    public void Move(int direction, bool extend = false)
    {
        Cursor = direction < 0
            ? (Cursor == 0 ? 0 : StringInfo.ParseCombiningCharacters(Text[..Cursor]).Last())
            : Math.Min(Text.Length, Cursor + (Cursor < Text.Length ? StringInfo.GetNextTextElement(Text, Cursor).Length : 0));
        if (!extend)
        {
            Anchor = Cursor;
        }
    }

    public void ClearSelection() { Anchor = Cursor; Dragging = false; }

    public void Begin(int position, int clicks, bool extend)
    {
        position = Math.Clamp(position, 0, Text.Length);
        Dragging = true;
        _unit = extend ? 1 : clicks;
        if (extend && Selection.Length > 0)
        {
            Cursor = position;
            _originStart = _originEnd = Anchor;
            return;
        }
        var (start, end) = Unit(position);
        Anchor = _originStart = start;
        Cursor = _originEnd = end;
    }

    public void Extend(int position)
    {
        if (!Dragging)
        {
            return;
        }

        var (start, end) = Unit(Math.Clamp(position, 0, Text.Length));
        Anchor = start < _originStart ? _originEnd : _originStart;
        Cursor = start < _originStart ? start : end;
    }

    public void End() => Dragging = false;

    private (int Start, int End) Unit(int position)
    {
        if (_unit == 3)
        {
            var start = position == 0 ? 0 : Text.LastIndexOf('\n', position - 1) + 1;
            var end = Text.IndexOf('\n', position);
            return (start, end < 0 ? Text.Length : end + 1);
        }
        if (_unit != 2 || position == Text.Length)
        {
            return (position, position);
        }

        var word = char.IsLetterOrDigit(Text[position]) || Text[position] == '_';
        var startWord = position;
        var endWord = position;
        bool Same(char c) => word ? char.IsLetterOrDigit(c) || c == '_' : char.IsWhiteSpace(c) == char.IsWhiteSpace(Text[position]) && !char.IsLetterOrDigit(c);
        while (startWord > 0 && Text[startWord - 1] != '\n' && Same(Text[startWord - 1]))
        {
            startWord--;
        }

        while (endWord < Text.Length && Text[endWord] != '\n' && Same(Text[endWord]))
        {
            endWord++;
        }

        return (startWord, endWord);
    }

    public IReadOnlyList<TextRow> Wrap(int width, int continuationIndent = 0, bool wordWrap = false, Func<int, int>? continuationIndentAt = null)
    {
        width = Math.Max(1, width);
        continuationIndent = Math.Clamp(continuationIndent, 0, width - 1);
        var indent = 0;
        var logicalStart = 0;
        var rows = new List<TextRow>();
        var start = 0;
        var cells = 0;
        var lastBreak = -1;
        for (var i = 0; i < Text.Length;)
        {
            var element = StringInfo.GetNextTextElement(Text, i);
            if (element == "\n")
            {
                rows.Add(new(start, i, indent));
                i++;
                start = i;
                logicalStart = i;
                cells = 0;
                indent = 0;
                lastBreak = -1;
                continue;
            }
            var size = CellWidth(element);
            if (cells > 0 && cells + size + indent > width)
            {
                var end = wordWrap && lastBreak > start ? lastBreak : i;
                rows.Add(new(start, end, indent));
                i = start = end;
                cells = 0;
                indent = Math.Clamp(continuationIndentAt?.Invoke(logicalStart) ?? continuationIndent, 0, width - 1);
                lastBreak = -1;
                continue;
            }
            cells += size;
            i += element.Length;
            if (string.IsNullOrWhiteSpace(element))
            {
                lastBreak = i;
            }
        }
        rows.Add(new(start, Text.Length, indent));
        return rows;
    }

    public int PositionAt(TextRow row, int column)
    {
        column = Math.Max(0, column - row.Indent);
        var cells = 0;
        for (var i = row.Start; i < row.End;)
        {
            var element = StringInfo.GetNextTextElement(Text, i);
            cells += CellWidth(element);
            if (cells > column)
            {
                return i;
            }

            i += element.Length;
        }
        return row.End;
    }

    public static int CellWidth(string text) => Segment.CellCount([new Segment(text)]);
    public readonly record struct TextRow(int Start, int End, int Indent = 0);
}
