using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConsoleToSvg.Terminal;

public sealed partial class AnsiParser
{
    private void ApplySgr(List<int> parameters)
    {
        if (parameters.Count == 0)
        {
            parameters.Add(0);
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var code = GetParameter(parameters, i, 0);
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
                case 5:
                case 6:
                    _style = _style with { Blink = true };
                    break;
                case 7:
                    _style = _style with { Reversed = true };
                    break;
                case 8:
                    _style = _style with { Hidden = true };
                    break;
                case 9:
                    _style = _style with { Strikethrough = true };
                    break;
                case 21:
                    _style = _style with { Underline = true };
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
                case 25:
                    _style = _style with { Blink = false };
                    break;
                case 27:
                    _style = _style with { Reversed = false };
                    break;
                case 28:
                    _style = _style with { Hidden = false };
                    break;
                case 29:
                    _style = _style with { Strikethrough = false };
                    break;
                case 53:
                    _style = _style with { Overline = true };
                    break;
                case 55:
                    _style = _style with { Overline = false };
                    break;
                case 59:
                    _style = _style with { UnderlineColor = null };
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
                    else if ((code == 38 || code == 48 || code == 58) && i + 1 < parameters.Count)
                    {
                        var isForeground = code == 38;
                        var isUnderlineColor = code == 58;
                        var mode = GetParameter(parameters, i + 1, 0);
                        if (mode == 5 && i + 2 < parameters.Count)
                        {
                            var color = FromAnsi256(GetParameter(parameters, i + 2, 0));
                            _style = ApplySgrColor(isForeground, isUnderlineColor, color);
                            i += 2;
                        }
                        else if (mode == 2)
                        {
                            var rgbStart = i + 2;
                            if (
                                rgbStart < parameters.Count
                                && parameters[rgbStart] == MissingParameter
                            )
                            {
                                rgbStart++;
                            }

                            if (rgbStart + 2 >= parameters.Count)
                            {
                                break;
                            }

                            var r = Clamp(GetParameter(parameters, rgbStart, 0), 0, 255);
                            var g = Clamp(GetParameter(parameters, rgbStart + 1, 0), 0, 255);
                            var b = Clamp(GetParameter(parameters, rgbStart + 2, 0), 0, 255);
                            var color = $"#{r:X2}{g:X2}{b:X2}";
                            _style = ApplySgrColor(isForeground, isUnderlineColor, color);
                            i = rgbStart + 2;
                        }
                    }

                    break;
            }
        }
    }

    private TextStyle ApplySgrColor(bool isForeground, bool isUnderlineColor, string color)
    {
        if (isUnderlineColor)
        {
            return _style with { UnderlineColor = color };
        }

        return isForeground
            ? _style with
            {
                Foreground = color,
            }
            : _style with
            {
                Background = color,
            };
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

        var separators = new[] { ';', ':' };
        var split = parameterText.Split(separators);
        foreach (var part in split)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                result.Add(MissingParameter);
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
                result.Add(MissingParameter);
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

        var parameter = parameters[index];
        return parameter == MissingParameter ? defaultValue : parameter;
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
