using System;
using System.Globalization;
using ConsoleAppFramework;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

public readonly record struct TerminalDimension(int? Value)
{
    public bool Adjust => !Value.HasValue;
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class TerminalDimensionParserAttribute
    : Attribute,
        IArgumentParser<TerminalDimension>
{
    public static bool TryParse(ReadOnlySpan<char> s, out TerminalDimension result)
    {
        if (s.Equals("adjust", StringComparison.OrdinalIgnoreCase))
        {
            result = default;
            return true;
        }

        if (
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0
        )
        {
            result = new(parsed);
            return true;
        }

        result = default;
        return false;
    }
}

public enum TimeSelectionKind
{
    None,
    Single,
    Range,
}

public readonly record struct TimeSelection(
    TimeSelectionKind Kind,
    double Value,
    double Start,
    double End
);

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class TimeSelectionParserAttribute : Attribute, IArgumentParser<TimeSelection>
{
    public static bool TryParse(ReadOnlySpan<char> s, out TimeSelection result)
    {
        var separator = s.Length > 1 ? s[1..].IndexOf('-') : -1;
        if (separator >= 0)
        {
            separator++;
            if (
                TryNonNegativeDouble(s[..separator], out var start)
                && TryNonNegativeDouble(s[(separator + 1)..], out var end)
                && end >= start
            )
            {
                result = new(TimeSelectionKind.Range, 0, start, end);
                return true;
            }

            result = default;
            return false;
        }

        if (TryNonNegativeDouble(s, out var single))
        {
            result = new(TimeSelectionKind.Single, single, 0, 0);
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryNonNegativeDouble(ReadOnlySpan<char> value, out double result) =>
        double.TryParse(value, NumberStyles.AllowExponent | NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && double.IsFinite(result)
        && result >= 0;
}

public readonly record struct OutputSize(double? Width, double? Height);

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class OutputSizeParserAttribute : Attribute, IArgumentParser<OutputSize>
{
    public static bool TryParse(ReadOnlySpan<char> s, out OutputSize result)
    {
        var separator = s.IndexOf('x');
        if (separator < 0)
        {
            if (TryPositiveDouble(s, out var singleWidth))
            {
                result = new(singleWidth, null);
                return true;
            }

            result = default;
            return false;
        }

        var widthPart = s[..separator];
        var heightPart = s[(separator + 1)..];
        double? parsedWidth = null;
        double? parsedHeight = null;
        if (!IsWildcard(widthPart))
        {
            if (!TryPositiveDouble(widthPart, out var parsed))
            {
                result = default;
                return false;
            }
            parsedWidth = parsed;
        }

        if (!IsWildcard(heightPart))
        {
            if (!TryPositiveDouble(heightPart, out var parsed))
            {
                result = default;
                return false;
            }
            parsedHeight = parsed;
        }

        result = new(parsedWidth, parsedHeight);
        return parsedWidth.HasValue || parsedHeight.HasValue;
    }

    private static bool IsWildcard(ReadOnlySpan<char> value) =>
        value.IsEmpty || value.SequenceEqual("*");

    private static bool TryPositiveDouble(ReadOnlySpan<char> value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && double.IsFinite(result)
        && result > 0;
}

public readonly record struct CoalesceWindow(double? Milliseconds);

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CoalesceWindowParserAttribute : Attribute, IArgumentParser<CoalesceWindow>
{
    public static bool TryParse(ReadOnlySpan<char> s, out CoalesceWindow result)
    {
        if (s.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            result = default;
            return true;
        }

        if (
            double.TryParse(
                s,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var milliseconds
            )
            && double.IsFinite(milliseconds)
            && milliseconds >= 0
        )
        {
            result = new(milliseconds);
            return true;
        }

        result = default;
        return false;
    }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class SvgConverterModeParserAttribute
    : Attribute,
        IArgumentParser<SvgConverterMode>
{
    public static bool TryParse(ReadOnlySpan<char> s, out SvgConverterMode result)
    {
        if (s.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            result = SvgConverterMode.Auto;
            return true;
        }
        if (s.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            result = SvgConverterMode.Ffmpeg;
            return true;
        }
        if (
            s.Equals("rsvg", StringComparison.OrdinalIgnoreCase)
            || s.Equals("rsvg-convert", StringComparison.OrdinalIgnoreCase)
        )
        {
            result = SvgConverterMode.RsvgConvert;
            return true;
        }
        if (s.Equals("resvg", StringComparison.OrdinalIgnoreCase))
        {
            result = SvgConverterMode.Resvg;
            return true;
        }

        result = default;
        return false;
    }
}
