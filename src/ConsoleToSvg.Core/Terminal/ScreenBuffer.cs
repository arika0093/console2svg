using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleToSvg.Terminal;

public readonly record struct TextStyle(
    string Foreground,
    string Background,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Reversed = false,
    bool Faint = false,
    bool Hidden = false,
    bool Strikethrough = false,
    bool Overline = false,
    bool Blink = false,
    string? UnderlineColor = null
);

public readonly struct ScreenCell
{
    public ScreenCell(
        string text,
        TextStyle style,
        bool isWide = false,
        bool isWideContinuation = false
    )
    {
        Text = text;
        Foreground = style.Foreground;
        Background = style.Background;
        Bold = style.Bold;
        Italic = style.Italic;
        Underline = style.Underline;
        Reversed = style.Reversed;
        Faint = style.Faint;
        Hidden = style.Hidden;
        Strikethrough = style.Strikethrough;
        Overline = style.Overline;
        Blink = style.Blink;
        UnderlineColor = style.UnderlineColor;
        IsWide = isWide;
        IsWideContinuation = isWideContinuation;
    }

    public string Text { get; }

    public string Foreground { get; }

    public string Background { get; }

    public bool Bold { get; }

    public bool Italic { get; }

    public bool Underline { get; }

    public bool Reversed { get; }

    public bool Faint { get; }
    public bool Hidden { get; }
    public bool Strikethrough { get; }
    public bool Overline { get; }
    public bool Blink { get; }
    public string? UnderlineColor { get; }

    public bool IsWide { get; }

    public bool IsWideContinuation { get; }

    public TextStyle ToTextStyle() =>
        new TextStyle(
            Foreground,
            Background,
            Bold,
            Italic,
            Underline,
            Reversed,
            Faint,
            Hidden,
            Strikethrough,
            Overline,
            Blink,
            UnderlineColor
        );
}

public sealed partial class ScreenBuffer
{
    private readonly Theme _theme;
    private ScreenCell[,] _mainCells;
    private ScreenCell[,] _altCells;
    private ScreenCell[,] _cells;
    private bool _isAltScreen;
    private int _savedRow;
    private int _savedCol;
    private int _savedMainRow;
    private int _savedMainCol;
    private int _scrollTop;
    private int _scrollBottom;
    private bool _pendingWrap;
    private bool _originMode;
    private bool _insertMode;
    private readonly SortedSet<int> _tabStops = new();
    private readonly List<ScreenCell[]> _scrollbackRows = new();

    public ScreenBuffer(int width, int height, Theme theme)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _theme = theme;
        DefaultStyle = new TextStyle(theme.Foreground, theme.Background, false, false, false);

        _mainCells = CreateBlankCells();
        _altCells = CreateBlankCells();
        _cells = _mainCells;
        _scrollTop = 0;
        _scrollBottom = Height - 1;
        CursorRow = 0;
        CursorCol = 0;
        for (var col = 8; col < Width; col += 8)
        {
            _tabStops.Add(col);
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int CursorRow { get; private set; }

    public int CursorCol { get; private set; }

    public TextStyle DefaultStyle { get; }

    public bool OriginMode => _originMode;

    public int ScrollbackCount => _scrollbackRows.Count;

    public int TotalHeight => _scrollbackRows.Count + Height;

    /// <summary>
    /// Returns a stable signature of the visible terminal state. This is useful
    /// when consecutive video samples show the same screen and can reuse an
    /// already-rendered SVG or PNG.
    /// </summary>
    public ulong GetVisualSignature()
    {
        const ulong fnvOffset = 1469598103934665603UL;
        const ulong fnvPrime = 1099511628211UL;

        var signature = fnvOffset;
        AddInt(CursorRow);
        AddInt(CursorCol);

        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                var cell = GetCell(row, col);
                AddString(cell.Text);
                AddString(cell.Foreground);
                AddString(cell.Background);
                AddBool(cell.Bold);
                AddBool(cell.Italic);
                AddBool(cell.Underline);
                AddBool(cell.Reversed);
                AddBool(cell.Faint);
                AddBool(cell.Hidden);
                AddBool(cell.Strikethrough);
                AddBool(cell.Overline);
                AddBool(cell.Blink);
                AddString(cell.UnderlineColor ?? string.Empty);
                AddBool(cell.IsWide);
                AddBool(cell.IsWideContinuation);
            }
        }

