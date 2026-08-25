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
            Array.Fill(_rowSignatureDirty, true);
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
        Array.Fill(_rowSignatureDirty, true);
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
            var topRow = _cells[top];
            var topSignature = _rowSignatures[top];
            var topDirty = _rowSignatureDirty[top];
            if (includeScrollback && top == 0)
            {
                _scrollbackRows.Add(topRow);
            }

            for (var row = top + 1; row <= bottom; row++)
            {
                _cells[row - 1] = _cells[row];
                _rowSignatures[row - 1] = _rowSignatures[row];
                _rowSignatureDirty[row - 1] = _rowSignatureDirty[row];
            }

            _cells[bottom] = includeScrollback && top == 0 ? CreateBlankRow() : topRow;
            _rowSignatures[bottom] = topSignature;
            _rowSignatureDirty[bottom] = topDirty;
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
            var bottomRow = _cells[bottom];
            var bottomSignature = _rowSignatures[bottom];
            var bottomDirty = _rowSignatureDirty[bottom];
            for (var row = bottom - 1; row >= top; row--)
            {
                _cells[row + 1] = _cells[row];
                _rowSignatures[row + 1] = _rowSignatures[row];
                _rowSignatureDirty[row + 1] = _rowSignatureDirty[row];
            }

            _cells[top] = bottomRow;
            _rowSignatures[top] = bottomSignature;
            _rowSignatureDirty[top] = bottomDirty;
            ClearRow(top);
        }
    }

    private void ClearRow(int row)
    {
        for (var col = 0; col < Width; col++)
        {
            SetCell(row, col, CreateCell(" ", DefaultStyle));
        }
    }

    private ScreenCell[][] CreateBlankCells()
    {
        var cells = new ScreenCell[Height][];
        for (var row = 0; row < Height; row++)
        {
            cells[row] = CreateBlankRow();
        }

        return cells;
    }

    private ScreenCell[] CreateBlankRow()
    {
        var row = new ScreenCell[Width];
        for (var col = 0; col < Width; col++)
        {
            row[col] = CreateCell(" ", DefaultStyle);
        }

        return row;
    }

    private static ScreenCell[][] CloneCells(ScreenCell[][] source)
    {
        var cloned = new ScreenCell[source.Length][];
        for (var row = 0; row < source.Length; row++)
        {
            cloned[row] = new ScreenCell[source[row].Length];
            Array.Copy(source[row], cloned[row], source[row].Length);
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
