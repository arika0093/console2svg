#pragma warning disable S127
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConsoleToSvg.Terminal;

public sealed class AnsiParser
{
    private readonly ScreenBuffer _buffer;
    private readonly Theme _theme;
    private TextStyle _style;
    private string _pendingEscapeSequence = string.Empty;

    // Holds a partial caret-notation sequence that spans event chunks (e.g. echoed ESC as "^[")
    private string _pendingCaretSequence = string.Empty;

    public AnsiParser(ScreenBuffer buffer, Theme theme)
    {
        _buffer = buffer;
        _theme = theme;
        _style = _buffer.DefaultStyle;
    }

    public void Process(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!string.IsNullOrEmpty(_pendingEscapeSequence))
        {
            text = _pendingEscapeSequence + text;
            _pendingEscapeSequence = string.Empty;
        }

        if (!string.IsNullOrEmpty(_pendingCaretSequence))
        {
            text = _pendingCaretSequence + text;
            _pendingCaretSequence = string.Empty;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\u001b')
            {
                if (!TryHandleEscape(text, i, out var escapeEndIndex))
                {
                    _pendingEscapeSequence = text.Substring(i);
                    break;
                }

                i = escapeEndIndex;
                continue;
            }

            // Caret-notation OSC: PTY ECHOCTL converts \x1b (ESC) to '^' + '['.
            // Only handle "^[]..." (OSC start) to avoid false positives with legitimate
            // text output that contains "^[" (e.g. docstrings, cat -v output).
            if (ch == '^' && i + 2 < text.Length && text[i + 1] == '[' && text[i + 2] == ']')
            {
                if (!TrySkipCaretOsc(text, i + 3, out var oscEnd))
                {
                    // Incomplete sequence (spans chunks); save and wait for the rest.
                    _pendingCaretSequence = text.Substring(i);
                    break;
                }

                i = oscEnd;
                continue;
            }

            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                var cluster = text.Substring(i, 2);
                i++;
                _buffer.PutSurrogatePair(cluster, _style);
                continue;
            }

            if (char.IsLowSurrogate(ch))
            {
                continue;
            }

            if (IsVariationSelector(ch))
            {
                _buffer.AppendToPreviousCell(ch.ToString());
                continue;
            }

