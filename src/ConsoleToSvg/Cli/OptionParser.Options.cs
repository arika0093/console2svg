using System;
using System.Globalization;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

public static partial class OptionParser
{
    private static bool ApplyOption(
        AppOptions options,
        string name,
        string? value,
        out string? error
    )
    {
        error = null;
        switch (name)
        {
            case "-v":
            case "--video":
                options.Mode = OutputMode.Video;
                options.IsModeExplicit = true;
                return true;
            case "--mask":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--mask requires a value.";
                    return false;
                }
                options.MaskPatterns.Add(value);
                return true;

            case "--verbose":
                options.Verbose = true;
                options.VerboseLogPath = string.IsNullOrWhiteSpace(value) ? null : value;
                return true;
            case "--version":
                options.ShowVersion = true;
                return true;
            case "--file":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--file requires a path.";
                    return false;
                }
                options.ConfigPath = value;
                return true;
            case "-c":
            case "--with-command":
                options.WithCommand = true;
                return true;
            case "--in":
                options.InputCastPath = value;
                return true;
            case "-o":
            case "--out":
                options.OutputPath = value ?? options.OutputPath;
                return true;
            case "-m":
            case "--mode":
                if (string.Equals(value, "image", StringComparison.OrdinalIgnoreCase))
                {
                    options.Mode = OutputMode.Image;
                    options.IsModeExplicit = true;
                    return true;
                }

                if (string.Equals(value, "video", StringComparison.OrdinalIgnoreCase))
                {
                    options.Mode = OutputMode.Video;
                    options.IsModeExplicit = true;
                    return true;
                }

                if (string.Equals(value, "repeat", StringComparison.OrdinalIgnoreCase))
                {
                    options.Mode = OutputMode.Repeat;
                    options.IsModeExplicit = true;
                    return true;
                }

                error = "--mode must be image, video, or repeat.";
                return false;
            case "-w":
            case "--width":
                if (string.Equals(value, "adjust", StringComparison.OrdinalIgnoreCase))
                {
                    options.WidthAdjust = true;
                    return true;
                }

                if (!TryParseInt(value, "--width", out var width, out error))
                {
                    return false;
                }

                options.Width = width;
                options.WidthAdjust = false;
                return true;
            case "-h":
            case "--height":
                if (string.Equals(value, "adjust", StringComparison.OrdinalIgnoreCase))
                {
                    options.HeightAdjust = true;
                    return true;
                }

                if (!TryParseInt(value, "--height", out var height, out error))
                {
                    return false;
                }

                options.Height = height;
                options.HeightAdjust = false;
                return true;
            case "--frame":
                if (!TryParseInt(value, "--frame", out var frame, out error))
                {
                    return false;
                }

