using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

public sealed partial class ConsoleToSvgCommandLine
{
    private sealed class Symbols
    {
        public Option<FileInfo> OutputPath { get; } =
            PathOption("--out", "Output file path.", "path", "-o");
        public Option<string> InputCastPath { get; } =
            RequiredString("--in", "Read an asciicast v2 file instead of recording.");
        public Option<bool> StdOut { get; } = Flag("--stdout", "Write SVG to standard output.");
        public Option<OutputMode?> Mode { get; } =
            ChoiceEnum<OutputMode>("--mode", "Output mode.", ["image", "video"], "-m");
        public Option<bool> Video { get; } = Flag("--video", "Output animated SVG.", "-v");
        public Option<string> Width { get; } =
            Dimension("--width", "Terminal width in characters.", "-w");
        public Option<string> Height { get; } =
            Dimension("--height", "Terminal height in rows.", "-h");
        public Option<int?> Frame { get; } =
            NonNegativeInt("--frame", "Frame index for image mode.");
        public Option<string> Time { get; } = TimeOption();
        public Option<string> CropTop { get; } =
            RequiredString("--crop-top", "Crop top by px, ch, or text.");
        public Option<string> CropRight { get; } =
            RequiredString("--crop-right", "Crop right by px or ch.");
        public Option<string> CropBottom { get; } =
            RequiredString("--crop-bottom", "Crop bottom by px, ch, or text.");
        public Option<string> CropLeft { get; } =
            RequiredString("--crop-left", "Crop left by px or ch.");
        public Option<string[]> Theme { get; } = MultipleStrings("--theme", "Appearance theme ID.");
        public Option<string> ForeColor { get; } =
            RequiredString("--forecolor", "Override the foreground color.");
        public Option<string> BackColor { get; } =
            RequiredString("--backcolor", "Override the terminal background color.");
        public Option<string?> Window { get; } =
            new("--window", "-d")
            {
                Arity = ArgumentArity.ZeroOrOne,
                HelpName = "style",
                Description =
                    "Terminal window chrome style; defaults to macos when specified without a value.",
            };
        public Option<double?> Margin { get; } =
            NonNegativeDouble("--margin", "Window chrome margin.");
        public Option<double?> Padding { get; } =
            NonNegativeDouble("--padding", "Inner shell padding.");
        public Option<double?> Opacity { get; } = OpacityOption();
        public Option<string> Font { get; } = RequiredString("--font", "CSS font family.");
        public Option<double?> FontSize { get; } =
            PositiveDouble("--fontsize", "Font size in pixels.");
        public Option<double?> PcPadding { get; } =
            NonNegativeDouble("--pc-padding", "Desktop padding override.");
        public Option<FileInfo[]> Background { get; } = BackgroundOption();
        public Option<string[]> Mask { get; } = MaskOption();
        public Option<bool> WithCommand { get; } =
            Flag("--with-command", "Prepend the command line to output.", "-c");
        public Option<string> Header { get; } =
            RequiredString("--header", "Override command header text.");
        public Option<string> Prompt { get; } = RequiredString("--prompt", "Prompt prefix.");
        public Option<string> Adjust { get; } =
            StringChoice(
                "--adjust",
                "SVG text lengthAdjust value.",
                ["spacing", "spacingAndGlyphs"]
            );
        public Option<bool> NoResize { get; } =
            Flag("--no-resize", "Keep the initial TTY size in live-server.");
        public Option<bool> NoLoop { get; } = Flag("--no-loop", "Disable animated SVG looping.");
        public Option<double?> Fps { get; } =
            PositiveDouble("--fps", "Maximum frame sampling rate.");
        public Option<VideoTimingMode?> Timing { get; } =
            ChoiceEnum<VideoTimingMode>(
                "--timing",
                "Video timing mode.",
                ["deterministic", "realtime"]
            );
        public Option<double?> Sleep { get; } =
            NonNegativeDouble("--sleep", "Delay after video capture.");
        public Option<double?> FadeOut { get; } =
            NonNegativeDouble("--fadeout", "Video fade-out duration.");
        public Option<string> Coalesce { get; } = CoalesceOption();
        public Option<double?> Timeout { get; } =
            PositiveDouble("--timeout", "Recording timeout in seconds.");
        public Option<string> SaveCastPath { get; } =
            RequiredString("--save-cast", "Save captured output as asciicast v2.");
        public Option<bool> EmbedCast { get; } =
            Flag("--embed-cast", "Embed the asciicast source in SVG.");
        public Option<bool> EmbedLogs { get; } =
            Flag("--embed-logs", "Embed diagnostic logs in SVG.");
        public Option<bool> EmbedReplay { get; } =
            Flag("--embed-replay", "Embed recorded keyboard input in SVG.");
        public Option<bool> EmbedDebug { get; } =
            Flag("--embed-debug", "Enable all embedded diagnostics.");
        public Option<string> ReplaySavePath { get; } =
            RequiredString("--replay-save", "Save keyboard input for later replay.");
        public Option<string> ReplayPath { get; } =
            RequiredString("--replay", "Replay recorded keyboard input.");
        public Option<string> SaveFramesPath { get; } =
            RequiredString("--save-frames", "Save individual SVG frames to a directory.");
        public Option<bool> Interactive { get; } =
            Flag("--interactive", "Run an interactive shell.", "-i");
        public Option<bool> NoColorEnv { get; } =
            Flag("--no-colorenv", "Disable PTY color environment overrides.");
        public Option<bool> NoDeleteEnvs { get; } =
            Flag("--no-delete-envs", "Keep CI environment variables.");
        public Option<string> SvgConverter { get; } = SvgConverterOption();
        public Option<string> Size { get; } = SizeOption();
        public Option<FileInfo?> Verbose { get; } = VerboseOption();
        public Option<FileInfo> VerboseLogPath { get; } = HiddenPath("--verbose-log");
        public Option<bool> LegacyRoot { get; } = HiddenFlag("--legacy-root");
        public Option<string> TmuxTarget { get; } = RequiredString("--target", "tmux pane target.");
        public Option<int?> History { get; } = HistoryOption();
        public Option<string> ListenAddress { get; } =
            RequiredString("--listen", "IP address for the live server.");
        public Option<bool> StatusJson { get; } = Flag("--json", "Write status as JSON.");

