using System;
using System.Globalization;

namespace ConsoleToSvg.Svg;

public enum CropUnit
{
    Pixels,
    Characters,
}

public readonly struct CropValue
{
    public CropValue(double value, CropUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public double Value { get; }

    public CropUnit Unit { get; }

    public static CropValue Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new CropValue(0, CropUnit.Pixels);
        }

        var value = raw.Trim();
        if (value.EndsWith("ch", StringComparison.OrdinalIgnoreCase))
        {
            var numeric = value.Substring(0, value.Length - 2);
            var parsed = ParseNumber(numeric);
            return new CropValue(Math.Max(0, parsed), CropUnit.Characters);
        }

        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            var numeric = value.Substring(0, value.Length - 2);
            var parsed = ParseNumber(numeric);
            return new CropValue(Math.Max(0, parsed), CropUnit.Pixels);
        }

        return new CropValue(Math.Max(0, ParseNumber(value)), CropUnit.Pixels);
    }

    private static double ParseNumber(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Invalid crop value: {value}");
    }
}

public sealed class CropOptions
{
    public CropOptions(CropValue top, CropValue right, CropValue bottom, CropValue left)
    {
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    public CropValue Top { get; }

    public CropValue Right { get; }

    public CropValue Bottom { get; }

    public CropValue Left { get; }

    public static CropOptions Parse(string? top, string? right, string? bottom, string? left)
    {
        return new CropOptions(CropValue.Parse(top), CropValue.Parse(right), CropValue.Parse(bottom), CropValue.Parse(left));
    }
}
