using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConsoleToSvg.Terminal;

public sealed partial class AnsiParser
{
    private const int MissingParameter = int.MinValue;

    private readonly ScreenBuffer _buffer;
    private readonly Theme _theme;
    private TextStyle _style;
    private string _pendingEscapeSequence = string.Empty;
    private CharacterSet _g0CharacterSet;
    private CharacterSet _g1CharacterSet;
    private bool _useG1CharacterSet;
    private TextStyle _savedDecStyle;
    private CharacterSet _savedDecG0CharacterSet;
    private CharacterSet _savedDecG1CharacterSet;
    private bool _savedDecUseG1CharacterSet;
    private bool _savedDecOriginMode;

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

            if (ch == '\u000E')
            {
                _useG1CharacterSet = true;
                continue;
            }

            if (ch == '\u000F')
            {
                _useG1CharacterSet = false;
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
                _buffer.AppendToPreviousCell(ch);
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
                _buffer.AppendToPreviousCell(ch);
                continue;
            }

            _buffer.PutChar(TranslateCharacterSet(ch), _style);
        }
    }

    private static bool IsZeroWidthChar(char ch) =>
        ch is '\u00AD' or '\u200B' or '\u200C' or '\u200D' or '\uFEFF';

    private static bool IsVariationSelector(char ch) => ch is >= '\uFE00' and <= '\uFE0F';

    private char TranslateCharacterSet(char value)
    {
        if (
            (_useG1CharacterSet ? _g1CharacterSet : _g0CharacterSet)
            != CharacterSet.DecSpecialGraphics
        )
        {
            return value;
        }

        return value switch
        {
            '`' => '\u25C6',
            'a' => '\u2592',
            'f' => '\u00B0',
            'g' => '\u00B1',
            'j' => '\u2518',
            'k' => '\u2510',
            'l' => '\u250C',
            'm' => '\u2514',
            'n' => '\u253C',
            'q' => '\u2500',
            't' => '\u251C',
            'u' => '\u2524',
            'v' => '\u2534',
            'w' => '\u252C',
            'x' => '\u2502',
            'y' => '\u2264',
            'z' => '\u2265',
            '{' => '\u03C0',
            '|' => '\u2260',
            '}' => '\u00A3',
            '~' => '\u00B7',
            _ => value,
        };
    }

    private enum CharacterSet
    {
        UsAscii,
        DecSpecialGraphics,
    }

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
                return TryHandleCharacterSetDesignation(next, text, index + 2, out endIndex);
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
                _savedDecStyle = _style;
                _savedDecG0CharacterSet = _g0CharacterSet;
                _savedDecG1CharacterSet = _g1CharacterSet;
                _savedDecUseG1CharacterSet = _useG1CharacterSet;
                _savedDecOriginMode = _buffer.OriginMode;
                endIndex = index + 1;
                return true;
            case '8':
                _style = _savedDecStyle;
                _g0CharacterSet = _savedDecG0CharacterSet;
                _g1CharacterSet = _savedDecG1CharacterSet;
                _useG1CharacterSet = _savedDecUseG1CharacterSet;
                _buffer.SetOriginMode(_savedDecOriginMode);
                _buffer.RestoreCursor();
                endIndex = index + 1;
                return true;
            case 'H':
                _buffer.SetTabStop();
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

    private bool TryHandleCharacterSetDesignation(
        char selector,
        string text,
        int start,
        out int endIndex
    )
    {
        endIndex = start;
        if (start >= text.Length)
        {
            return false;
        }

        var characterSet =
            text[start] == '0' ? CharacterSet.DecSpecialGraphics : CharacterSet.UsAscii;
        if (selector == '(')
        {
            _g0CharacterSet = characterSet;
        }
        else
        {
            _g1CharacterSet = characterSet;
        }

        return true;
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
}