        public IEnumerable<Option> Options =>
            [
                OutputPath,
                StdOut,
                Mode,
                Video,
                Width,
                Height,
                Timeout,
                InputCastPath,
                SaveCastPath,
                EmbedCast,
                EmbedLogs,
                EmbedReplay,
                EmbedDebug,
                ReplaySavePath,
                ReplayPath,
                Verbose,
                WithCommand,
                Header,
                Prompt,
                Window,
                PcPadding,
                Opacity,
                Theme,
                ForeColor,
                BackColor,
                Margin,
                Padding,
                Background,
                Font,
                FontSize,
                Mask,
                Frame,
                Time,
                Size,
                SaveFramesPath,
                CropTop,
                CropRight,
                CropBottom,
                CropLeft,
                NoLoop,
                Fps,
                Timing,
                Sleep,
                FadeOut,
                Coalesce,
                Interactive,
                NoResize,
                NoColorEnv,
                NoDeleteEnvs,
                Adjust,
                SvgConverter,
                VerboseLogPath,
                LegacyRoot,
                TmuxTarget,
                History,
                ListenAddress,
            ];

        public IEnumerable<Option> CaptureOptions =>
            Options.Except([Interactive, TmuxTarget, History, ListenAddress]);

        public IEnumerable<Option> InteractiveOptions =>
            CaptureOptions.Except([
                InputCastPath,
                SaveCastPath,
                EmbedCast,
                EmbedReplay,
                EmbedDebug,
                ReplaySavePath,
                ReplayPath,
                Frame,
                Time,
                LegacyRoot,
            ]);

        public IEnumerable<Option> ReplayOptions =>
            CaptureOptions.Except([Interactive, LegacyRoot]);

        public IEnumerable<Option> CastOptions =>
            ReplayOptions.Except([
                InputCastPath,
                SaveCastPath,
                EmbedCast,
                EmbedReplay,
                EmbedDebug,
                ReplaySavePath,
                ReplayPath,
            ]);

        public IEnumerable<Option> LiveServerOptions =>
            Options.Except([
                OutputPath,
                InputCastPath,
                StdOut,
                Mode,
                Video,
                Frame,
                Time,
                CropTop,
                CropRight,
                CropBottom,
                CropLeft,
                NoLoop,
                Timing,
                Sleep,
                FadeOut,
                Coalesce,
                Timeout,
                SaveCastPath,
                EmbedCast,
                EmbedLogs,
                EmbedReplay,
                EmbedDebug,
                ReplaySavePath,
                ReplayPath,
                SaveFramesPath,
                Interactive,
                SvgConverter,
                Size,
                TmuxTarget,
                History,
                LegacyRoot,
            ]);