        return signature;

        void AddString(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                signature ^= value[i];
                signature *= fnvPrime;
            }
            signature ^= 0xFF;
            signature *= fnvPrime;
        }

        void AddBool(bool value)
        {
            signature ^= value ? (byte)1 : (byte)0;
            signature *= fnvPrime;
        }

        void AddInt(int value)
        {
            unchecked
            {
                signature ^= (byte)value;
                signature *= fnvPrime;
                signature ^= (byte)(value >> 8);
                signature *= fnvPrime;
                signature ^= (byte)(value >> 16);
                signature *= fnvPrime;
                signature ^= (byte)(value >> 24);
                signature *= fnvPrime;
            }
        }
    }

    public ScreenCell GetCell(int row, int col)
    {
        if (row < 0 || row >= Height || col < 0 || col >= Width)
        {
            return new ScreenCell(" ", DefaultStyle);
        }

        return _cells[row, col];
    }

    public ScreenCell GetScrollbackCell(int scrollbackRow, int col)
    {
        if (scrollbackRow < 0 || scrollbackRow >= _scrollbackRows.Count || col < 0 || col >= Width)
        {
            return new ScreenCell(" ", DefaultStyle);
        }

        return _scrollbackRows[scrollbackRow][col];
    }

    public ScreenCell GetCellFromTop(int row, int col)
    {
        if (row < _scrollbackRows.Count)
        {
            return GetScrollbackCell(row, col);
        }

        return GetCell(row - _scrollbackRows.Count, col);
    }

    public ScreenBuffer Clone()
    {
        var cloned = new ScreenBuffer(Width, Height, _theme)
        {
            CursorRow = CursorRow,
            CursorCol = CursorCol,
            _savedRow = _savedRow,
            _savedCol = _savedCol,
            _savedMainRow = _savedMainRow,
            _savedMainCol = _savedMainCol,
            _isAltScreen = _isAltScreen,
            _scrollTop = _scrollTop,
            _scrollBottom = _scrollBottom,
            _pendingWrap = _pendingWrap,
            _originMode = _originMode,
            _insertMode = _insertMode,
            _mainCells = CloneCells(_mainCells),
            _altCells = CloneCells(_altCells),
        };
        cloned._tabStops.Clear();
        cloned._tabStops.UnionWith(_tabStops);

        cloned._cells = cloned._isAltScreen ? cloned._altCells : cloned._mainCells;
        return cloned;
    }

    public void PutChar(char value, TextStyle style)
    {
        if (value == '\n')
        {
            LineFeed();
            return;
        }

        if (value == '\r')
        {
            CarriageReturn();
            return;
        }

        if (value == '\b')
        {
            Backspace();
            return;
        }

        if (value == '\t')
        {
            var nextStop = _tabStops
                .Where(stop => stop > CursorCol)
                .DefaultIfEmpty(Width - 1)
                .First();

            var spaces = Math.Max(1, nextStop - CursorCol);
            for (var i = 0; i < spaces; i++)
            {
                PutPrintable(" ", style);
            }

            return;
        }

        if (char.IsControl(value))
        {
            return;
        }

        PutPrintable(value.ToString(), style);
    }

    public void PutSurrogatePair(string cluster, TextStyle style)
    {
        PutPrintable(cluster, style);
    }

    public void RepeatPreviousCharacter(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var row = CursorRow;
        var col = _pendingWrap ? CursorCol : CursorCol - 1;
        if (col < 0)
        {
            return;
        }

        if (_cells[row, col].IsWideContinuation && col > 0)
        {
            col--;
        }

        var previous = _cells[row, col];
        if (previous.IsWideContinuation)
        {
            return;
        }

        var style = previous.ToTextStyle();
        for (var i = 0; i < count; i++)
        {
            PutPrintable(previous.Text, style);
        }
    }

    public void AppendToPreviousCell(string combining)
    {
        // Find the previous printable cell
        var col = CursorCol - 1;
        var row = CursorRow;
        if (col < 0)
        {
            if (row == 0)
            {
                return;
            }

            row--;
            col = Width - 1;
        }

        // If it's a wide continuation, step back to the actual wide cell
        if (_cells[row, col].IsWideContinuation && col > 0)
        {
            col--;
        }

        var prev = _cells[row, col];
        if (prev.Text == " ")
        {
            return;
        }

        _cells[row, col] = new ScreenCell(
            prev.Text + combining,
            prev.ToTextStyle(),
            prev.IsWide,
            prev.IsWideContinuation
        );

        if (combining == "\uFE0F" && !prev.IsWide && !prev.IsWideContinuation)
        {
            TryPromoteCellToWide(row, col);
        }
    }

    private void TryPromoteCellToWide(int row, int col)
    {
        if (col + 1 >= Width)
        {
            return;
        }

        var next = _cells[row, col + 1];
        if (next.Text != " " || next.IsWideContinuation)
        {
            return;
        }

        var cell = _cells[row, col];
        _cells[row, col] = new ScreenCell(
            cell.Text,
            cell.ToTextStyle(),
            isWide: true,
            isWideContinuation: false
        );
        _cells[row, col + 1] = new ScreenCell(
            " ",
            cell.ToTextStyle(),
            isWide: false,
            isWideContinuation: true
        );

        if (CursorRow == row && CursorCol == col + 1)
        {
            CursorCol++;
            if (CursorCol >= Width)
            {
                _pendingWrap = true;
                CursorCol = Width - 1;
            }
        }
    }

    private void PutPrintable(string text, TextStyle style)
    {
        // Apply any pending wrap from the previous character filling the last column
        if (_pendingWrap)
        {
            _pendingWrap = false;
            CursorCol = 0;
            Index();
        }

        var isWide = IsWideCharacter(text);
        if (_insertMode)
        {
            InsertBlankCharacters(isWide ? 2 : 1, style);
        }

        if (isWide && CursorCol + 1 >= Width)
        {
            _cells[CursorRow, CursorCol] = new ScreenCell(" ", DefaultStyle);
            CursorCol = 0;
            Index();
        }

        _cells[CursorRow, CursorCol] = new ScreenCell(text, style, isWide);
        CursorCol++;

        if (isWide && CursorCol < Width)
        {
            _cells[CursorRow, CursorCol] = new ScreenCell(" ", style, false, true);
            CursorCol++;
        }

        if (CursorCol >= Width)
        {
            // Deferred wrap: keep cursor at last column, wrap on next printable char
            _pendingWrap = true;
            CursorCol = Width - 1;
        }
    }

    private static bool IsWideCharacter(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int codePoint;
        if (text.Length >= 2 && char.IsHighSurrogate(text[0]) && char.IsLowSurrogate(text[1]))
        {
            codePoint = char.ConvertToUtf32(text[0], text[1]);
        }
        else if (text.Length == 1)
        {
            codePoint = text[0];
        }
        else
        {
            return false;
        }

        return IsEastAsianWide(codePoint) || IsBmpEmojiWide(codePoint);
    }

    private static bool IsBmpEmojiWide(int cp) =>
        cp
            is 0x2611 // ☑
                or 0x2705 // ✅
                or 0x274C // ❌
                or 0x2753 // ❓
                or 0x2754 // ❔
                or 0x2755 // ❕
                or 0x2757; // ❗

    private static bool IsEastAsianWide(int cp) =>
        cp
            is (>= 0x1100 and <= 0x115F)
                or (>= 0x2E80 and <= 0x2FFD)
                or (>= 0x3000 and <= 0x303F)
                or (>= 0x3040 and <= 0x33FF)
                or (>= 0x3400 and <= 0x4DBF)
                or (>= 0x4E00 and <= 0x9FFF)
                or (>= 0xA000 and <= 0xA48C)
                or (>= 0xA960 and <= 0xA97F)
                or (>= 0xAC00 and <= 0xD7A3)
                or (>= 0xF900 and <= 0xFAFF)
                or (>= 0xFE10 and <= 0xFE1F)
                or (>= 0xFE30 and <= 0xFE6F)
                or (>= 0xFF01 and <= 0xFF60)
                or (>= 0xFFE0 and <= 0xFFE6)
                or (>= 0x1B000 and <= 0x1B0FF)
                or (>= 0x1F004 and <= 0x1F004)
                or (>= 0x1F0CF and <= 0x1F0CF)
                or (>= 0x1F200 and <= 0x1F2FF)
                or (>= 0x1F300 and <= 0x1F64F)
                or (>= 0x1F680 and <= 0x1F6FF)
                or (>= 0x1F900 and <= 0x1F9FF)
                or (>= 0x20000 and <= 0x2FFFD)
                or (>= 0x30000 and <= 0x3FFFD);
}
