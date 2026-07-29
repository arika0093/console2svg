using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleToSvg.Terminal;

public sealed partial class ScreenBuffer
{
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
            _scrollTop = 0;
            _scrollBottom = Height - 1;
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
        _scrollTop = 0;
        _scrollBottom = Height - 1;
    }

    private void ScrollRegionUp(int top, int bottom, int count, bool includeScrollback)
    {
        if (count <= 0 || top < 0 || bottom >= Height || top > bottom)
        {
            return;
        }

        count = Math.Min(count, bottom - top + 1);
        for (var i = 0; i < count; i++)
        {
            if (includeScrollback && top == 0)
            {
                var topRow = new ScreenCell[Width];
                for (var col = 0; col < Width; col++)
                {
                    topRow[col] = _cells[0, col];
                }

                _scrollbackRows.Add(topRow);
            }

            for (var row = top + 1; row <= bottom; row++)
            {
                for (var col = 0; col < Width; col++)
                {
                    _cells[row - 1, col] = _cells[row, col];
                }
            }

            ClearRow(bottom);
        }
    }

    private void ScrollRegionDown(int top, int bottom, int count)
    {
        if (count <= 0 || top < 0 || bottom >= Height || top > bottom)
        {
            return;
        }

        count = Math.Min(count, bottom - top + 1);
        for (var i = 0; i < count; i++)
        {
            for (var row = bottom - 1; row >= top; row--)
            {
                for (var col = 0; col < Width; col++)
                {
                    _cells[row + 1, col] = _cells[row, col];
                }
            }

            ClearRow(top);
        }
    }

    private void ClearRow(int row)
    {
        for (var col = 0; col < Width; col++)
        {
            _cells[row, col] = new ScreenCell(" ", DefaultStyle);
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