        public IEnumerable<Option> TmuxCaptureOptions =>
            CaptureOptions
                .Except([InputCastPath, StdOut, Interactive, NoResize, ListenAddress, LegacyRoot])
                .Concat([TmuxTarget, History]);

        public IEnumerable<Option> TmuxLiveServerOptions =>
            LiveServerOptions.Except([History]).Append(TmuxTarget);

        private static Option<bool> Flag(
            string name,
            string description,
            params string[] aliases
        ) => new(name, aliases) { Arity = ArgumentArity.Zero, Description = description };

        private static Option<string> RequiredString(
            string name,
            string description,
            string? helpName = null,
            params string[] aliases
        ) => new(name, aliases) { Description = description, HelpName = helpName };

        private static Option<FileInfo> PathOption(
            string name,
            string description,
            string? helpName = null,
            params string[] aliases
        ) => new(name, aliases) { Description = description, HelpName = helpName };

        private static Option<FileInfo> HiddenPath(string name) => new(name) { Hidden = true };

        private static Option<bool> HiddenFlag(string name) => new(name) { Hidden = true };

        private static Option<T?> ChoiceEnum<T>(
            string name,
            string description,
            string[] values,
            params string[] aliases
        )
            where T : struct, Enum
        {
            var option = new Option<T?>(name, aliases) { Description = description };
            option.AcceptOnlyFromAmong(StringComparer.OrdinalIgnoreCase, values);
            return option;
        }

        private static Option<string> StringChoice(
            string name,
            string description,
            string[] values,
            params string[] aliases
        )
        {
            var option = new Option<string>(name, aliases) { Description = description };
            option.AcceptOnlyFromAmong(StringComparer.OrdinalIgnoreCase, values);
            return option;
        }

        private static Option<string> SvgConverterOption()
        {
            var values = new[] { "auto", "ffmpeg", "rsvg-convert", "rsvg", "resvg" };
            var option = new Option<string>("--svg-converter")
            {
                Description = "SVG-to-raster converter (auto, ffmpeg, rsvg-convert, or resvg).",
                HelpName = "converter",
            };
            option.CompletionSources.Add(values);
            option.Validators.Add(result =>
            {
                var value = result.GetValueOrDefault<string>();
                if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
                    result.AddError(
                        "--svg-converter must be auto, ffmpeg, rsvg-convert, or resvg."
                    );
            });
            return option;
        }

        private static Option<string> Dimension(
            string name,
            string description,
            params string[] aliases
        )
        {
            var option = RequiredString(name, description, "int|adjust", aliases);
            option.Validators.Add(result =>
            {
                var value = result.GetValueOrDefault<string>();
                if (string.Equals(value, "adjust", StringComparison.OrdinalIgnoreCase))
                    return;
                if (
                    !int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed
                    )
                )
                {
                    result.AddError($"{name} must be integer or adjust.");
                }
                else if (parsed <= 0)
                {
                    result.AddError($"{name} must be greater than 0.");
                }
            });
            return option;
        }