            if (IsZeroWidthChar(ch))
            {
                continue;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (
                category
                is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark
            )
            {
                _buffer.AppendToPreviousCell(ch.ToString());
                continue;
            }

            _buffer.PutChar(ch, _style);
        }
    }

    private static bool IsZeroWidthChar(char ch) =>
        ch is '\u00AD' or '\u200B' or '\u200C' or '\u200D' or '\uFEFF';

    private static bool IsVariationSelector(char ch) => ch is >= '\uFE00' and <= '\uFE0F';

    private bool TryHandleEscape(string text, int index, out int endIndex)
    {
        endIndex = index;
        if (index + 1 >= text.Length)
        {
            return false;
        }

        var next = text[index + 1];
        switch (next)
        {
            case '[':
                return TryHandleCsi(text, index + 2, out endIndex);
            case ']':
                return TrySkipOsc(text, index + 2, out endIndex);
            case 'P':
                return TrySkipDcs(text, index + 2, out endIndex);
            case '(':
            case ')':
            case '*':
            case '+':
            case '-':
            case '.':
            case '/':
            case '#':
            case '%':
                return TrySkipEscSingleFinal(text, index + 2, out endIndex);
            case '7':
                _buffer.SaveCursor();
                endIndex = index + 1;
                return true;
            case '8':
                _buffer.RestoreCursor();
                endIndex = index + 1;
                return true;
            case 'D':
                _buffer.Index();
                endIndex = index + 1;
                return true;
            case 'E':
                _buffer.NextLine();
                endIndex = index + 1;
                return true;
            case 'M':
                _buffer.ReverseIndex();
                endIndex = index + 1;
                return true;
            case 'c':
                _buffer.ClearDisplay(2);
                _buffer.MoveCursorTo(0, 0);
                _style = _buffer.DefaultStyle;
                endIndex = index + 1;
                return true;
            default:
                endIndex = index + 1;
                return true;
        }
    }

    private static bool TrySkipEscSingleFinal(string text, int start, out int endIndex)
    {
        endIndex = start;
        if (start >= text.Length)
        {
            return false;
        }

        endIndex = start;
        return true;
    }

    private static bool TrySkipOsc(string text, int start, out int endIndex)
    {
        endIndex = text.Length - 1;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\a')
            {
                endIndex = i;
                return true;
            }

            if (text[i] == '\u001b' && i + 1 < text.Length && text[i + 1] == '\\')
            {
                endIndex = i + 1;
                return true;
            }
        }

        return false;
    }

    // Skips the body of a caret-notation OSC sequence up to:
    //   • BEL (\a)
    //   • Caret-notation ST: ^[\
    //   • Real ST: ESC\
    private static bool TrySkipCaretOsc(string text, int start, out int endIndex)
    {
        endIndex = start;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\a')
            {
                endIndex = i;
                return true;
            }
            // Caret-notation string terminator: ^[\
            if (text[i] == '^' && i + 2 < text.Length && text[i + 1] == '[' && text[i + 2] == '\\')
            {
                endIndex = i + 2;
                return true;
            }
            // Real ESC-backslash string terminator (mixed notation)
            if (text[i] == '\u001b' && i + 1 < text.Length && text[i + 1] == '\\')
            {
                endIndex = i + 1;
                return true;
            }
        }

        return false; // incomplete — caller will store as pending
    }

    private static bool TrySkipDcs(string text, int start, out int endIndex) =>
        TrySkipOsc(text, start, out endIndex);

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
                var parameterText =
                    paramStart <= i ? text.Substring(paramStart, i - paramStart) : string.Empty;
                var parameters = ParseParameters(parameterText);
                ApplyCsi(privateMarker, c, parameters);
                endIndex = i;
                return true;
            }

            i++;
        }

        return false;
    }

    private void ApplyCsi(char? privateMarker, char command, List<int> parameters)
    {
        if (privateMarker == '?' && parameters.Count > 0 && parameters[0] == 1049)
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
                _buffer.MoveCursorTo(row, col);
                return;
            }
            case 'd':
            {
                var row = Math.Max(1, GetParameter(parameters, 0, 1)) - 1;
                _buffer.MoveCursorTo(row, _buffer.CursorCol);
                return;
            }
            case 'J':
                _buffer.ClearDisplay(GetParameter(parameters, 0, 0));
                return;
            case 'K':
                _buffer.ClearLine(GetParameter(parameters, 0, 0));
                return;
            case 'P':
                _buffer.DeleteCharacters(Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case '@':
                _buffer.InsertBlankCharacters(Math.Max(1, GetParameter(parameters, 0, 1)));
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
            case 'X':
                _buffer.EraseChars(Math.Max(1, GetParameter(parameters, 0, 1)));
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

    private void ApplySgr(List<int> parameters)
    {
        if (parameters.Count == 0)
        {
            parameters.Add(0);
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var code = parameters[i];
            switch (code)
            {
                case 0:
                    _style = _buffer.DefaultStyle;
                    break;
                case 1:
                    _style = _style with { Bold = true, Faint = false };
                    break;
                case 2:
                    _style = _style with { Bold = false, Faint = true };
                    break;
                case 3:
                    _style = _style with { Italic = true };
                    break;
                case 4:
                    _style = _style with { Underline = true };
                    break;
                case 7:
                    _style = _style with { Reversed = true };
                    break;
                case 22:
                    _style = _style with { Bold = false, Faint = false };
                    break;
                case 23:
                    _style = _style with { Italic = false };
                    break;
                case 24:
                    _style = _style with { Underline = false };
                    break;
                case 27:
                    _style = _style with { Reversed = false };
                    break;
                case 39:
                    _style = _style with { Foreground = _buffer.DefaultStyle.Foreground };
                    break;
                case 49:
                    _style = _style with { Background = _buffer.DefaultStyle.Background };
                    break;
                default:
                    if (code >= 30 && code <= 37)
                    {
                        _style = _style with { Foreground = _theme.AnsiPalette[code - 30] };
                    }
                    else if (code >= 40 && code <= 47)
                    {
                        _style = _style with { Background = _theme.AnsiPalette[code - 40] };
                    }
                    else if (code >= 90 && code <= 97)
                    {
                        _style = _style with { Foreground = _theme.AnsiPalette[8 + (code - 90)] };
                    }
                    else if (code >= 100 && code <= 107)
                    {
                        _style = _style with { Background = _theme.AnsiPalette[8 + (code - 100)] };
                    }
                    else if ((code == 38 || code == 48) && i + 1 < parameters.Count)
                    {
                        var isForeground = code == 38;
                        var mode = parameters[i + 1];
                        if (mode == 5 && i + 2 < parameters.Count)
                        {
                            var color = FromAnsi256(parameters[i + 2]);
                            _style = isForeground
                                ? _style with
                                {
                                    Foreground = color,
                                }
                                : _style with
                                {
                                    Background = color,
                                };
                            i += 2;
                        }
                        else if (mode == 2 && i + 4 < parameters.Count)
                        {
                            var r = Clamp(parameters[i + 2], 0, 255);
                            var g = Clamp(parameters[i + 3], 0, 255);
                            var b = Clamp(parameters[i + 4], 0, 255);
                            var color = $"#{r:X2}{g:X2}{b:X2}";
                            _style = isForeground
                                ? _style with
                                {
                                    Foreground = color,
                                }
                                : _style with
                                {
                                    Background = color,
                                };
                            i += 4;
                        }
                    }

                    break;
            }
        }
    }

    private string FromAnsi256(int index)
    {
        index = Clamp(index, 0, 255);
        if (index < 16)
        {
            return _theme.AnsiPalette[index];
        }

        if (index >= 232)
        {
            var gray = 8 + ((index - 232) * 10);
            gray = Clamp(gray, 0, 255);
            return $"#{gray:X2}{gray:X2}{gray:X2}";
        }

        var cube = index - 16;
        var r = cube / 36;
        var g = (cube % 36) / 6;
        var b = cube % 6;

        var rgbR = r == 0 ? 0 : (55 + r * 40);
        var rgbG = g == 0 ? 0 : (55 + g * 40);
        var rgbB = b == 0 ? 0 : (55 + b * 40);
        return $"#{rgbR:X2}{rgbG:X2}{rgbB:X2}";
    }

    private static List<int> ParseParameters(string parameterText)
    {
        var result = new List<int>();
        if (string.IsNullOrEmpty(parameterText))
        {
            return result;
        }

        var split = parameterText.Split(';');
        foreach (var part in split)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                result.Add(0);
                continue;
            }

            if (
                int.TryParse(
                    part,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value
                )
            )
            {
                result.Add(value);
            }
            else
            {
                result.Add(0);
            }
        }

        return result;
    }

    private static int GetParameter(List<int> parameters, int index, int defaultValue)
    {
        if (index < 0 || index >= parameters.Count)
        {
            return defaultValue;
        }

        return parameters[index];
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
