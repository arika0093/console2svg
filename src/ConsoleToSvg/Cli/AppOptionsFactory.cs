using System;
using System.Collections.Generic;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

internal sealed class RenderCommandSettings
{
    public string? Out { get; init; }
    public string? Format { get; init; }
    public bool Animation { get; init; }
    public OutputMode? Mode { get; init; }
    public TerminalDimension Width { get; init; }
    public TerminalDimension Height { get; init; }
    public int? Frame { get; init; }
    public TimeSelection Time { get; init; }
    public string CropTop { get; init; } = "0";
    public string CropRight { get; init; } = "0";
    public string CropBottom { get; init; } = "0";
    public string CropLeft { get; init; } = "0";
    public string Theme { get; init; } = "dark";
    public string? ForeColor { get; init; }
    public string? BackColor { get; init; }
    public string? Font { get; init; }
    public double? FontSize { get; init; }
    public string Window { get; init; } = "none";
    public double? Padding { get; init; }
    public bool NoLoop { get; init; }
    public double Fps { get; init; } = 12;
    public double Sleep { get; init; }
    public double Fadeout { get; init; }
    public VideoTimingMode Timing { get; init; } = VideoTimingMode.Deterministic;
    public double Opacity { get; init; } = 1;
    public string Adjust { get; init; } = "spacing";
    public string[]? Background { get; init; }
    public bool PcMode { get; init; }
    public double? PcPadding { get; init; }
    public string? SaveFrames { get; init; }
    public OutputSize Size { get; init; }
    public SvgConverterMode SvgConverter { get; init; } = SvgConverterMode.Auto;
    public string[]? Mask { get; init; }
    public bool Stdout { get; init; }
    public bool Verbose { get; init; }
    public string? VerboseLog { get; init; }
}

internal static class AppOptionsFactory
{
    public static AppOptions CreateRendered(
        CliVerb verb,
        RenderCommandSettings settings,
        string? inputCastPath,
        string? replayPath,
        string[] escapedCommand
    )
    {
        var options = new AppOptions
        {
            Verb = verb,
            InputCastPath = inputCastPath,
            ReplayPath = replayPath,
            DelimitedCommand = escapedCommand.Length == 0 ? null : escapedCommand,
            Command = escapedCommand.Length == 0 ? null : string.Join(' ', escapedCommand),
            OutputPath = settings.Out ?? "output.svg",
            IsOutputPathExplicit = settings.Out is not null,
            Format = settings.Format?.ToLowerInvariant(),
            Mode = settings.Mode ?? OutputMode.Image,
            IsModeExplicit = settings.Mode.HasValue || settings.Animation,
            Width = settings.Width.Value,
            WidthAdjust = settings.Width.Adjust,
            Height = settings.Height.Value,
            HeightAdjust = settings.Height.Adjust,
            Frame = settings.Frame,
            CropTop = settings.CropTop,
            CropRight = settings.CropRight,
            CropBottom = settings.CropBottom,
            CropLeft = settings.CropLeft,
            Theme = settings.Theme,
            ForeColor = settings.ForeColor,
            BackColor = settings.BackColor,
            Font = settings.Font,
            FontSize = settings.FontSize,
            Window = settings.Window,
            Padding = settings.Padding,
            Loop = !settings.NoLoop,
            VideoFps = settings.Fps,
            VideoSleep = settings.Sleep,
            VideoFadeOut = settings.Fadeout,
            VideoTiming = settings.Timing,
            Opacity = settings.Opacity,
            LengthAdjust = settings.Adjust,
            PcMode = settings.PcMode,
            PcPadding = settings.PcPadding,
            SaveFramesDir = settings.SaveFrames,
            SizeWidth = settings.Size.Width,
            SizeHeight = settings.Size.Height,
            SvgConverter = settings.SvgConverter,
            StdOut = settings.Stdout,
            Verbose = settings.Verbose || settings.VerboseLog is not null,
            VerboseLogPath = settings.VerboseLog,
        };

        if (settings.Animation)
        {
            options.Mode = OutputMode.Video;
        }

        ApplyTime(options, settings.Time);
        AddValues(options.Background, settings.Background, splitGradient: true);
        AddValues(options.MaskPatterns, settings.Mask, splitGradient: false);
        return options;
    }

    public static AppOptions CreateRecord(
        string output,
        TerminalDimension width,
        TerminalDimension height,
        double? timeout,
        CoalesceWindow coalesce,
        bool noColorEnv,
        bool noDeleteEnvs,
        bool verbose,
        string? verboseLog,
        string[] escapedCommand
    ) =>
        new()
        {
            Verb = CliVerb.Record,
            OutputPath = output,
            IsOutputPathExplicit = true,
            Width = width.Value,
            WidthAdjust = width.Adjust,
            Height = height.Value,
            HeightAdjust = height.Adjust,
            Timeout = timeout,
            OutputCoalesceMs = coalesce.Milliseconds,
            NoColorEnv = noColorEnv,
            NoDeleteEnvs = noDeleteEnvs,
            Verbose = verbose || verboseLog is not null,
            VerboseLogPath = verboseLog,
            DelimitedCommand = escapedCommand.Length == 0 ? null : escapedCommand,
            Command = escapedCommand.Length == 0 ? null : string.Join(' ', escapedCommand),
        };

    public static void ApplyCaptureOptions(
        AppOptions options,
        double? timeout,
        CoalesceWindow coalesce,
        bool noColorEnv,
        bool noDeleteEnvs,
        string? saveCast,
        string? replaySave,
        bool embedCast,
        bool embedLogs,
        bool embedReplay,
        bool embedDebug,
        bool withCommand,
        string? prompt,
        string? header
    )
    {
        options.Timeout = timeout;
        options.OutputCoalesceMs = coalesce.Milliseconds;
        options.NoColorEnv = noColorEnv;
        options.NoDeleteEnvs = noDeleteEnvs;
        options.SaveCastPath = saveCast;
        options.ReplaySavePath = replaySave;
        options.EmbedCast = embedCast || embedDebug;
        options.EmbedLogs = embedLogs || embedDebug;
        options.EmbedReplay = embedReplay || embedDebug;
        options.EmbedDebug = embedDebug;
        options.WithCommand = withCommand;
        options.Prompt = prompt;
        options.Header = header;
    }

    public static void ApplyShellOptions(
        AppOptions options,
        double? timeout,
        bool noColorEnv,
        bool noDeleteEnvs,
        bool noSuffix
    )
    {
        options.Interactive = true;
        options.Timeout = timeout;
        options.NoColorEnv = noColorEnv;
        options.NoDeleteEnvs = noDeleteEnvs;
        options.NoSuffix = noSuffix;
    }

    private static void ApplyTime(AppOptions options, TimeSelection time)
    {
        if (time.Kind == TimeSelectionKind.Single)
        {
            options.Time = time.Value;
        }
        else if (time.Kind == TimeSelectionKind.Range)
        {
            options.TimeStart = time.Start;
            options.TimeEnd = time.End;
        }
    }

    private static void AddValues(
        ICollection<string> destination,
        string[]? values,
        bool splitGradient
    )
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            if (
                splitGradient
                && value.Contains(':', StringComparison.Ordinal)
                && !value.Contains("://", StringComparison.Ordinal)
            )
            {
                var parts = value.Split(':', 2);
                if (parts.Length == 2 && parts[0].Length != 0 && parts[1].Length != 0)
                {
                    destination.Add(parts[0]);
                    destination.Add(parts[1]);
                    continue;
                }
            }

            destination.Add(value);
        }
    }
}
