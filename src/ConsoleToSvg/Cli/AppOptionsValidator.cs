using System;
using System.IO;

namespace ConsoleToSvg.Cli;

internal static class AppOptionsValidator
{
    public static bool TryFinalize(AppOptions options, out string? error)
    {
        error = null;
        if (!ApplyFormat(options, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            error = "--out must not be empty.";
            return false;
        }
        if (options.Frame is < 0)
        {
            error = "--frame must be non-negative.";
            return false;
        }
        if (options.Frame.HasValue && (options.Time.HasValue || options.TimeStart.HasValue))
        {
            error = "--time and --frame are mutually exclusive.";
            return false;
        }
        if (
            options.Verb is CliVerb.Render or CliVerb.Replay or CliVerb.Shell
            && options.Mode == OutputMode.Repeat
        )
        {
            error = $"--mode repeat is not valid for the {options.Verb.ToString().ToLowerInvariant()} command.";
            return false;
        }
        if (options.Verb == CliVerb.Render && !string.IsNullOrWhiteSpace(options.Command))
        {
            error = "The render command does not accept a command after --.";
            return false;
        }
        if (!IsFiniteAtLeast(options.Padding, 0))
        {
            error = "--padding must be a non-negative finite number.";
            return false;
        }
        if (!IsFiniteGreaterThan(options.VideoFps, 0))
        {
            error = "--fps must be greater than 0.";
            return false;
        }
        if (!IsFiniteAtLeast(options.VideoSleep, 0))
        {
            error = "--sleep must be a non-negative number.";
            return false;
        }
        if (!IsFiniteAtLeast(options.VideoFadeOut, 0))
        {
            error = "--fadeout must be a non-negative number.";
            return false;
        }
        if (!IsFiniteAtLeast(options.Opacity, 0) || options.Opacity > 1)
        {
            error = "--opacity must be a number between 0 and 1.";
            return false;
        }
        if (
            !string.Equals(options.LengthAdjust, "spacing", StringComparison.Ordinal)
            && !string.Equals(
                options.LengthAdjust,
                "spacingAndGlyphs",
                StringComparison.Ordinal
            )
        )
        {
            error = "--adjust must be spacing or spacingAndGlyphs.";
            return false;
        }
        if (!IsFiniteGreaterThan(options.FontSize, 0))
        {
            error = "--fontsize must be greater than 0.";
            return false;
        }
        if (!IsFiniteGreaterThan(options.Timeout, 0))
        {
            error = "--timeout must be greater than 0.";
            return false;
        }
        if (options.Background.Count > 2)
        {
            error = "--background accepts at most two colors.";
            return false;
        }
        if (
            (options.EmbedCast || options.EmbedLogs || options.EmbedReplay)
            && !options.StdOut
            && !string.IsNullOrEmpty(Path.GetExtension(options.OutputPath))
            && !string.Equals(
                Path.GetExtension(options.OutputPath),
                ".svg",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            error = "Embed options require SVG output.";
            return false;
        }
        if (
            (options.EmbedCast || options.EmbedLogs || options.EmbedReplay)
            && !string.IsNullOrWhiteSpace(options.Format)
            && !string.Equals(options.Format, "svg", StringComparison.OrdinalIgnoreCase)
        )
        {
            error = "Embed options require SVG format.";
            return false;
        }
        if (options.EmbedReplay && string.IsNullOrWhiteSpace(options.Command))
        {
            error = "--embed-replay requires a command after --.";
            return false;
        }
        if (options.EmbedReplay && !string.IsNullOrWhiteSpace(options.ReplayPath))
        {
            error = "--embed-replay cannot be used by replay.";
            return false;
        }
        if (options.Mode == OutputMode.Repeat && string.IsNullOrWhiteSpace(options.Command))
        {
            error = "--mode repeat requires a command after --.";
            return false;
        }
        if (options.Mode == OutputMode.Repeat && options.EmbedReplay)
        {
            error = "--embed-replay cannot be used with --mode repeat.";
            return false;
        }
        if (options.Verb == CliVerb.Replay && string.IsNullOrWhiteSpace(options.Command))
        {
            error = "The replay command requires a command after --.";
            return false;
        }
        if (
            !string.IsNullOrWhiteSpace(options.ReplaySavePath)
            && string.IsNullOrWhiteSpace(options.Command)
        )
        {
            error = "--replay-save requires a command after --.";
            return false;
        }

        return true;
    }

    private static bool ApplyFormat(AppOptions options, out string? error)
    {
        error = null;
        if (options.Format is null)
        {
            return true;
        }
        if (!OutputFormatCatalog.TryResolve(options.Format, out var format))
        {
            error = $"Unknown output format: {options.Format}";
            return false;
        }

        options.Format = format!.Extension;
        if (!format.SupportsImage)
        {
            if (options.IsModeExplicit && options.Mode == OutputMode.Image)
            {
                error = $"Format {options.Format} does not support image output.";
                return false;
            }
            if (options.Mode != OutputMode.Repeat)
            {
                options.Mode = OutputMode.Video;
                options.IsModeExplicit = true;
            }
        }
        else if (
            options.IsModeExplicit
            && options.Mode == OutputMode.Video
            && !format.SupportsAnimation
        )
        {
            error = $"Format {options.Format} does not support animation.";
            return false;
        }

        if (!options.IsOutputPathExplicit)
        {
            options.OutputPath = $"output.{options.Format}";
        }
        return true;
    }

    private static bool IsFiniteAtLeast(double value, double minimum) =>
        double.IsFinite(value) && value >= minimum;

    private static bool IsFiniteAtLeast(double? value, double minimum) =>
        !value.HasValue || IsFiniteAtLeast(value.Value, minimum);

    private static bool IsFiniteGreaterThan(double value, double minimum) =>
        double.IsFinite(value) && value > minimum;

    private static bool IsFiniteGreaterThan(double? value, double minimum) =>
        !value.HasValue || IsFiniteGreaterThan(value.Value, minimum);
}
