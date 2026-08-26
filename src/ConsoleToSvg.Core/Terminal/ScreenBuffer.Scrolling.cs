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
            _altRowsShared = new bool[Height];
            _cells = _altCells;
            _rowsShared = _altRowsShared;
            CursorRow = 0;
            CursorCol = 0;
            _scrollTop = 0;
            _scrollBottom = Height - 1;
            _isAltScreen = true;
            ResetRowVisualSignatures();
            return;
        }

        if (!_isAltScreen)
        {
            return;
        }

        _cells = _mainCells;
        _rowsShared = _mainRowsShared;
        _isAltScreen = false;
        CursorRow = Clamp(_savedMainRow, 0, Height - 1);
        CursorCol = Clamp(_savedMainCol, 0, Width - 1);
        _scrollTop = 0;
        _scrollBottom = Height - 1;
        ResetRowVisualSignatures();
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
            var topRowShared = _rowsShared[top];
            var topSignature = _rowSignatures[top];
            var topDirty = _rowSignatureDirty[top];
            if (includeScrollback && top == 0)
            {
                _scrollbackRows.Add(topRow);
            }

            for (var row = top + 1; row <= bottom; row++)
            {
                _cells[row - 1] = _cells[row];
                _rowsShared[row - 1] = _rowsShared[row];
                _rowSignatures[row - 1] = _rowSignatures[row];
                _rowSignatureDirty[row - 1] = _rowSignatureDirty[row];
            }

            _cells[bottom] = includeScrollback && top == 0 ? CreateBlankRow() : topRow;
            _rowsShared[bottom] = !(includeScrollback && top == 0) && topRowShared;
            _rowSignatures[bottom] = topSignature;
            _rowSignatureDirty[bottom] = (includeScrollback && top == 0) || topDirty;
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
            var bottomRowShared = _rowsShared[bottom];
            var bottomSignature = _rowSignatures[bottom];
            var bottomDirty = _rowSignatureDirty[bottom];
            for (var row = bottom - 1; row >= top; row--)
            {
                _cells[row + 1] = _cells[row];
                _rowsShared[row + 1] = _rowsShared[row];
                _rowSignatures[row + 1] = _rowSignatures[row];
                _rowSignatureDirty[row + 1] = _rowSignatureDirty[row];
            }

            _cells[top] = bottomRow;
            _rowsShared[top] = bottomRowShared;
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