        private static Option<int?> NonNegativeInt(string name, string description)
        {
            var option = new Option<int?>(name) { Description = description };
            option.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<int?>() is < 0)
                    result.AddError($"{name} must be non-negative.");
            });
            return option;
        }

        private static Option<double?> PositiveDouble(string name, string description)
        {
            var option = new Option<double?>(name) { Description = description };
            option.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<double?>() is not { } value)
                    return;
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                    result.AddError($"{name} must be greater than 0.");
            });
            return option;
        }

        private static Option<double?> NonNegativeDouble(string name, string description)
        {
            var option = new Option<double?>(name) { Description = description };
            option.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<double?>() is not { } value)
                    return;
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                    result.AddError($"{name} must be a non-negative finite number.");
            });
            return option;
        }

        private static Option<double?> OpacityOption()
        {
            var option = new Option<double?>("--opacity")
            {
                Description = "Background fill opacity.",
            };
            option.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<double?>() is not { } value)
                    return;
                if (double.IsNaN(value) || double.IsInfinity(value) || value is < 0 or > 1)
                    result.AddError("--opacity must be a number between 0 and 1.");
            });
            return option;
        }

        private static Option<string> TimeOption()
        {
            var option = RequiredString("--time", "Time in seconds or START-END range.", "sec");
            option.Validators.Add(result =>
            {
                var value = result.GetValueOrDefault<string>();
                var separator = value.IndexOf('-');
                if (separator > 0 && separator < value.Length - 1)
                {
                    if (
                        !TryParseNonNegative(value[..separator], out var start)
                        || !TryParseNonNegative(value[(separator + 1)..], out var end)
                    )
                    {
                        result.AddError("--time values must be non-negative numbers.");
                    }
                    else if (end < start)
                    {
                        result.AddError("--time range end must be greater than or equal to start.");
                    }
                    return;
                }

                if (!TryParseNonNegative(value, out _))
                    result.AddError("--time must be a non-negative number.");
            });
            return option;
        }

        private static Option<string> CoalesceOption()
        {
            var option = RequiredString(
                "--coalesce-ms",
                "Output coalescing gap or auto.",
                "ms|auto"
            );
            option.Validators.Add(result =>
            {
                var value = result.GetValueOrDefault<string>();
                if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
                    return;
                if (!TryParseNonNegative(value, out _))
                    result.AddError("--coalesce-ms must be auto or a non-negative number.");
            });
            return option;
        }

        private static Option<string> SizeOption()
        {
            var option = RequiredString(
                "--size",
                "Output dimensions: WIDTH, WIDTHx*, *xHEIGHT, or WIDTHxHEIGHT.",
                "WxH"
            );
            option.Validators.Add(result =>
            {
                var value = result.GetValueOrDefault<string>();
                if (!TryValidateSize(value, out var error))
                    result.AddError(error!);
            });
            return option;
        }

        private static Option<string[]> MultipleStrings(string name, string description) =>
            new(name) { Description = description };

        private static Option<string[]> MaskOption() =>
            new("--mask")
            {
                Description = "Mask one or more sensitive strings in output.",
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true,
            };

        private static Option<FileInfo[]> BackgroundOption()
        {
            var option = new Option<FileInfo[]>("--background")
            {
                Description =
                    "Desktop background color or image. Can be specified twice for a gradient.",
            };
            option.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<FileInfo[]>()?.Any(value => value is null) == true)
                    result.AddError("--background requires a value.");
            });
            return option;
        }

        private static Option<FileInfo?> VerboseOption() =>
            new("--verbose")
            {
                Arity = ArgumentArity.ZeroOrOne,
                Description = "Enable verbose logging; optionally write to a file.",
            };

        private static Option<int?> HistoryOption()
        {
            var option = new Option<int?>("--history")
            {
                Arity = ArgumentArity.ZeroOrOne,
                Description = "Include all history, or a number of recent lines.",
            };
            option.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<int?>() is <= 0)
                    result.AddError("--history value must be greater than 0.");
            });
            return option;
        }

        private static bool TryParseNonNegative(string value, out double parsed) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
            && !double.IsNaN(parsed)
            && !double.IsInfinity(parsed)
            && parsed >= 0;

        private static bool TryValidateSize(string value, out string? error)
        {
            error = null;
            var separator = value.IndexOf('x', StringComparison.OrdinalIgnoreCase);
            if (separator < 0)
            {
                if (!TryParsePositive(value))
                {
                    error = "--size value must be a positive number or WIDTHxHEIGHT format.";
                    return false;
                }
                return true;
            }

            var width = value[..separator];
            var height = value[(separator + 1)..];
            var hasWidth = !string.IsNullOrEmpty(width) && width != "*";
            var hasHeight = !string.IsNullOrEmpty(height) && height != "*";
            if (!hasWidth && !hasHeight)
            {
                error = "--size must specify at least one numeric dimension.";
                return false;
            }
            if (hasWidth && !TryParsePositive(width))
            {
                error = "--size width component must be a positive number or *.";
                return false;
            }
            if (hasHeight && !TryParsePositive(height))
            {
                error = "--size height component must be a positive number or *.";
                return false;
            }
            return true;
        }

        private static bool TryParsePositive(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && !double.IsNaN(parsed)
            && !double.IsInfinity(parsed)
            && parsed > 0;
    }
}
