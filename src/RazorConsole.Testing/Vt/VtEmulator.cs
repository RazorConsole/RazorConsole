// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using System.Text;
using RazorConsole.Core.Layout;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Testing.Vt;

/// <summary>
/// A VT/xterm screen emulator that turns terminal output into a <see cref="TerminalCell"/> grid.
/// It covers what xterm-compatible applications and ConPTY emit; it is not a complete xterm.
/// </summary>
/// <remarks>
/// Not thread-safe: the owner serializes <see cref="Feed(ReadOnlySpan{byte})"/>, <see cref="Resize"/>, and
/// <see cref="CreateFrame"/>. The parser follows the DEC state machine (ground, escape, CSI, OSC, and
/// ignored DCS/SOS/PM/APC strings), and UTF-8 sequences may be split across calls.
/// </remarks>
internal sealed class VtEmulator
{
    private const int MaximumParameters = 32;
    private const int MaximumStringLength = 4096;

    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly Action<string>? _respond;
    private readonly int[] _parameters = new int[MaximumParameters];
    private readonly bool[] _subParameter = new bool[MaximumParameters];
    private readonly StringBuilder _print = new();
    private readonly StringBuilder _string = new();
    private readonly SavedCursor[] _savedCursors = [SavedCursor.Initial, SavedCursor.Initial];

    private TerminalCell[][] _primary;
    private TerminalCell[][] _alternate;
    private bool[] _tabStops;
    private char[] _chars = new char[4096];

    private ParserState _state;
    private int _parameterCount;
    private bool _parameterStarted;
    private char _prefix;
    private char _intermediate;
    private bool _stringEscape;
    private char _pendingHighSurrogate;

    private int _x;
    private int _y;
    private bool _wrapPending;
    private int _top;
    private int _bottom;
    private bool _originMode;
    private bool _autoWrap = true;
    private bool _alternateActive;
    private bool _g0Graphics;
    private bool _g1Graphics;
    private bool _shiftOut;
    private Attributes _attributes;
    private Style? _style;
    private bool _styleValid = true;
    private string? _lastGrapheme;
    private int _lastGraphemeX;
    private int _lastGraphemeY;

    public VtEmulator(int width, int height, Action<string>? respond = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        _respond = respond;
        _primary = CreateRows(width, height);
        _alternate = CreateRows(width, height);
        _tabStops = CreateTabStops(width);
        _bottom = height - 1;
    }

    private enum ParserState
    {
        Ground,
        Escape,
        EscapeIntermediate,
        Csi,
        CsiIgnore,
        Osc,
        IgnoredString,
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int CursorX => _x;

    public int CursorY => _y;

    public bool CursorVisible { get; private set; } = true;

    public TerminalScreenBuffer ActiveBuffer => _alternateActive ? TerminalScreenBuffer.Alternate : TerminalScreenBuffer.Normal;

    public TerminalMouseTracking MouseTracking { get; private set; }

    public bool BracketedPaste { get; private set; }

    public bool ApplicationCursorKeys { get; private set; }

    public bool FocusReporting { get; private set; }

    /// <summary>
    /// Gets whether the application is inside a synchronized update (DEC private mode 2026).
    /// </summary>
    public bool SynchronizedOutput { get; private set; }

    /// <summary>
    /// Gets how many synchronized updates have started, so a new update can be told apart from a long one.
    /// </summary>
    public long SynchronizedUpdateCount { get; private set; }

    private TerminalCell[][] Screen => _alternateActive ? _alternate : _primary;

    /// <summary>
    /// Ends a synchronized update that the application left open for too long, as terminals do after a timeout.
    /// </summary>
    public void EndSynchronizedOutput() => SynchronizedOutput = false;

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        var count = _decoder.GetCharCount(bytes, flush: false);
        if (count > _chars.Length)
        {
            _chars = new char[Math.Max(count, _chars.Length * 2)];
        }

        var written = _decoder.GetChars(bytes, _chars, flush: false);
        Feed(_chars.AsSpan(0, written));
    }

    public void Feed(string text) => Feed(text.AsSpan());

