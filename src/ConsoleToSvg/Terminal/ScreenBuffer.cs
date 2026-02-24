using System;

namespace ConsoleToSvg.Terminal;

public readonly struct TextStyle
{
    public TextStyle(string foreground, string background, bool bold, bool italic, bool underline)
    {
        Foreground = foreground;
        Background = background;
        Bold = bold;
        Italic = italic;
        Underline = underline;
    }

    public string Foreground { get; }

    public string Background { get; }

    public bool Bold { get; }

    public bool Italic { get; }

    public bool Underline { get; }
}

public readonly struct ScreenCell
{
    public ScreenCell(char character, TextStyle style)
    {
        Character = character;
        Foreground = style.Foreground;
        Background = style.Background;
        Bold = style.Bold;
        Italic = style.Italic;
        Underline = style.Underline;
    }

    public char Character { get; }

    public string Foreground { get; }

    public string Background { get; }

    public bool Bold { get; }

    public bool Italic { get; }

    public bool Underline { get; }
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

    public ScreenCell GetCell(int row, int col)
    {
        if (row < 0 || row >= Height || col < 0 || col >= Width)
        {
            return new ScreenCell(' ', DefaultStyle);
        }

        return _cells[row, col];
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
                PutChar(' ', style);
            }

            return;
        }

        if (char.IsControl(value))
        {
            return;
        }

        _cells[CursorRow, CursorCol] = new ScreenCell(value, style);
        CursorCol++;
        if (CursorCol >= Width)
        {
            CursorCol = 0;
            CursorRow++;
            if (CursorRow >= Height)
            {
                ScrollUp(1);
                CursorRow = Height - 1;
            }
        }
    }

    public void MoveCursorTo(int row, int col)
    {
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
        CursorCol = 0;
    }

    public void LineFeed()
    {
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
                    _cells[CursorRow, col] = new ScreenCell(' ', DefaultStyle);
                }

                return;
            case 2:
                for (var col = 0; col < Width; col++)
                {
                    _cells[CursorRow, col] = new ScreenCell(' ', DefaultStyle);
                }

                return;
            default:
                for (var col = CursorCol; col < Width; col++)
                {
                    _cells[CursorRow, col] = new ScreenCell(' ', DefaultStyle);
                }

                return;
        }
    }

    public void ClearDisplay(int mode)
    {
        switch (mode)
        {
            case 1:
                for (var row = 0; row <= CursorRow; row++)
                {
                    var start = row == CursorRow ? 0 : 0;
                    var end = row == CursorRow ? CursorCol : Width - 1;
                    for (var col = start; col <= end; col++)
                    {
                        _cells[row, col] = new ScreenCell(' ', DefaultStyle);
                    }
                }

                return;
            case 2:
                for (var row = 0; row < Height; row++)
                {
                    for (var col = 0; col < Width; col++)
                    {
                        _cells[row, col] = new ScreenCell(' ', DefaultStyle);
                    }
                }

                return;
            default:
                for (var row = CursorRow; row < Height; row++)
                {
                    var start = row == CursorRow ? CursorCol : 0;
                    for (var col = start; col < Width; col++)
                    {
                        _cells[row, col] = new ScreenCell(' ', DefaultStyle);
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
            for (var row = 1; row < Height; row++)
            {
                for (var col = 0; col < Width; col++)
                {
                    _cells[row - 1, col] = _cells[row, col];
                }
            }

            for (var col = 0; col < Width; col++)
            {
                _cells[Height - 1, col] = new ScreenCell(' ', DefaultStyle);
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
                cells[row, col] = new ScreenCell(' ', DefaultStyle);
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
