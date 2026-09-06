using System;
using System.Globalization;
using System.IO;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

public static partial class OptionParser
{
    private static bool TryParseSvgConverterMode(
        string value,
        out SvgConverterMode mode,
        out string? error
    )
    {
        error = null;
        if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            mode = SvgConverterMode.Auto;
            return true;
        }
        if (string.Equals(value, "ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            mode = SvgConverterMode.Ffmpeg;
            return true;
        }
        if (
            string.Equals(value, "rsvg-convert", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "rsvg", StringComparison.OrdinalIgnoreCase)
        )
        {
            mode = SvgConverterMode.RsvgConvert;
            return true;
        }
        if (string.Equals(value, "resvg", StringComparison.OrdinalIgnoreCase))
        {
            mode = SvgConverterMode.Resvg;
            return true;
        }
        mode = SvgConverterMode.Auto;
        error = "--svg-converter must be auto, ffmpeg, rsvg-convert, or resvg.";
        return false;
    }

    private static bool TryParseInt(
        string? value,
        string option,
        out int parsedValue,
        out string? error
    )
    {
        error = null;
        parsedValue = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Missing value for {option}.";
            return false;
        }

        if (
            !int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsedValue
            )
        )
        {
            error = $"{option} must be integer.";
            return false;
        }

        return true;
    }

    private static bool TryParseDouble(
        string? value,
        string option,
        out double parsedValue,
        out string? error
    )
    {
        error = null;
        parsedValue = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Missing value for {option}.";
            return false;
        }

        if (
            !double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsedValue
            )
        )
        {
            error = $"{option} must be a number.";
            return false;
        }

        return true;
    }

    private static bool TryParseSize(
        string? value,
        out double? sizeWidth,
        out double? sizeHeight,
        out string? error
    )
    {
        sizeWidth = null;
        sizeHeight = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "--size requires a value.";
            return false;
        }

        var xIdx = value.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (xIdx < 0)
        {
            // Width only: --size 800
            if (
                !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            )
            {
                error = "--size value must be a positive number or WIDTHxHEIGHT format.";
                return false;
            }

            if (w <= 0)
            {
                error = "--size width must be greater than 0.";
                return false;
            }

            sizeWidth = w;
            return true;
        }

        var wPart = value.Substring(0, xIdx);
        var hPart = value.Substring(xIdx + 1);

        if (!string.IsNullOrEmpty(wPart) && !string.Equals(wPart, "*", StringComparison.Ordinal))
        {
            if (
                !double.TryParse(
                    wPart,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var pw
                )
            )
            {
                error = "--size width component must be a positive number or *.";
                return false;
            }

            if (pw <= 0)
            {
                error = "--size width must be greater than 0.";
                return false;
            }

            sizeWidth = pw;
        }

        if (!string.IsNullOrEmpty(hPart) && !string.Equals(hPart, "*", StringComparison.Ordinal))
        {
            if (
                !double.TryParse(
                    hPart,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var ph
                )
            )
            {
                error = "--size height component must be a positive number or *.";
                return false;
            }

            if (ph <= 0)
            {
                error = "--size height must be greater than 0.";
                return false;
            }

            sizeHeight = ph;
        }

        if (sizeWidth is null && sizeHeight is null)
        {
            error = "--size must specify at least one numeric dimension.";
            return false;
        }

        return true;
    }

    private static bool Validate(AppOptions options, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            error = "--out must not be empty.";
            return false;
        }

        if (options.Width.HasValue && options.Width.Value <= 0)
        {
            error = "--width must be greater than 0.";
            return false;
        }

        if (options.Height.HasValue && options.Height.Value <= 0)
        {
            error = "--height must be greater than 0.";
            return false;
        }

        if (options.Frame is < 0)
        {
            error = "--frame must be non-negative.";
            return false;
        }

        var hasTimeSingle = options.Time.HasValue;
        var hasTimeRange = options.TimeStart.HasValue || options.TimeEnd.HasValue;
        if (hasTimeSingle && hasTimeRange)
        {
            error = "--time cannot specify both a single value and a range.";
            return false;
        }

        if ((hasTimeSingle || hasTimeRange) && options.Frame.HasValue)
        {
            error = "--time and --frame are mutually exclusive.";
            return false;
        }

        if (hasTimeRange && (!options.TimeStart.HasValue || !options.TimeEnd.HasValue))
        {
            error = "--time range requires both start and end (e.g. --time 1.5-3.0).";
            return false;
        }

        if (
            options.Padding.HasValue
            && (
                double.IsNaN(options.Padding.Value)
                || double.IsInfinity(options.Padding.Value)
                || options.Padding.Value < 0
            )
        )
        {
            error = "--padding must be a non-negative finite number.";
            return false;
        }

        if (
            double.IsNaN(options.VideoFps)
            || double.IsInfinity(options.VideoFps)
            || options.VideoFps <= 0
        )
        {
            error = "--fps must be greater than 0.";
            return false;
        }

        if (
            double.IsNaN(options.VideoSleep)
            || double.IsInfinity(options.VideoSleep)
            || options.VideoSleep < 0
        )
        {
            error = "--sleep must be a non-negative number.";
            return false;
        }

        if (
            double.IsNaN(options.VideoFadeOut)
            || double.IsInfinity(options.VideoFadeOut)
            || options.VideoFadeOut < 0
        )
        {
            error = "--fadeout must be a non-negative number.";
            return false;
        }

        if (
            options.OutputCoalesceMs.HasValue
            && (
                double.IsNaN(options.OutputCoalesceMs.Value)
                || double.IsInfinity(options.OutputCoalesceMs.Value)
                || options.OutputCoalesceMs.Value < 0
            )
        )
        {
            error = "--coalesce-ms must be auto or a non-negative number.";
            return false;
        }

        if (
            double.IsNaN(options.Opacity)
            || double.IsInfinity(options.Opacity)
            || options.Opacity < 0
            || options.Opacity > 1
        )
        {
            error = "--opacity must be a number between 0 and 1.";
            return false;
        }

        if (!IsLengthAdjustValue(options.LengthAdjust))
        {
            error = "--adjust must be spacing or spacingAndGlyphs.";
            return false;
        }

        if (
            options.FontSize.HasValue
            && (
                double.IsNaN(options.FontSize.Value)
                || double.IsInfinity(options.FontSize.Value)
                || options.FontSize.Value <= 0
            )
        )
        {
            error = "--fontsize must be greater than 0.";
            return false;
        }

        if (
            options.SizeWidth.HasValue
            && (
                double.IsNaN(options.SizeWidth.Value)
                || double.IsInfinity(options.SizeWidth.Value)
                || options.SizeWidth.Value <= 0
            )
        )
        {
            error = "--size width must be greater than 0.";
            return false;
        }

        if (
            options.SizeHeight.HasValue
            && (
                double.IsNaN(options.SizeHeight.Value)
                || double.IsInfinity(options.SizeHeight.Value)
                || options.SizeHeight.Value <= 0
            )
        )
        {
            error = "--size height must be greater than 0.";
            return false;
        }

        if (
            options.Timeout.HasValue
            && (
                double.IsNaN(options.Timeout.Value)
                || double.IsInfinity(options.Timeout.Value)
                || options.Timeout.Value <= 0
            )
        )
        {
            error = "--timeout must be greater than 0.";
            return false;
        }

        if (options.Interactive)
        {
            if (options.EmbedCast)
            {
                error = "--interactive cannot be used with --embed-cast.";
                return false;
            }

            if (options.EmbedLogs || options.EmbedReplay)
            {
                error = "--interactive cannot be used with embed options.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(options.Command) && options.DelimitedCommand is null)
            {
                error =
                    "An interactive program must be specified after -- (for example: -i -- vim).";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(options.InputCastPath))
            {
                error = "--interactive cannot be used with --in.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(options.InputSvgPath))
            {
                error = "--interactive cannot be used with SVG input.";
                return false;
            }

            if (options.StdOut)
            {
                error = "--interactive cannot be used with --stdout.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(options.SaveCastPath))
            {
                error = "--interactive cannot be used with --save-cast.";
                return false;
            }

            if (options.Mode == OutputMode.Repeat)
            {
                error = "--interactive cannot be used with --mode repeat.";
                return false;
            }

            if (
                !string.IsNullOrWhiteSpace(options.ReplayPath)
                || !string.IsNullOrWhiteSpace(options.ReplaySavePath)
            )
            {
                error = "--interactive cannot be used with replay options.";
                return false;
            }
        }

        if (
            (options.EmbedCast || options.EmbedLogs || options.EmbedReplay)
            && !options.StdOut
            && !string.IsNullOrEmpty(Path.GetExtension(options.OutputPath))
            && !string.Equals(Path.GetExtension(options.OutputPath), ".svg", StringComparison.OrdinalIgnoreCase)
        )
        {
            error = "Embed options require SVG output.";
            return false;
        }

        if (options.EmbedReplay && string.IsNullOrWhiteSpace(options.Command))
        {
            error = "--embed-replay requires a command to be specified.";
            return false;
        }

        if (options.EmbedReplay && !string.IsNullOrWhiteSpace(options.ReplayPath))
        {
            error = "--embed-replay and --replay cannot be used together.";
            return false;
        }

        if (options.EmbedReplay && options.Mode == OutputMode.Repeat)
        {
            error = "--embed-replay cannot be used with --mode repeat.";
            return false;
        }

        if (options.Mode == OutputMode.Repeat && string.IsNullOrWhiteSpace(options.Command))
        {
            error = "--mode repeat requires a command to be specified.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.InputSvgPath)
            && !string.IsNullOrWhiteSpace(options.InputCastPath))
        {
            error = "SVG input and --in cannot be used together.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.InputSvgPath))
        {
            var outputExtension = Path.GetExtension(options.OutputPath).TrimStart('.');
            var usesVideoOutput = options.IsModeExplicit
                ? options.Mode is OutputMode.Video or OutputMode.Repeat
                : IsVideoFormat(outputExtension);
            if (usesVideoOutput)
            {
                error = "SVG input cannot be converted to video output.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.Command)
            && (!string.IsNullOrWhiteSpace(options.InputCastPath)
                || !string.IsNullOrWhiteSpace(options.InputSvgPath)))
        {
            error = "--command and --in cannot be used together.";
            return false;
        }

        if (
            !string.IsNullOrWhiteSpace(options.ReplayPath)
            && !string.IsNullOrWhiteSpace(options.ReplaySavePath)
        )
        {
            error = "--replay and --replay-save cannot be used together.";
            return false;
        }

        if (
            !string.IsNullOrWhiteSpace(options.ReplayPath)
            && string.IsNullOrWhiteSpace(options.Command)
        )
        {
            error = "--replay requires a command to be specified.";
            return false;
        }

        if (
            !string.IsNullOrWhiteSpace(options.ReplaySavePath)
            && string.IsNullOrWhiteSpace(options.Command)
        )
        {
            error = "--replay-save requires a command to be specified.";
            return false;
        }

        return true;
    }

    private static bool IsVideoFormat(string extension) => extension.ToLowerInvariant() switch
    {
        "mp4" or "webm" or "avi" or "mov" or "mkv" or "ogv" or "flv" or "ts" or "wmv" or "m4v" or "gif" => true,
        _ => false,
    };
}