    public void Feed(ReadOnlySpan<char> text)
    {
        foreach (var ch in text)
        {
            Process(ch);
        }

        FlushPrint();
    }

    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        FlushPrint();
        _primary = ResizeRows(_primary, width, height);
        _alternate = ResizeRows(_alternate, width, height);
        var tabStops = CreateTabStops(width);
        Array.Copy(_tabStops, tabStops, Math.Min(_tabStops.Length, width));
        _tabStops = tabStops;
        Width = width;
        Height = height;
        _top = 0;
        _bottom = height - 1;
        _x = Math.Min(_x, width - 1);
        _y = Math.Min(_y, height - 1);
        _wrapPending = false;
        _lastGrapheme = null;
    }

    public PtyTerminalFrame CreateFrame(long frameNumber, DateTimeOffset observedAt)
    {
        var screen = Screen;
        var cells = new TerminalCell[Height, Width];
        for (var y = 0; y < Height; y++)
        {
            var row = screen[y];
            for (var x = 0; x < Width; x++)
            {
                cells[y, x] = row[x];
            }
        }

        return new PtyTerminalFrame(
            frameNumber,
            observedAt,
            cells,
            _x,
            _y,
            CursorVisible,
            ActiveBuffer,
            MouseTracking,
            BracketedPaste);
    }

    private void Process(char ch)
    {
        if (_state == ParserState.Ground && IsPrintable(ch))
        {
            _print.Append(ch);
            return;
        }

        FlushPrint();
        switch (_state)
        {
            case ParserState.Ground:
                Execute(ch);
                break;
            case ParserState.Escape:
                ProcessEscape(ch);
                break;
            case ParserState.EscapeIntermediate:
                ProcessEscapeIntermediate(ch);
                break;
            case ParserState.Csi:
            case ParserState.CsiIgnore:
                ProcessCsi(ch);
                break;
            case ParserState.Osc:
            case ParserState.IgnoredString:
                ProcessString(ch);
                break;
        }
    }

    private static bool IsPrintable(char ch)
        => ch >= ' ' && ch != '\u007f' && (ch < '\u0080' || ch > '\u009f');

    private bool HandleControlInSequence(char ch)
    {
        switch (ch)
        {
            case '\u001b':
                EnterEscape();
                return true;
            case '\u0018':
            case '\u001a':
                _state = ParserState.Ground;
                return true;
            case < ' ':
                Execute(ch);
                return true;
            case '\u007f':
                return true;
            default:
                return false;
        }
    }

    private void EnterEscape()
    {
        _state = ParserState.Escape;
        _intermediate = '\0';
    }

    private void Execute(char ch)
    {
        _lastGrapheme = null;
        switch (ch)
        {
            case '\u001b':
                EnterEscape();
                break;
            case '\b':
                _wrapPending = false;
                if (_x > 0)
                {
                    _x--;
                }

                break;
            case '\t':
                HorizontalTab(1);
                break;
            case '\n':
            case '\v':
            case '\f':
                LineFeed();
                break;
            case '\r':
                _x = 0;
                _wrapPending = false;
                break;
            case '\u000e':
                _shiftOut = true;
                break;
            case '\u000f':
                _shiftOut = false;
                break;
        }
    }

    private void ProcessEscape(char ch)
    {
        if (HandleControlInSequence(ch))
        {
            return;
        }

        switch (ch)
        {
            case '[':
                _state = ParserState.Csi;
                _parameterCount = 0;
                _parameterStarted = false;
                _prefix = '\0';
                _intermediate = '\0';
                return;
            case ']':
                _state = ParserState.Osc;
                _string.Clear();
                _stringEscape = false;
                return;
            case 'P':
            case 'X':
            case '^':
            case '_':
                _state = ParserState.IgnoredString;
                _stringEscape = false;
                return;
            case >= ' ' and <= '/':
                _intermediate = ch;
                _state = ParserState.EscapeIntermediate;
                return;
        }

        _state = ParserState.Ground;
        _lastGrapheme = null;
        switch (ch)
        {
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                _x = 0;
                LineFeed();
                break;
            case 'H':
                _tabStops[_x] = true;
                break;
            case 'M':
                ReverseIndex();
                break;
            case 'c':
                FullReset();
                break;
        }
    }

    private void ProcessEscapeIntermediate(char ch)
    {
        if (HandleControlInSequence(ch))
        {
            return;
        }

        if (ch is >= ' ' and <= '/')
        {
            return;
        }

        _state = ParserState.Ground;
        switch (_intermediate)
        {
            case '(':
                _g0Graphics = ch == '0';
                break;
            case ')':
                _g1Graphics = ch == '0';
                break;
            case '#' when ch == '8':
                ScreenAlignment();
                break;
        }
    }

    private void ProcessCsi(char ch)
    {
        if (HandleControlInSequence(ch))
        {
            return;
        }

        switch (ch)
        {
            case >= '0' and <= '9':
                if (_state == ParserState.CsiIgnore || _intermediate != '\0')
                {
                    _state = ParserState.CsiIgnore;
                    return;
                }

                if (!_parameterStarted)
                {
                    StartParameter(subParameter: false);
                }

                ref var value = ref _parameters[_parameterCount - 1];
                value = value < 0 ? ch - '0' : Math.Min(value * 10 + ch - '0', 65535);
                return;
            case ';':
            case ':':
                if (!_parameterStarted)
                {
                    StartParameter(subParameter: false);
                }

                _parameterStarted = false;
                if (_parameterCount < MaximumParameters)
                {
                    StartParameter(subParameter: ch == ':');
                    _parameterStarted = true;
                }

                return;
            case >= '<' and <= '?':
                if (_parameterCount > 0 || _prefix != '\0')
                {
                    _state = ParserState.CsiIgnore;
                }
                else
                {
                    _prefix = ch;
                }

                return;
            case >= ' ' and <= '/':
                _intermediate = ch;
                return;
            case >= '@' and <= '~':
                var ignore = _state == ParserState.CsiIgnore;
                _state = ParserState.Ground;
                _lastGrapheme = null;
                if (!ignore)
                {
                    DispatchCsi(ch);
                }

                return;
        }
    }

    private void StartParameter(bool subParameter)
    {
        if (_parameterCount >= MaximumParameters)
        {
            _state = ParserState.CsiIgnore;
            return;
        }

        _parameters[_parameterCount] = -1;
        _subParameter[_parameterCount] = subParameter;
        _parameterCount++;
        _parameterStarted = true;
    }

    private void ProcessString(char ch)
    {
        if (_stringEscape)
        {
            _stringEscape = false;
            FinishString();
            if (ch != '\\')
            {
                EnterEscape();
                ProcessEscape(ch);
            }

            return;
        }

        switch (ch)
        {
            case '\u0007':
                FinishString();
                return;
            case '\u001b':
                _stringEscape = true;
                return;
            case '\u0018':
            case '\u001a':
                _state = ParserState.Ground;
                return;
        }

        if (_state == ParserState.Osc && _string.Length < MaximumStringLength)
        {
            _string.Append(ch);
        }
    }

    private void FinishString()
    {
        var isOsc = _state == ParserState.Osc;
        _state = ParserState.Ground;
        if (!isOsc)
        {
            return;
        }

        var text = _string.ToString();
        _string.Clear();
        if (text.StartsWith("8;", StringComparison.Ordinal))
        {
            var uriStart = text.IndexOf(';', 2);
            var uri = uriStart < 0 ? string.Empty : text[(uriStart + 1)..];
            SetAttributes(_attributes with { Link = uri.Length == 0 ? null : uri });
        }
    }

    private int Parameter(int index, int defaultValue)
    {
        if (index >= _parameterCount)
        {
            return defaultValue;
        }

        var value = _parameters[index];
        return value < 0 ? defaultValue : value;
    }

    private int Count(int index = 0) => Math.Max(1, Parameter(index, 1));

    private void DispatchCsi(char final)
    {
        if (_intermediate != '\0')
        {
            DispatchCsiWithIntermediate(final);
            return;
        }

        if (_prefix == '?')
        {
            switch (final)
            {
                case 'h':
                    SetPrivateModes(true);
                    break;
                case 'l':
                    SetPrivateModes(false);
                    break;
                case 'J':
                    EraseInDisplay(Parameter(0, 0));
                    break;
                case 'K':
                    EraseInLine(Parameter(0, 0));
                    break;
                case 'n' when Parameter(0, 0) == 6:
                    Respond($"\u001b[?{ReportedRow()};{_x + 1}R");
                    break;
            }

            return;
        }

        if (_prefix == '>')
        {
            if (final == 'c')
            {
                Respond("\u001b[>0;10;1c");
            }

            return;
        }

        if (_prefix != '\0')
        {
            return;
        }

        switch (final)
        {
            case '@':
                InsertCharacters(Count());
                break;
            case 'A':
                CursorUp(Count());
                break;
            case 'B':
            case 'e':
                CursorDown(Count());
                break;
            case 'C':
            case 'a':
                MoveTo(_x + Count(), _y);
                break;
            case 'D':
                MoveTo(_x - Count(), _y);
                break;
            case 'E':
                CursorDown(Count());
                _x = 0;
                break;
            case 'F':
                CursorUp(Count());
                _x = 0;
                break;
            case 'G':
            case '`':
                MoveTo(Count() - 1, _y);
                break;
            case 'H':
            case 'f':
                SetCursorPosition(Count(0), Count(1));
                break;
            case 'I':
                HorizontalTab(Count());
                break;
            case 'J':
                EraseInDisplay(Parameter(0, 0));
                break;
            case 'K':
                EraseInLine(Parameter(0, 0));
                break;
            case 'L':
                InsertLines(Count());
                break;
            case 'M':
                DeleteLines(Count());
                break;
            case 'P':
                DeleteCharacters(Count());
                break;
            case 'S':
                ScrollUp(Count());
                break;
            case 'T':
                if (_parameterCount <= 1)
                {
                    ScrollDown(Count());
                }

                break;
            case 'X':
                EraseCharacters(Count());
                break;
            case 'Z':
                BackTab(Count());
                break;
            case 'b':
                RepeatLastGrapheme(Count());
                break;
            case 'c':
                if (Parameter(0, 0) == 0)
                {
                    Respond("\u001b[?1;2c");
                }

                break;
            case 'd':
                SetCursorPosition(Count(), _x + 1, keepColumn: true);
                break;
            case 'g':
                ClearTabStops(Parameter(0, 0));
                break;
            case 'm':
                SelectGraphicRendition();
                break;
            case 'n':
                DeviceStatusReport(Parameter(0, 0));
                break;
            case 'r':
                var bottom = Parameter(1, 0);
                SetScrollRegion(Count(0), bottom == 0 ? Height : bottom);
                break;
            case 's':
                SaveCursor();
                break;
            case 'u':
                RestoreCursor();
                break;
            case 't':
                if (Parameter(0, 0) == 18)
                {
                    Respond($"\u001b[8;{Height};{Width}t");
                }

                break;
        }
    }

    private void DispatchCsiWithIntermediate(char final)
    {
        if (_intermediate == '!' && final == 'p')
        {
            SoftReset();
        }
        else if (_intermediate == '$' && final == 'p' && _prefix == '?')
        {
            var mode = Parameter(0, 0);
            var state = GetPrivateMode(mode) switch
            {
                true => 1,
                false => 2,
                null => 0,
            };
            Respond($"\u001b[?{mode};{state}$y");
        }
    }

    private void Respond(string response) => _respond?.Invoke(response);

    private int ReportedRow() => _originMode ? _y - _top + 1 : _y + 1;

    private void DeviceStatusReport(int kind)
    {
        if (kind == 5)
        {
            Respond("\u001b[0n");
        }
        else if (kind == 6)
        {
            Respond($"\u001b[{ReportedRow()};{_x + 1}R");
        }
    }

    private void SetPrivateModes(bool enabled)
    {
        for (var i = 0; i < Math.Max(1, _parameterCount); i++)
        {
            SetPrivateMode(Parameter(i, 0), enabled);
        }
    }

    private void SetPrivateMode(int mode, bool enabled)
    {
        switch (mode)
        {
            case 1:
                ApplicationCursorKeys = enabled;
                break;
            case 6:
                _originMode = enabled;
                SetCursorPosition(1, 1);
                break;
            case 7:
                _autoWrap = enabled;
                if (!enabled)
                {
                    _wrapPending = false;
                }

                break;
            case 25:
                CursorVisible = enabled;
                break;
            case 47:
                SwitchBuffer(enabled, clearAlternate: false);
                break;
            case 1047:
                if (!enabled && _alternateActive)
                {
                    ClearRows(_alternate, 0, Height - 1);
                }

                SwitchBuffer(enabled, clearAlternate: false);
                break;
            case 1048:
                if (enabled)
                {
                    SaveCursor();
                }
                else
                {
                    RestoreCursor();
                }

                break;
            case 1049:
                if (enabled)
                {
                    if (!_alternateActive)
                    {
                        SaveCursor();
                        SwitchBuffer(true, clearAlternate: true);
                    }
                }
                else if (_alternateActive)
                {
                    SwitchBuffer(false, clearAlternate: false);
                    RestoreCursor();
                }

                break;
            case 1000:
                SetMouse(TerminalMouseTracking.Normal, enabled);
                break;
            case 1002:
                SetMouse(TerminalMouseTracking.ButtonEvent, enabled);
                break;
            case 1003:
                SetMouse(TerminalMouseTracking.AnyEvent, enabled);
                break;
            case 1006:
                SetMouse(TerminalMouseTracking.SgrEncoding, enabled);
                break;
            case 1004:
                FocusReporting = enabled;
                break;
            case 2004:
                BracketedPaste = enabled;
                break;
            case 2026:
                if (enabled && !SynchronizedOutput)
                {
                    SynchronizedUpdateCount++;
                }

                SynchronizedOutput = enabled;
                break;
        }
    }

    private bool? GetPrivateMode(int mode) => mode switch
    {
        1 => ApplicationCursorKeys,
        6 => _originMode,
        7 => _autoWrap,
        25 => CursorVisible,
        47 or 1047 or 1049 => _alternateActive,
        1000 => MouseTracking.HasFlag(TerminalMouseTracking.Normal),
        1002 => MouseTracking.HasFlag(TerminalMouseTracking.ButtonEvent),
        1003 => MouseTracking.HasFlag(TerminalMouseTracking.AnyEvent),
        1006 => MouseTracking.HasFlag(TerminalMouseTracking.SgrEncoding),
        1004 => FocusReporting,
        2004 => BracketedPaste,
        2026 => SynchronizedOutput,
        _ => null,
    };

    private void SetMouse(TerminalMouseTracking flag, bool enabled)
        => MouseTracking = enabled ? MouseTracking | flag : MouseTracking & ~flag;

    private void SwitchBuffer(bool alternate, bool clearAlternate)
    {
        if (alternate && clearAlternate)
        {
            ClearRows(_alternate, 0, Height - 1);
        }

        _alternateActive = alternate;
        _wrapPending = false;
    }

    private void SelectGraphicRendition()
    {
        if (_parameterCount == 0)
        {
            SetAttributes(_attributes with { Foreground = null, Background = null, Decoration = Decoration.None });
            return;
        }

        var attributes = _attributes;
        for (var i = 0; i < _parameterCount; i++)
        {
            var code = Parameter(i, 0);
            var groupEnd = i + 1;
            while (groupEnd < _parameterCount && _subParameter[groupEnd])
            {
                groupEnd++;
            }

            switch (code)
            {
                case 0:
                    attributes = attributes with { Foreground = null, Background = null, Decoration = Decoration.None };
                    break;
                case 1:
                    attributes = attributes.With(Decoration.Bold);
                    break;
                case 2:
                    attributes = attributes.With(Decoration.Dim);
                    break;
                case 3:
                    attributes = attributes.With(Decoration.Italic);
                    break;
                case 4:
                    attributes = groupEnd > i + 1 && Parameter(i + 1, 1) == 0
                        ? attributes.Without(Decoration.Underline)
                        : attributes.With(Decoration.Underline);
                    break;
                case 5:
                    attributes = attributes.With(Decoration.SlowBlink);
                    break;
                case 6:
                    attributes = attributes.With(Decoration.RapidBlink);
                    break;
                case 7:
                    attributes = attributes.With(Decoration.Invert);
                    break;
                case 8:
                    attributes = attributes.With(Decoration.Conceal);
                    break;
                case 9:
                    attributes = attributes.With(Decoration.Strikethrough);
                    break;
                case 21:
                    attributes = attributes.With(Decoration.Underline);
                    break;
                case 22:
                    attributes = attributes.Without(Decoration.Bold | Decoration.Dim);
                    break;
                case 23:
                    attributes = attributes.Without(Decoration.Italic);
                    break;
                case 24:
                    attributes = attributes.Without(Decoration.Underline);
                    break;
                case 25:
                    attributes = attributes.Without(Decoration.SlowBlink | Decoration.RapidBlink);
                    break;
                case 27:
                    attributes = attributes.Without(Decoration.Invert);
                    break;
                case 28:
                    attributes = attributes.Without(Decoration.Conceal);
                    break;
                case 29:
                    attributes = attributes.Without(Decoration.Strikethrough);
                    break;
                case >= 30 and <= 37:
                    attributes = attributes with { Foreground = Color.FromInt32(code - 30) };
                    break;
                case 38:
                    attributes = attributes with { Foreground = ReadExtendedColor(ref i, groupEnd) ?? attributes.Foreground };
                    break;
                case 39:
                    attributes = attributes with { Foreground = null };
                    break;
                case >= 40 and <= 47:
                    attributes = attributes with { Background = Color.FromInt32(code - 40) };
                    break;
                case 48:
                    attributes = attributes with { Background = ReadExtendedColor(ref i, groupEnd) ?? attributes.Background };
                    break;
                case 49:
                    attributes = attributes with { Background = null };
                    break;
                case >= 90 and <= 97:
                    attributes = attributes with { Foreground = Color.FromInt32(code - 90 + 8) };
                    break;
                case >= 100 and <= 107:
                    attributes = attributes with { Background = Color.FromInt32(code - 100 + 8) };
                    break;
            }

            // Sub-parameters (colon form) belong to the code they follow.
            i = Math.Max(i, groupEnd - 1);
        }

        SetAttributes(attributes);
    }

    private Color? ReadExtendedColor(ref int index, int groupEnd)
    {
        if (groupEnd > index + 1)
        {
            // Colon form: 38:5:n, 38:2:r:g:b, or 38:2:colorspace:r:g:b.
            var kind = Parameter(index + 1, -1);
            var count = groupEnd - index;
            if (kind == 5 && count >= 3)
            {
                return IndexedColor(Parameter(index + 2, 0));
            }

            if (kind == 2 && count >= 5)
            {
                var first = count >= 6 ? index + 3 : index + 2;
                return RgbColor(Parameter(first, 0), Parameter(first + 1, 0), Parameter(first + 2, 0));
            }

            return null;
        }

        var mode = Parameter(index + 1, -1);
        if (mode == 5 && index + 2 < _parameterCount)
        {
            var color = IndexedColor(Parameter(index + 2, 0));
            index += 2;
            return color;
        }

        if (mode == 2 && index + 4 < _parameterCount)
        {
            var color = RgbColor(Parameter(index + 2, 0), Parameter(index + 3, 0), Parameter(index + 4, 0));
            index += 4;
            return color;
        }

        index = _parameterCount;
        return null;
    }

    private static Color? IndexedColor(int value)
        => value is >= 0 and <= 255 ? Color.FromInt32(value) : null;

    private static Color RgbColor(int r, int g, int b)
        => new((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));

    private void SetAttributes(Attributes attributes)
    {
        if (attributes != _attributes)
        {
            _attributes = attributes;
            _styleValid = false;
        }
    }

    private Style? CurrentStyle
    {
        get
        {
            if (!_styleValid)
            {
                _style = _attributes.IsDefault
                    ? null
                    : new Style(_attributes.Foreground, _attributes.Background, _attributes.Decoration, _attributes.Link);
                _styleValid = true;
            }

            return _style;
        }
    }

    private TerminalCell BlankCell
        => _attributes.Background is { } background
            ? new TerminalCell(" ", new Style(background: background))
            : TerminalCell.Empty;

    private void FlushPrint()
    {
        if (_print.Length == 0)
        {
            return;
        }

        var text = _print.ToString();
        _print.Clear();

        if (_pendingHighSurrogate != '\0')
        {
            text = _pendingHighSurrogate + text;
            _pendingHighSurrogate = '\0';
        }

        if (char.IsHighSurrogate(text[^1]))
        {
            _pendingHighSurrogate = text[^1];
            text = text[..^1];
            if (text.Length == 0)
            {
                return;
            }
        }

        // A combining mark or joiner continuing the previous grapheme (for example, split across reads)
        // rewrites that grapheme instead of standing alone.
        if (_lastGrapheme is not null)
        {
            var combined = _lastGrapheme + text;
            if (StringInfo.GetNextTextElementLength(combined) > _lastGrapheme.Length)
            {
                _x = _lastGraphemeX;
                _y = _lastGraphemeY;
                _wrapPending = false;
                text = combined;
            }
        }

        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            PrintGrapheme(elements.GetTextElement());
        }
    }

    private void PrintGrapheme(string grapheme)
    {
        var graphics = _shiftOut ? _g1Graphics : _g0Graphics;
        if (graphics && grapheme.Length == 1)
        {
            grapheme = MapSpecialGraphics(grapheme[0]);
        }

        var width = CellWidth(grapheme);
        if (width == 0 || width > Width)
        {
            return;
        }

        if (_wrapPending && _autoWrap)
        {
            _x = 0;
            LineFeed();
        }

        _wrapPending = false;
        if (_x + width > Width)
        {
            if (_autoWrap)
            {
                _x = 0;
                LineFeed();
            }
            else
            {
                _x = Width - width;
            }
        }

        var row = Screen[_y];
        DetachWideCharacters(row, _x, _x + width);
        var style = CurrentStyle;
        row[_x] = new TerminalCell(grapheme, style);
        for (var i = 1; i < width; i++)
        {
            row[_x + i] = new TerminalCell(string.Empty, style);
        }

        _lastGrapheme = grapheme;
        _lastGraphemeX = _x;
        _lastGraphemeY = _y;
        _x += width;
        if (_x >= Width)
        {
            _x = Width - 1;
            _wrapPending = _autoWrap;
        }
    }

    private static int CellWidth(string grapheme)
    {
        if (grapheme.Length == 1 && grapheme[0] < '\u007f')
        {
            return 1;
        }

        return Segment.CellCount([new Segment(grapheme)]);
    }

    private static string MapSpecialGraphics(char ch) => ch switch
    {
        '`' => "◆",
        'a' => "▒",
        'f' => "°",
        'g' => "±",
        'j' => "┘",
        'k' => "┐",
        'l' => "┌",
        'm' => "└",
        'n' => "┼",
        'o' => "⎺",
        'p' => "⎻",
        'q' => "─",
        'r' => "⎼",
        's' => "⎽",
        't' => "├",
        'u' => "┤",
        'v' => "┴",
        'w' => "┬",
        'x' => "│",
        'y' => "≤",
        'z' => "≥",
        '{' => "π",
        '|' => "≠",
        '}' => "£",
        '~' => "·",
        _ => ch.ToString(),
    };

    private void RepeatLastGrapheme(int count)
    {
        // REP repeats the last printed graphic character; this implementation keeps the last grapheme.
        var row = Screen[_y];
        var x = _x - (_wrapPending ? 0 : 1);
        if (x < 0 || x >= Width || row[x].Text.Length == 0)
        {
            return;
        }

        var grapheme = row[x].Text;
        for (var i = 0; i < Math.Min(count, Width * Height); i++)
        {
            PrintGrapheme(grapheme);
        }
    }

    /// <summary>
    /// Blanks the halves of wide characters that are cut by overwriting or erasing columns [from, to).
    /// </summary>
    private static void DetachWideCharacters(TerminalCell[] row, int from, int to)
    {
        if (from > 0 && from < row.Length && row[from].Text.Length == 0)
        {
            row[from - 1] = new TerminalCell(" ", row[from - 1].Style);
        }

        if (to < row.Length && row[to].Text.Length == 0)
        {
            row[to] = new TerminalCell(" ", row[to].Style);
        }
    }

    private void LineFeed()
    {
        _wrapPending = false;
        if (_y == _bottom)
        {
            ScrollUp(1);
        }
        else if (_y < Height - 1)
        {
            _y++;
        }
    }

    private void ReverseIndex()
    {
        _wrapPending = false;
        if (_y == _top)
        {
            ScrollDown(1);
        }
        else if (_y > 0)
        {
            _y--;
        }
    }

    private void ScrollUp(int count) => ScrollRegion(_top, _bottom, count);

    private void ScrollDown(int count) => ScrollRegion(_top, _bottom, -count);

    /// <summary>
    /// Scrolls rows [top, bottom] up by <paramref name="count"/> (down when negative), filling with blank rows.
    /// </summary>
    private void ScrollRegion(int top, int bottom, int count)
    {
        var screen = Screen;
        var size = bottom - top + 1;
        var amount = Math.Min(Math.Abs(count), size);
        if (amount == 0)
        {
            return;
        }

        var blank = BlankCell;
        if (count > 0)
        {
            for (var y = top; y <= bottom; y++)
            {
                screen[y] = y + amount <= bottom ? screen[y + amount] : CreateRow(Width, blank);
            }
        }
        else
        {
            for (var y = bottom; y >= top; y--)
            {
                screen[y] = y - amount >= top ? screen[y - amount] : CreateRow(Width, blank);
            }
        }
    }

    private void InsertLines(int count)
    {
        if (_y < _top || _y > _bottom)
        {
            return;
        }

        ScrollRegion(_y, _bottom, -count);
        _x = 0;
        _wrapPending = false;
    }

    private void DeleteLines(int count)
    {
        if (_y < _top || _y > _bottom)
        {
            return;
        }

        ScrollRegion(_y, _bottom, count);
        _x = 0;
        _wrapPending = false;
    }

    private void InsertCharacters(int count)
    {
        var row = Screen[_y];
        count = Math.Min(count, Width - _x);
        DetachWideCharacters(row, _x, _x);
        var lastKept = Width - count - 1;
        if (lastKept >= _x && lastKept + 1 < Width && row[lastKept + 1].Text.Length == 0)
        {
            // The wide character that ends up in the last column loses its continuation.
            row[lastKept] = new TerminalCell(" ", row[lastKept].Style);
        }

        Array.Copy(row, _x, row, _x + count, Width - _x - count);
        Array.Fill(row, BlankCell, _x, count);
        _wrapPending = false;
    }

    private void DeleteCharacters(int count)
    {
        var row = Screen[_y];
        count = Math.Min(count, Width - _x);
        DetachWideCharacters(row, _x, _x + count);
        Array.Copy(row, _x + count, row, _x, Width - _x - count);
        Array.Fill(row, BlankCell, Width - count, count);
        _wrapPending = false;
    }

    private void EraseCharacters(int count)
    {
        var row = Screen[_y];
        var end = Math.Min(Width, _x + count);
        DetachWideCharacters(row, _x, end);
        Array.Fill(row, BlankCell, _x, end - _x);
        _wrapPending = false;
    }

    private void EraseInLine(int kind)
    {
        var row = Screen[_y];
        var (from, to) = kind switch
        {
            0 => (_x, Width),
            1 => (0, _x + 1),
            2 => (0, Width),
            _ => (0, 0),
        };
        if (to <= from)
        {
            return;
        }

        DetachWideCharacters(row, from, to);
        Array.Fill(row, BlankCell, from, to - from);
        _wrapPending = false;
    }

    private void EraseInDisplay(int kind)
    {
        switch (kind)
        {
            case 0:
                EraseInLine(0);
                FillRows(Screen, _y + 1, Height - 1);
                break;
            case 1:
                FillRows(Screen, 0, _y - 1);
                EraseInLine(1);
                break;
            case 2:
                FillRows(Screen, 0, Height - 1);
                break;
        }

        _wrapPending = false;
    }

    private void FillRows(TerminalCell[][] screen, int from, int to)
    {
        var blank = BlankCell;
        for (var y = Math.Max(0, from); y <= Math.Min(Height - 1, to); y++)
        {
            Array.Fill(screen[y], blank);
        }
    }

    private void ClearRows(TerminalCell[][] screen, int from, int to)
    {
        for (var y = from; y <= to; y++)
        {
            Array.Fill(screen[y], TerminalCell.Empty);
        }
    }

    private void ScreenAlignment()
    {
        foreach (var row in Screen)
        {
            Array.Fill(row, new TerminalCell("E", null));
        }

        _top = 0;
        _bottom = Height - 1;
        _x = 0;
        _y = 0;
        _wrapPending = false;
    }

    private void MoveTo(int x, int y)
    {
        _x = Math.Clamp(x, 0, Width - 1);
        _y = Math.Clamp(y, 0, Height - 1);
        _wrapPending = false;
    }

    private void CursorUp(int count)
    {
        var limit = _y >= _top ? _top : 0;
        MoveTo(_x, Math.Max(limit, _y - count));
    }

    private void CursorDown(int count)
    {
        var limit = _y <= _bottom ? _bottom : Height - 1;
        MoveTo(_x, Math.Min(limit, _y + count));
    }

    private void SetCursorPosition(int row, int column, bool keepColumn = false)
    {
        var y = _originMode
            ? Math.Clamp(_top + row - 1, _top, _bottom)
            : row - 1;
        MoveTo(keepColumn ? _x : column - 1, y);
    }

    private void SetScrollRegion(int top, int bottom)
    {
        bottom = Math.Min(bottom, Height);
        if (top >= bottom)
        {
            return;
        }

        _top = top - 1;
        _bottom = bottom - 1;
        SetCursorPosition(1, 1);
    }

    private void HorizontalTab(int count)
    {
        _wrapPending = false;
        for (var i = 0; i < count && _x < Width - 1; i++)
        {
            do
            {
                _x++;
            }
            while (_x < Width - 1 && !_tabStops[_x]);
        }
    }

    private void BackTab(int count)
    {
        _wrapPending = false;
        for (var i = 0; i < count && _x > 0; i++)
        {
            do
            {
                _x--;
            }
            while (_x > 0 && !_tabStops[_x]);
        }
    }

    private void ClearTabStops(int kind)
    {
        if (kind == 0)
        {
            _tabStops[_x] = false;
        }
        else if (kind == 3)
        {
            Array.Clear(_tabStops);
        }
    }

    private void SaveCursor()
        => _savedCursors[_alternateActive ? 1 : 0] = new SavedCursor(
            _x, _y, _wrapPending, _attributes, _originMode, _autoWrap, _g0Graphics, _g1Graphics, _shiftOut);

    private void RestoreCursor()
    {
        var saved = _savedCursors[_alternateActive ? 1 : 0];
        _x = Math.Min(saved.X, Width - 1);
        _y = Math.Min(saved.Y, Height - 1);
        _wrapPending = saved.WrapPending && _x == Width - 1;
        SetAttributes(saved.Attributes);
        _originMode = saved.OriginMode;
        _autoWrap = saved.AutoWrap;
        _g0Graphics = saved.G0Graphics;
        _g1Graphics = saved.G1Graphics;
        _shiftOut = saved.ShiftOut;
    }

    private void SoftReset()
    {
        CursorVisible = true;
        _originMode = false;
        _autoWrap = true;
        ApplicationCursorKeys = false;
        _top = 0;
        _bottom = Height - 1;
        _g0Graphics = false;
        _g1Graphics = false;
        _shiftOut = false;
        SetAttributes(default);
        _savedCursors[0] = SavedCursor.Initial;
        _savedCursors[1] = SavedCursor.Initial;
        _wrapPending = false;
    }

    private void FullReset()
    {
        SoftReset();
        ClearRows(_primary, 0, Height - 1);
        ClearRows(_alternate, 0, Height - 1);
        _alternateActive = false;
        _tabStops = CreateTabStops(Width);
        MouseTracking = TerminalMouseTracking.None;
        BracketedPaste = false;
        FocusReporting = false;
        SynchronizedOutput = false;
        _x = 0;
        _y = 0;
    }

    private static TerminalCell[] CreateRow(int width, TerminalCell fill)
    {
        var row = new TerminalCell[width];
        Array.Fill(row, fill);
        return row;
    }

    private static TerminalCell[][] CreateRows(int width, int height)
    {
        var rows = new TerminalCell[height][];
        for (var y = 0; y < height; y++)
        {
            rows[y] = CreateRow(width, TerminalCell.Empty);
        }

        return rows;
    }

    private static TerminalCell[][] ResizeRows(TerminalCell[][] rows, int width, int height)
    {
        var resized = CreateRows(width, height);
        for (var y = 0; y < Math.Min(height, rows.Length); y++)
        {
            var count = Math.Min(width, rows[y].Length);
            Array.Copy(rows[y], resized[y], count);
            if (count > 0 && count < rows[y].Length && rows[y][count].Text.Length == 0)
            {
                // A wide character cut by the new right edge.
                resized[y][count - 1] = TerminalCell.Empty;
            }
        }

        return resized;
    }

    private static bool[] CreateTabStops(int width)
    {
        var stops = new bool[width];
        for (var x = 8; x < width; x += 8)
        {
            stops[x] = true;
        }

        return stops;
    }

    private readonly record struct Attributes(Color? Foreground, Color? Background, Decoration Decoration, string? Link)
    {
        public bool IsDefault => Foreground is null && Background is null && Decoration == Decoration.None && Link is null;

        public Attributes With(Decoration decoration) => this with { Decoration = Decoration | decoration };

        public Attributes Without(Decoration decoration) => this with { Decoration = Decoration & ~decoration };
    }

    private readonly record struct SavedCursor(
        int X,
        int Y,
        bool WrapPending,
        Attributes Attributes,
        bool OriginMode,
        bool AutoWrap,
        bool G0Graphics,
        bool G1Graphics,
        bool ShiftOut)
    {
        public static SavedCursor Initial { get; } = new(0, 0, false, default, false, true, false, false, false);
    }
}