                options.Frame = frame;
                return true;
            case "--time":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--time requires a value.";
                    return false;
                }

                // Range: 1.5-3.0
                var dashIndex = value.IndexOf('-');
                if (dashIndex > 0 && dashIndex < value.Length - 1)
                {
                    var startPart = value[..dashIndex];
                    var endPart = value[(dashIndex + 1)..];
                    if (
                        !TryParseDouble(startPart, "--time", out var timeStart, out error)
                        || !TryParseDouble(endPart, "--time", out var timeEnd, out error)
                    )
                    {
                        return false;
                    }

                    if (timeStart < 0 || timeEnd < 0)
                    {
                        error = "--time values must be non-negative.";
                        return false;
                    }

                    if (timeEnd < timeStart)
                    {
                        error = "--time range end must be greater than or equal to start.";
                        return false;
                    }

                    options.TimeStart = timeStart;
                    options.TimeEnd = timeEnd;
                    return true;
                }

                if (!TryParseDouble(value, "--time", out var timeVal, out error))
                {
                    return false;
                }

                if (timeVal < 0)
                {
                    error = "--time must be non-negative.";
                    return false;
                }

                options.Time = timeVal;
                return true;
            case "--crop-top":
                options.CropTop = value ?? "0";
                return true;
            case "--crop-right":
                options.CropRight = value ?? "0";
                return true;
            case "--crop-bottom":
                options.CropBottom = value ?? "0";
                return true;
            case "--crop-left":
                options.CropLeft = value ?? "0";
                return true;
            case "--theme":
                options.Theme = string.IsNullOrWhiteSpace(value) ? "dark" : value;
                return true;
            case "--forecolor":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--forecolor requires a value.";
                    return false;
                }

                options.ForeColor = value;
                return true;
            case "-d":
            case "--window":
                // Accept built-in names (macos, windows, macos-pc, windows-pc, transparent, none)
                // or a path to a custom .json chrome definition file.
                // Validation of the value happens at load time via ChromeLoader.
                options.Window = string.IsNullOrWhiteSpace(value) ? "macos" : value;
                return true;
            case "--padding":
                if (!TryParseDouble(value, "--padding", out var padding, out error))
                {
                    return false;
                }

                options.Padding = padding;
                return true;
            case "--no-loop":
                options.Loop = false;
                return true;
            case "--no-colorenv":
                options.NoColorEnv = true;
                return true;
            case "--no-delete-envs":
                options.NoDeleteEnvs = true;
                return true;
            case "--fps":
                if (!TryParseDouble(value, "--fps", out var fps, out error))
                {
                    return false;
                }

                options.VideoFps = fps;
                return true;
            case "--timing":
                if (string.Equals(value, "deterministic", StringComparison.OrdinalIgnoreCase))
                {
                    options.VideoTiming = VideoTimingMode.Deterministic;
                    return true;
                }

                if (string.Equals(value, "realtime", StringComparison.OrdinalIgnoreCase))
                {
                    options.VideoTiming = VideoTimingMode.Realtime;
                    return true;
                }

                error = "--timing must be deterministic or realtime.";
                return false;
            case "--sleep":
                if (!TryParseDouble(value, "--sleep", out var sleep, out error))
                {
                    return false;
                }

                options.VideoSleep = sleep;
                return true;
            case "--fadeout":
                if (!TryParseDouble(value, "--fadeout", out var fadeout, out error))
                {
                    return false;
                }

                options.VideoFadeOut = fadeout;
                return true;
            case "--coalesce-ms":
                if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    options.OutputCoalesceMs = null;
                    return true;
                }

                if (!TryParseDouble(value, "--coalesce-ms", out var coalesceMs, out error))
                {
                    return false;
                }

                options.OutputCoalesceMs = coalesceMs;
                return true;
            case "--opacity":
                if (!TryParseDouble(value, "--opacity", out var opacity, out error))
                {
                    return false;
                }

                options.Opacity = opacity;
                return true;
            case "--adjust":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--adjust requires a value.";
                    return false;
                }

                options.LengthAdjust = value;
                return true;
            case "--background":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--background requires a value.";
                    return false;
                }

                if (options.Background.Count >= 2)
                {
                    error =
                        "--background can be specified at most twice (start color and end color).";
                    return false;
                }

                // Support "#from:#to" colon-separated gradient shorthand.
                // Skip split when the value is a URL (contains "://").
                if (
                    options.Background.Count == 0
                    && value.Contains(':', StringComparison.Ordinal)
                    && !value.Contains("://", StringComparison.Ordinal)
                )
                {
                    var colonIdx = value.IndexOf(':', StringComparison.Ordinal);
                    var part1 = value.Substring(0, colonIdx);
                    var part2 = value.Substring(colonIdx + 1);
                    if (!string.IsNullOrWhiteSpace(part1) && !string.IsNullOrWhiteSpace(part2))
                    {
                        options.Background.Add(part1);
                        options.Background.Add(part2);
                        return true;
                    }
                }

                options.Background.Add(value);
                return true;
            case "--timeout":
                if (!TryParseDouble(value, "--timeout", out var timeout, out error))
                {
                    return false;
                }

                options.Timeout = timeout;
                return true;
            case "--font":
                options.Font = value;
                return true;
            case "--fontsize":
                if (!TryParseDouble(value, "--fontsize", out var fontsize, out error))
                {
                    return false;
                }

                options.FontSize = fontsize;
                return true;
            case "--save-cast":
                options.SaveCastPath = value;
                return true;
            case "--embed-cast":
                options.EmbedCast = true;
                return true;
            case "--embed-logs":
                options.EmbedLogs = true;
                return true;
            case "--embed-replay":
                options.EmbedReplay = true;
                return true;
            case "--embed-debug":
                options.EmbedDebug = true;
                options.EmbedCast = true;
                options.EmbedLogs = true;
                options.EmbedReplay = true;
                return true;
            case "--replay-save":
                options.ReplaySavePath = value;
                return true;
            case "--replay":
                options.ReplayPath = value;
                return true;
            case "--header":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--header requires a value.";
                    return false;
                }

                options.Header = value;
                return true;
            case "--prompt":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--prompt requires a value.";
                    return false;
                }

                options.Prompt = value;
                return true;
            case "--pcmode":
                options.PcMode = true;
                return true;
            case "--stdout":
                options.StdOut = true;
                return true;
            case "-i":
            case "--interactive":
                options.Interactive = true;
                return true;
            case "--pc-padding":
                if (!TryParseDouble(value, "--pc-padding", out var pcPadding, out error))
                {
                    return false;
                }

                options.PcPadding = pcPadding;
                return true;
            case "--backcolor":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--backcolor requires a value.";
                    return false;
                }

                options.BackColor = value;
                return true;
            case "--save-frames":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--save-frames requires a directory path.";
                    return false;
                }

                options.SaveFramesDir = value;
                return true;
            case "--size":
                if (!TryParseSize(value, out var sizeWidth, out var sizeHeight, out error))
                {
                    return false;
                }

                options.SizeWidth = sizeWidth;
                options.SizeHeight = sizeHeight;
                return true;
            case "--svg-converter":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = $"{name} requires a value.";
                    return false;
                }
                if (!TryParseSvgConverterMode(value, out var svgConverter, out error))
                {
                    return false;
                }
                options.SvgConverter = svgConverter;
                return true;
            default:
                error = $"Unknown option: {name}";
                return false;
        }
    }
}
