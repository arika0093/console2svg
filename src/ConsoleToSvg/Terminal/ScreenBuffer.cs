using System;
using System.Collections.Generic;

namespace ConsoleToSvg.Terminal;

public readonly record struct TextStyle(
    string Foreground,
    string Background,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Reversed = false,
    bool Faint = false
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

    public bool IsWide { get; }

    public bool IsWideContinuation { get; }

    public TextStyle ToTextStyle() =>
        new TextStyle(Foreground, Background, Bold, Italic, Underline, Reversed, Faint);
}

public sealed class ScreenBuffer
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
    private bool _pendingWrap;
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
        CursorRow = 0;
        CursorCol = 0;
    }

    public int Width { get; }

    public int Height { get; }

    public int CursorRow { get; private set; }

    public int CursorCol { get; private set; }

    public TextStyle DefaultStyle { get; }

    public int ScrollbackCount => _scrollbackRows.Count;

    public int TotalHeight => _scrollbackRows.Count + Height;

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
            _pendingWrap = _pendingWrap,
            _mainCells = CloneCells(_mainCells),
            _altCells = CloneCells(_altCells),
        };

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
            var nextStop = ((CursorCol / 8) + 1) * 8;
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
            CursorRow++;
            if (CursorRow >= Height)
            {
                ScrollUp(1);
                CursorRow = Height - 1;
            }
        }

        var isWide = IsWideCharacter(text);

        if (isWide && CursorCol + 1 >= Width)
        {
            _cells[CursorRow, CursorCol] = new ScreenCell(" ", DefaultStyle);
            CursorCol = 0;
            CursorRow++;
            if (CursorRow >= Height)
            {
                ScrollUp(1);
                CursorRow = Height - 1;
            }
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

    public void MoveCursorTo(int row, int col)
    {
        _pendingWrap = false;
        CursorRow = Clamp(row, 0, Height - 1);
        CursorCol = Clamp(col, 0, Width - 1);
    }

    public void MoveCursorBy(int rowDelta, int colDelta)
    {
        MoveCursorTo(CursorRow + rowDelta, CursorCol + colDelta);
    }

    public void SaveCursor()
    {
        _savedRow = CursorRow;
        _savedCol = CursorCol;
    }

    public void RestoreCursor()
    {
        MoveCursorTo(_savedRow, _savedCol);
    }

    public void CarriageReturn()
    {
        _pendingWrap = false;
        CursorCol = 0;
    }

    public void LineFeed()
    {
        _pendingWrap = false;
        CursorRow++;
        if (CursorRow >= Height)
        {
            ScrollUp(1);
            CursorRow = Height - 1;
        }
    }

    public void Backspace()
    {
        CursorCol = Math.Max(0, CursorCol - 1);
    }

    public void ClearLine(int mode)
    {
        switch (mode)
        {
            case 1:
                for (var col = 0; col <= CursorCol; col++)
                {
                    _cells[CursorRow, col] = new ScreenCell(" ", DefaultStyle);
                }

                return;
            case 2:
                for (var col = 0; col < Width; col++)
                {
                    _cells[CursorRow, col] = new ScreenCell(" ", DefaultStyle);
                }

                return;
            default:
                for (var col = CursorCol; col < Width; col++)
                {
                    _cells[CursorRow, col] = new ScreenCell(" ", DefaultStyle);
                }

                return;
        }
    }

    public void DeleteCharacters(int count)
    {
        if (count <= 0)
        {
            return;
        }

        count = Math.Min(count, Width - CursorCol);

        // Shift remaining cells in the row to the left
        for (var col = CursorCol; col < Width - count; col++)
        {
            _cells[CursorRow, col] = _cells[CursorRow, col + count];
        }

        // Fill vacated cells on the right with blanks
        for (var col = Width - count; col < Width; col++)
        {
            _cells[CursorRow, col] = new ScreenCell(" ", DefaultStyle);
        }
    }

    public void EraseChars(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var endCol = Math.Min(Width - 1, CursorCol + count - 1);
        for (var col = CursorCol; col <= endCol; col++)
        {
            var cell = _cells[CursorRow, col];
            if (cell.IsWideContinuation && col > 0)
            {
                _cells[CursorRow, col - 1] = new ScreenCell(" ", DefaultStyle);
            }

            if (cell.IsWide && col + 1 < Width)
            {
                _cells[CursorRow, col + 1] = new ScreenCell(" ", DefaultStyle);
            }

            _cells[CursorRow, col] = new ScreenCell(" ", DefaultStyle);
        }
    }

    public void ClearDisplay(int mode)
    {
        switch (mode)
        {
            case 1:
                for (var row = 0; row <= CursorRow; row++)
                {
                    var end = row == CursorRow ? CursorCol : Width - 1;
                    for (var col = 0; col <= end; col++)
                    {
                        _cells[row, col] = new ScreenCell(" ", DefaultStyle);
                    }
                }

                return;
            case 2:
                for (var row = 0; row < Height; row++)
                {
                    for (var col = 0; col < Width; col++)
                    {
                        _cells[row, col] = new ScreenCell(" ", DefaultStyle);
                    }
                }

                return;
            default:
                for (var row = CursorRow; row < Height; row++)
                {
                    var start = row == CursorRow ? CursorCol : 0;
                    for (var col = start; col < Width; col++)
                    {
                        _cells[row, col] = new ScreenCell(" ", DefaultStyle);
                    }
                }

                return;
        }
    }

    public void SetAlternateScreen(bool enabled)
    {
        if (enabled)
        {
            if (_isAltScreen)
            {
                return;
            }

            _savedMainRow = CursorRow;
            _savedMainCol = CursorCol;
            _altCells = CreateBlankCells();
            _cells = _altCells;
            CursorRow = 0;
            CursorCol = 0;
            _isAltScreen = true;
            return;
        }

        if (!_isAltScreen)
        {
            return;
        }

        _cells = _mainCells;
        _isAltScreen = false;
        CursorRow = Clamp(_savedMainRow, 0, Height - 1);
        CursorCol = Clamp(_savedMainCol, 0, Width - 1);
    }

    private void ScrollUp(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var topRow = new ScreenCell[Width];
            for (var col = 0; col < Width; col++)
            {
                topRow[col] = _cells[0, col];
            }

            _scrollbackRows.Add(topRow);

            for (var row = 1; row < Height; row++)
            {
                for (var col = 0; col < Width; col++)
                {
                    _cells[row - 1, col] = _cells[row, col];
                }
            }

            for (var col = 0; col < Width; col++)
            {
                _cells[Height - 1, col] = new ScreenCell(" ", DefaultStyle);
            }
        }
    }

    private ScreenCell[,] CreateBlankCells()
    {
        var cells = new ScreenCell[Height, Width];
        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                cells[row, col] = new ScreenCell(" ", DefaultStyle);
            }
        }

        return cells;
    }

    private static ScreenCell[,] CloneCells(ScreenCell[,] source)
    {
        var cloned = new ScreenCell[source.GetLength(0), source.GetLength(1)];
        for (var row = 0; row < source.GetLength(0); row++)
        {
            for (var col = 0; col < source.GetLength(1); col++)
            {
                cloned[row, col] = source[row, col];
            }
        }

        return cloned;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
