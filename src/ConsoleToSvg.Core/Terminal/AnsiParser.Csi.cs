using System;
using System.Buffers;

namespace ConsoleToSvg.Terminal;

public sealed partial class AnsiParser
{
    private bool TryHandleCsi(string text, int start, out int endIndex)
    {
        endIndex = text.Length - 1;
        char? privateMarker = null;
        var paramStart = start;
        if (start < text.Length && text[start] is '<' or '=' or '>' or '?')
        {
            privateMarker = text[start];
            paramStart++;
            start++;
        }

        var i = start;
        while (i < text.Length)
        {
            var c = text[i];
            if (c >= '@' && c <= '~')
            {
                var parameterText = text.AsSpan(paramStart, i - paramStart);
                var parameterCount = CountParameters(parameterText);
                if (parameterCount <= 16)
                {
                    Span<int> parameters = stackalloc int[parameterCount];
                    ParseParameters(parameterText, parameters);
                    ApplyCsi(privateMarker, c, parameters);
                }
                else
                {
                    var rented = ArrayPool<int>.Shared.Rent(parameterCount);
                    try
                    {
                        var parameters = rented.AsSpan(0, parameterCount);
                        ParseParameters(parameterText, parameters);
                        ApplyCsi(privateMarker, c, parameters);
                    }
                    finally
                    {
                        ArrayPool<int>.Shared.Return(rented);
                    }
                }

                endIndex = i;
                return true;
            }

            i++;
        }

        return false;
    }

    private void ApplyCsi(char? privateMarker, char command, ReadOnlySpan<int> parameters)
    {
        if (privateMarker == '?' && parameters.Length > 0)
        {
            foreach (var parameter in parameters)
            {
                if (parameter is 47 or 1047 or 1049)
                {
                    if (command == 'h')
                    {
                        _buffer.SetAlternateScreen(true);
                    }
                    else if (command == 'l')
                    {
                        _buffer.SetAlternateScreen(false);
                    }

                    return;
                }

                if (parameter == 6)
                {
                    if (command == 'h')
                    {
                        _buffer.SetOriginMode(true);
                    }
                    else if (command == 'l')
                    {
                        _buffer.SetOriginMode(false);
                    }

                    return;
                }

                if (parameter == 25)
                {
                    if (command == 'h')
                    {
                        _buffer.SetCursorVisible(true);
                    }
                    else if (command == 'l')
                    {
                        _buffer.SetCursorVisible(false);
                    }

                    return;
                }

                if (parameter == 25)
                {
                    if (command == 'h')
                    {
                        _buffer.SetCursorVisible(true);
                    }
                    else if (command == 'l')
                    {
                        _buffer.SetCursorVisible(false);
                    }

                    return;
                }
            }
        }

        // Private CSI sequences (e.g. CSI ? 4 m, CSI > 4 ; 2 m) are not SGR.
        // Ignore them unless explicitly handled above.
        if (privateMarker is not null)
        {
            return;
        }

        switch (command)
        {
            case 'm':
                ApplySgr(parameters);
                return;
            case 'A':
                _buffer.MoveCursorBy(-Math.Max(1, GetParameter(parameters, 0, 1)), 0);
                return;
            case 'B':
            case 'e':
                _buffer.MoveCursorBy(Math.Max(1, GetParameter(parameters, 0, 1)), 0);
                return;
            case 'C':
            case 'a':
                _buffer.MoveCursorBy(0, Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case 'b':
                _buffer.RepeatPreviousCharacter(Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case 'D':
                _buffer.MoveCursorBy(0, -Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case 'E':
                _buffer.MoveCursorBy(Math.Max(1, GetParameter(parameters, 0, 1)), 0);
                _buffer.CarriageReturn();
                return;
            case 'F':
                _buffer.MoveCursorBy(-Math.Max(1, GetParameter(parameters, 0, 1)), 0);
                _buffer.CarriageReturn();
                return;
            case 'G':
            case '`':
                {
                    var col = Math.Max(1, GetParameter(parameters, 0, 1)) - 1;
                    _buffer.MoveCursorTo(_buffer.CursorRow, col);
                    return;
                }
            case 'H':
            case 'f':
                {
                    var row = Math.Max(1, GetParameter(parameters, 0, 1)) - 1;
                    var col = Math.Max(1, GetParameter(parameters, 1, 1)) - 1;
                    _buffer.MoveCursorToOriginRelative(row, col);
                    return;
                }
            case 'd':
                {
                    var row = Math.Max(1, GetParameter(parameters, 0, 1)) - 1;
                    _buffer.MoveCursorToOriginRelative(row, _buffer.CursorCol);
                    return;
                }
            case 'J':
                _buffer.ClearDisplay(GetParameter(parameters, 0, 0), _style);
                return;
            case 'K':
                _buffer.ClearLine(GetParameter(parameters, 0, 0), _style);
                return;
            case 'P':
                _buffer.DeleteCharacters(Math.Max(1, GetParameter(parameters, 0, 1)), _style);
                return;
            case '@':
                _buffer.InsertBlankCharacters(Math.Max(1, GetParameter(parameters, 0, 1)), _style);
                return;
            case 'L':
                _buffer.InsertLines(Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case 'M':
                _buffer.DeleteLines(Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case 'S':
                _buffer.ScrollUpLines(Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case 'T':
                _buffer.ScrollDownLines(Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case 'r':
                {
                    var top = Math.Max(1, GetParameter(parameters, 0, 1)) - 1;
                    var bottom = Math.Max(1, GetParameter(parameters, 1, _buffer.Height)) - 1;
                    _buffer.SetScrollRegion(top, bottom);
                    return;
                }
            case 'g':
                _buffer.ClearTabStops(GetParameter(parameters, 0, 0));
                return;
            case 'h':
                if (GetParameter(parameters, 0, 0) == 4)
                {
                    _buffer.SetInsertMode(true);
                }

                return;
            case 'l':
                if (GetParameter(parameters, 0, 0) == 4)
                {
                    _buffer.SetInsertMode(false);
                }
                return;
            case 'X':
                _buffer.EraseChars(Math.Max(1, GetParameter(parameters, 0, 1)), _style);
                return;
            case 's':
                _buffer.SaveCursor();
                return;
            case 'u':
                _buffer.RestoreCursor();
                return;
            default:
                return;
        }
    }
}
