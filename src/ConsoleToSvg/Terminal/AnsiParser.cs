using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConsoleToSvg.Terminal;

public sealed class AnsiParser
{
    private readonly ScreenBuffer _buffer;
    private readonly Theme _theme;
    private TextStyle _style;

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

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\u001b')
            {
                i = HandleEscape(text, i);
                continue;
            }

            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                var cluster = text.Substring(i, 2);
                i++;
                _buffer.PutSurrogatePair(cluster, _style);
                continue;
            }

            if (char.IsLowSurrogate(ch) || IsVariationSelector(ch))
            {
                continue;
            }

            _buffer.PutChar(ch, _style);
        }
    }

    private static bool IsVariationSelector(char ch) =>
        ch is >= '\uFE00' and <= '\uFE0F';

    private int HandleEscape(string text, int index)
    {
        if (index + 1 >= text.Length)
        {
            return index;
        }

        var next = text[index + 1];
        switch (next)
        {
            case '[':
                return HandleCsi(text, index + 2);
            case ']':
                return SkipOsc(text, index + 2);
            case '7':
                _buffer.SaveCursor();
                return index + 1;
            case '8':
                _buffer.RestoreCursor();
                return index + 1;
            case 'c':
                _buffer.ClearDisplay(2);
                _buffer.MoveCursorTo(0, 0);
                _style = _buffer.DefaultStyle;
                return index + 1;
            default:
                return index + 1;
        }
    }

    private int SkipOsc(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\a')
            {
                return i;
            }

            if (text[i] == '\u001b' && i + 1 < text.Length && text[i + 1] == '\\')
            {
                return i + 1;
            }
        }

        return text.Length - 1;
    }

    private int HandleCsi(string text, int start)
    {
        var privateMode = false;
        var paramStart = start;
        if (start < text.Length && text[start] == '?')
        {
            privateMode = true;
            paramStart++;
            start++;
        }

        var i = start;
        while (i < text.Length)
        {
            var c = text[i];
            if (c >= '@' && c <= '~')
            {
                var parameterText = paramStart <= i ? text.Substring(paramStart, i - paramStart) : string.Empty;
                var parameters = ParseParameters(parameterText);
                ApplyCsi(privateMode, c, parameters);
                return i;
            }

            i++;
        }

        return text.Length - 1;
    }

    private void ApplyCsi(bool privateMode, char command, List<int> parameters)
    {
        if (privateMode && parameters.Count > 0 && parameters[0] == 1049)
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

        switch (command)
        {
            case 'm':
                ApplySgr(parameters);
                return;
            case 'A':
                _buffer.MoveCursorBy(-Math.Max(1, GetParameter(parameters, 0, 1)), 0);
                return;
            case 'B':
                _buffer.MoveCursorBy(Math.Max(1, GetParameter(parameters, 0, 1)), 0);
                return;
            case 'C':
                _buffer.MoveCursorBy(0, Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case 'D':
                _buffer.MoveCursorBy(0, -Math.Max(1, GetParameter(parameters, 0, 1)));
                return;
            case 'H':
            case 'f':
                {
                    var row = Math.Max(1, GetParameter(parameters, 0, 1)) - 1;
                    var col = Math.Max(1, GetParameter(parameters, 1, 1)) - 1;
                    _buffer.MoveCursorTo(row, col);
                    return;
                }
            case 'J':
                _buffer.ClearDisplay(GetParameter(parameters, 0, 0));
                return;
            case 'K':
                _buffer.ClearLine(GetParameter(parameters, 0, 0));
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
                    _style = new TextStyle(_style.Foreground, _style.Background, true, _style.Italic, _style.Underline);
                    break;
                case 3:
                    _style = new TextStyle(_style.Foreground, _style.Background, _style.Bold, true, _style.Underline);
                    break;
                case 4:
                    _style = new TextStyle(_style.Foreground, _style.Background, _style.Bold, _style.Italic, true);
                    break;
                case 22:
                    _style = new TextStyle(_style.Foreground, _style.Background, false, _style.Italic, _style.Underline);
                    break;
                case 23:
                    _style = new TextStyle(_style.Foreground, _style.Background, _style.Bold, false, _style.Underline);
                    break;
                case 24:
                    _style = new TextStyle(_style.Foreground, _style.Background, _style.Bold, _style.Italic, false);
                    break;
                case 39:
                    _style = new TextStyle(_buffer.DefaultStyle.Foreground, _style.Background, _style.Bold, _style.Italic, _style.Underline);
                    break;
                case 49:
                    _style = new TextStyle(_style.Foreground, _buffer.DefaultStyle.Background, _style.Bold, _style.Italic, _style.Underline);
                    break;
                default:
                    if (code >= 30 && code <= 37)
                    {
                        _style = new TextStyle(_theme.AnsiPalette[code - 30], _style.Background, _style.Bold, _style.Italic, _style.Underline);
                    }
                    else if (code >= 40 && code <= 47)
                    {
                        _style = new TextStyle(_style.Foreground, _theme.AnsiPalette[code - 40], _style.Bold, _style.Italic, _style.Underline);
                    }
                    else if (code >= 90 && code <= 97)
                    {
                        _style = new TextStyle(_theme.AnsiPalette[8 + (code - 90)], _style.Background, _style.Bold, _style.Italic, _style.Underline);
                    }
                    else if (code >= 100 && code <= 107)
                    {
                        _style = new TextStyle(_style.Foreground, _theme.AnsiPalette[8 + (code - 100)], _style.Bold, _style.Italic, _style.Underline);
                    }
                    else if ((code == 38 || code == 48) && i + 1 < parameters.Count)
                    {
                        var isForeground = code == 38;
                        var mode = parameters[i + 1];
                        if (mode == 5 && i + 2 < parameters.Count)
                        {
                            var color = FromAnsi256(parameters[i + 2]);
                            if (isForeground)
                            {
                                _style = new TextStyle(color, _style.Background, _style.Bold, _style.Italic, _style.Underline);
                            }
                            else
                            {
                                _style = new TextStyle(_style.Foreground, color, _style.Bold, _style.Italic, _style.Underline);
                            }

                            i += 2;
                        }
                        else if (mode == 2 && i + 4 < parameters.Count)
                        {
                            var r = Clamp(parameters[i + 2], 0, 255);
                            var g = Clamp(parameters[i + 3], 0, 255);
                            var b = Clamp(parameters[i + 4], 0, 255);
                            var color = $"#{r:X2}{g:X2}{b:X2}";
                            if (isForeground)
                            {
                                _style = new TextStyle(color, _style.Background, _style.Bold, _style.Italic, _style.Underline);
                            }
                            else
                            {
                                _style = new TextStyle(_style.Foreground, color, _style.Bold, _style.Italic, _style.Underline);
                            }

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

            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
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
