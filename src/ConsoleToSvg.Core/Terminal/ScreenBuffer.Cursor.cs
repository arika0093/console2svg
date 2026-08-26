using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleToSvg.Terminal;

public sealed partial class ScreenBuffer
{
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

    public void MoveCursorToOriginRelative(int row, int col) =>
        MoveCursorTo(_originMode ? _scrollTop + row : row, col);

    public void SetOriginMode(bool enabled)
    {
        _originMode = enabled;
        MoveCursorTo(enabled ? _scrollTop : 0, 0);
    }

    public void SetInsertMode(bool enabled) => _insertMode = enabled;

    public void SetCursorVisible(bool visible) => _cursorVisible = visible;

    public void SetTabStop() => _tabStops.Add(CursorCol);

    public void ClearTabStops(int mode)
    {
        if (mode == 3)
        {
            _tabStops.Clear();
        }
        else
        {
            _tabStops.Remove(CursorCol);
        }
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
        Index();
    }

    public void Index()
    {
        _pendingWrap = false;
        if (CursorRow == _scrollBottom)
        {
            ScrollRegionUp(
                _scrollTop,
                _scrollBottom,
                1,
                includeScrollback: !_isAltScreen && _scrollTop == 0 && _scrollBottom == Height - 1
            );
            return;
        }

        CursorRow = Math.Min(Height - 1, CursorRow + 1);
    }

    public void NextLine()
    {
        CarriageReturn();
        Index();
    }

    public void ReverseIndex()
    {
        _pendingWrap = false;
        if (CursorRow == _scrollTop)
        {
            ScrollRegionDown(_scrollTop, _scrollBottom, 1);
            return;
        }

        CursorRow = Math.Max(0, CursorRow - 1);
    }

    public void SetScrollRegion(int top, int bottom)
    {
        top = Clamp(top, 0, Height - 1);
        bottom = Clamp(bottom, 0, Height - 1);

        if (top >= bottom)
        {
            _scrollTop = 0;
            _scrollBottom = Height - 1;
        }
        else
        {
            _scrollTop = top;
            _scrollBottom = bottom;
        }

        MoveCursorTo(_originMode ? _scrollTop : 0, 0);
    }

    public void Backspace()
    {
        CursorCol = Math.Max(0, CursorCol - 1);
    }

    public void ClearLine(int mode, TextStyle? style = null)
    {
        var eraseStyle = style ?? DefaultStyle;
        switch (mode)
        {
            case 1:
                for (var col = 0; col <= CursorCol; col++)
                {
                    SetCell(CursorRow, col, CreateCell(" ", eraseStyle));
                }

                return;
            case 2:
                for (var col = 0; col < Width; col++)
                {
                    SetCell(CursorRow, col, CreateCell(" ", eraseStyle));
                }

                return;
            default:
                for (var col = CursorCol; col < Width; col++)
                {
                    SetCell(CursorRow, col, CreateCell(" ", eraseStyle));
                }

                return;
        }
    }

    public void DeleteCharacters(int count, TextStyle? style = null)
    {
        if (count <= 0)
        {
            return;
        }

        var eraseStyle = style ?? DefaultStyle;

        count = Math.Min(count, Width - CursorCol);

        // Shift remaining cells in the row to the left
        for (var col = CursorCol; col < Width - count; col++)
        {
            SetCell(CursorRow, col, _cells[CursorRow][col + count]);
        }

        // Fill vacated cells on the right with blanks
        for (var col = Width - count; col < Width; col++)
        {
            SetCell(CursorRow, col, CreateCell(" ", eraseStyle));
        }
    }

    public void InsertBlankCharacters(int count, TextStyle? style = null)
    {
        if (count <= 0)
        {
            return;
        }

        var eraseStyle = style ?? DefaultStyle;

        count = Math.Min(count, Width - CursorCol);

        for (var col = Width - 1; col >= CursorCol + count; col--)
        {
            SetCell(CursorRow, col, _cells[CursorRow][col - count]);
        }

        for (var col = CursorCol; col < CursorCol + count; col++)
        {
            SetCell(CursorRow, col, CreateCell(" ", eraseStyle));
        }
    }

    public void InsertLines(int count)
    {
        if (count <= 0 || CursorRow < _scrollTop || CursorRow > _scrollBottom)
        {
            return;
        }

        ScrollRegionDown(CursorRow, _scrollBottom, count);
    }

    public void DeleteLines(int count)
    {
        if (count <= 0 || CursorRow < _scrollTop || CursorRow > _scrollBottom)
        {
            return;
        }

        ScrollRegionUp(CursorRow, _scrollBottom, count, includeScrollback: false);
    }

    public void ScrollUpLines(int count)
    {
        ScrollRegionUp(
            _scrollTop,
            _scrollBottom,
            count,
            includeScrollback: !_isAltScreen && _scrollTop == 0 && _scrollBottom == Height - 1
        );
    }

    public void ScrollDownLines(int count)
    {
        ScrollRegionDown(_scrollTop, _scrollBottom, count);
    }

    public void EraseChars(int count, TextStyle? style = null)
    {
        if (count <= 0)
        {
            return;
        }

        var eraseStyle = style ?? DefaultStyle;

        var endCol = Math.Min(Width - 1, CursorCol + count - 1);
        for (var col = CursorCol; col <= endCol; col++)
        {
            var cell = _cells[CursorRow][col];
            if (cell.IsWideContinuation && col > 0)
            {
                SetCell(CursorRow, col - 1, CreateCell(" ", eraseStyle));
            }

            if (cell.IsWide && col + 1 < Width)
            {
                SetCell(CursorRow, col + 1, CreateCell(" ", eraseStyle));
            }

            SetCell(CursorRow, col, CreateCell(" ", eraseStyle));
        }
    }

    public void ClearDisplay(int mode, TextStyle? style = null)
    {
        var eraseStyle = style ?? DefaultStyle;
        switch (mode)
        {
            case 1:
                for (var row = 0; row <= CursorRow; row++)
                {
                    var end = row == CursorRow ? CursorCol : Width - 1;
                    for (var col = 0; col <= end; col++)
                    {
                        SetCell(row, col, CreateCell(" ", eraseStyle));
                    }
                }

                return;
            case 2:
                for (var row = 0; row < Height; row++)
                {
                    for (var col = 0; col < Width; col++)
                    {
                        SetCell(row, col, CreateCell(" ", eraseStyle));
                    }
                }

                return;
            default:
                for (var row = CursorRow; row < Height; row++)
                {
                    var start = row == CursorRow ? CursorCol : 0;
                    for (var col = start; col < Width; col++)
                    {
                        SetCell(row, col, CreateCell(" ", eraseStyle));
                    }
                }

                return;
        }
    }
}
