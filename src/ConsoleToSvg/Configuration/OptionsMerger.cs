using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Configuration;

public static class OptionsMerger
{
    public static ConsoleOptions Merge(params ConsoleOptions?[] layers)
    {
        var merged = new ConsoleOptions();
        foreach (var layer in layers.OfType<ConsoleOptions>())
        {
            merged = Merge(merged, layer);
        }

        return merged;
    }

    public static ConsoleOptions Merge(ConsoleOptions lower, ConsoleOptions higher) =>
        new()
        {
            Terminal = Merge(lower.Terminal, higher.Terminal),
            Environment = Merge(lower.Environment, higher.Environment),
            Interactive = Merge(lower.Interactive, higher.Interactive),
            LiveServer = Merge(lower.LiveServer, higher.LiveServer),
            Capture = Merge(lower.Capture, higher.Capture),
            Render = Merge(lower.Render, higher.Render),
            Converter = Merge(lower.Converter, higher.Converter),
            Appearance = Merge(lower.Appearance, higher.Appearance),
        };

    private static TerminalOptions? Merge(TerminalOptions? lower, TerminalOptions? higher) =>
        lower is null && higher is null
            ? null
            : new TerminalOptions
            {
                Width = higher?.Width ?? lower?.Width,
                Height = higher?.Height ?? lower?.Height,
            };

    private static EnvironmentOptions? Merge(
        EnvironmentOptions? lower,
        EnvironmentOptions? higher
    ) =>
        lower is null && higher is null
            ? null
            : new EnvironmentOptions
            {
                Color = higher?.Color ?? lower?.Color,
                Ci = higher?.Ci ?? lower?.Ci,
            };

    private static InteractiveOptions? Merge(
        InteractiveOptions? lower,
        InteractiveOptions? higher
    ) =>
        lower is null && higher is null
            ? null
            : new InteractiveOptions { Mouse = higher?.Mouse ?? lower?.Mouse };

    private static LiveServerOptions? Merge(LiveServerOptions? lower, LiveServerOptions? higher) =>
        lower is null && higher is null
            ? null
            : new LiveServerOptions { Host = higher?.Host ?? lower?.Host };

    private static CaptureOptions? Merge(CaptureOptions? lower, CaptureOptions? higher) =>
        lower is null && higher is null
            ? null
            : new CaptureOptions
            {
                Mode = higher?.Mode ?? lower?.Mode,
                Crop = Merge(lower?.Crop, higher?.Crop),
                Image = Merge(lower?.Image, higher?.Image),
                Video = Merge(lower?.Video, higher?.Video),
            };

    private static CropOptions? Merge(CropOptions? lower, CropOptions? higher) =>
        lower is null && higher is null
            ? null
            : new CropOptions
            {
                Top = higher?.Top ?? lower?.Top,
                Right = higher?.Right ?? lower?.Right,
                Bottom = higher?.Bottom ?? lower?.Bottom,
                Left = higher?.Left ?? lower?.Left,
            };

    private static ImageCaptureOptions? Merge(
        ImageCaptureOptions? lower,
        ImageCaptureOptions? higher
    ) =>
        lower is null && higher is null
            ? null
            : new ImageCaptureOptions
            {
                Frame = higher?.Frame ?? lower?.Frame,
                Time = higher?.Time ?? lower?.Time,
            };

    private static VideoCaptureOptions? Merge(
        VideoCaptureOptions? lower,
        VideoCaptureOptions? higher
    ) =>
        lower is null && higher is null
            ? null
            : new VideoCaptureOptions
            {
                Fps = higher?.Fps ?? lower?.Fps,
                Loop = higher?.Loop ?? lower?.Loop,
                Time = Merge(lower?.Time, higher?.Time),
                Timing = higher?.Timing ?? lower?.Timing,
                Coalesce = higher?.Coalesce ?? lower?.Coalesce,
                Fadeout = higher?.Fadeout ?? lower?.Fadeout,
            };

    private static CaptureTimeRange? Merge(CaptureTimeRange? lower, CaptureTimeRange? higher) =>
        lower is null && higher is null
            ? null
            : new CaptureTimeRange
            {
                Start = higher?.Start ?? lower?.Start,
                End = higher?.End ?? lower?.End,
            };

    private static RenderOptions? Merge(RenderOptions? lower, RenderOptions? higher) =>
        lower is null && higher is null
            ? null
            : new RenderOptions
            {
                Masking = Merge(lower?.Masking, higher?.Masking),
                Adjust = higher?.Adjust ?? lower?.Adjust,
            };

    private static MaskingOptions? Merge(MaskingOptions? lower, MaskingOptions? higher) =>
        lower is null && higher is null
            ? null
            : new MaskingOptions
            {
                Auto = higher?.Auto ?? lower?.Auto,
                Strings = MergeArray(lower?.Strings, higher?.Strings),
            };

    private static ConverterOptions? Merge(ConverterOptions? lower, ConverterOptions? higher) =>
        lower is null && higher is null
            ? null
            : new ConverterOptions { SvgConverter = higher?.SvgConverter ?? lower?.SvgConverter };

    private static AppearanceOptions? Merge(AppearanceOptions? lower, AppearanceOptions? higher) =>
        lower is null && higher is null
            ? null
            : new AppearanceOptions
            {
                Font = Merge(lower?.Font, higher?.Font),
                Size = Merge(lower?.Size, higher?.Size),
                Theme = MergeArray(lower?.Theme, higher?.Theme),
                Window = higher?.Window ?? lower?.Window,
                Padding = higher?.Padding ?? lower?.Padding,
                Margin = higher?.Margin ?? lower?.Margin,
                PcPadding = higher?.PcPadding ?? lower?.PcPadding,
                Background = MergeArray(lower?.Background, higher?.Background),
                Opacity = higher?.Opacity ?? lower?.Opacity,
                Forecolor = higher?.Forecolor ?? lower?.Forecolor,
                Backcolor = higher?.Backcolor ?? lower?.Backcolor,
                Header = Merge(lower?.Header, higher?.Header),
            };

    private static FontOptions? Merge(FontOptions? lower, FontOptions? higher) =>
        lower is null && higher is null
            ? null
            : new FontOptions
            {
                Family = higher?.Family ?? lower?.Family,
                Size = higher?.Size ?? lower?.Size,
            };

    private static ImageSizeOptions? Merge(ImageSizeOptions? lower, ImageSizeOptions? higher) =>
        lower is null && higher is null
            ? null
            : new ImageSizeOptions
            {
                Width = higher?.Width ?? lower?.Width,
                Height = higher?.Height ?? lower?.Height,
            };

    private static HeaderOptions? Merge(HeaderOptions? lower, HeaderOptions? higher) =>
        lower is null && higher is null
            ? null
            : new HeaderOptions
            {
                Text = higher?.Text ?? lower?.Text,
                WithCommand = higher?.WithCommand ?? lower?.WithCommand,
                Prompt = higher?.Prompt ?? lower?.Prompt,
            };

    private static string[]? MergeArray(string[]? lower, string[]? higher)
    {
        if (higher is not null)
        {
            return [.. higher];
        }

        return lower is null ? null : [.. lower];
    }
}

public static class DocumentValidator
{
    public static IReadOnlyList<string> Validate(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ValidateOptions(document.Options, "options");
    }

    public static IReadOnlyList<string> Validate(ScenarioDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>(ValidateOptions(document.Options, "options"));
        if (document.Scenario is null)
        {
            errors.Add("scenario is required.");
            return errors;
        }

        var scenario = document.Scenario;
        if (scenario.WorkingDir?.Path is not null && scenario.WorkingDir.Temporary == true)
        {
            errors.Add("scenario.working-dir.path and temporary cannot both be specified.");
        }

        if (scenario.Launch is null || string.IsNullOrWhiteSpace(scenario.Launch.Executable))
        {
            errors.Add("scenario.launch.executable is required.");
        }

        if (scenario.Launch?.Options is { } launchOptions)
        {
            if (
                launchOptions.Environment is not null
                || launchOptions.Interactive is not null
                || launchOptions.LiveServer is not null
                || launchOptions.Capture is not null
                || launchOptions.Render is not null
                || launchOptions.Converter is not null
                || launchOptions.Appearance is not null
            )
            {
                errors.Add("scenario.launch.options may only contain terminal.");
            }

            if (launchOptions.Terminal is { } terminal)
            {
                ValidateDimensions(terminal, "scenario.launch.options", errors);
            }
        }

        ValidateCommandSteps(scenario.Prepare, "scenario.prepare", errors);
        ValidateCommandSteps(scenario.Verify, "scenario.verify", errors);
        ValidateCommandSteps(scenario.Teardown, "scenario.teardown", errors);

        if (scenario.Execute is not null)
        {
            for (var index = 0; index < scenario.Execute.Length; index++)
            {
                ValidateStep(scenario.Execute[index], $"scenario.execute[{index}]", errors);
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateOptions(ConsoleOptions? options, string path)
    {
        var errors = new List<string>();
        if (options is null)
        {
            return errors;
        }

        if (options.Terminal?.Width is <= 0)
        {
            errors.Add($"{path}.terminal.width must be greater than zero.");
        }
        if (options.Terminal?.Height is <= 0)
        {
            errors.Add($"{path}.terminal.height must be greater than zero.");
        }

        if (options.Environment?.Color is not null and not "overwrite" and not "preserve")
        {
            errors.Add($"{path}.environment.color must be 'overwrite' or 'preserve'.");
        }
        if (options.Environment?.Ci is not null and not "strip" and not "preserve")
        {
            errors.Add($"{path}.environment.ci must be 'strip' or 'preserve'.");
        }
        if (options.LiveServer?.Host is { } host && string.IsNullOrWhiteSpace(host))
        {
            errors.Add($"{path}.live-server.host must not be empty.");
        }

        if (options.Capture is { } capture)
        {
            if (capture.Mode is not null and not "image" and not "video")
            {
                errors.Add($"{path}.capture.mode must be 'image' or 'video'.");
            }
            if (capture.Image?.Frame is not null && capture.Image.Time is not null)
            {
                errors.Add($"{path}.capture.image.frame and time cannot both be specified.");
            }
            if (capture.Image?.Frame is < 0 || capture.Image?.Time is < 0)
            {
                errors.Add($"{path}.capture.image frame and time must be non-negative.");
            }
            if (capture.Video?.Fps is <= 0)
            {
                errors.Add($"{path}.capture.video.fps must be greater than zero.");
            }
            if (capture.Video?.Fadeout is < 0)
            {
                errors.Add($"{path}.capture.video.fadeout must be non-negative.");
            }
            if (capture.Video?.Timing is not null and not "deterministic" and not "realtime")
            {
                errors.Add($"{path}.capture.video.timing must be 'deterministic' or 'realtime'.");
            }
            if (capture.Video?.Coalesce is { } coalesce && !IsCoalesceValid(coalesce))
            {
                errors.Add(
                    $"{path}.capture.video.coalesce must be 'auto' or a non-negative number."
                );
            }
            if (
                capture.Video?.Time?.Start is < 0
                || capture.Video?.Time?.End is < 0
                || (
                    capture.Video?.Time?.Start is { } start
                    && capture.Video.Time.End is { } end
                    && end < start
                )
            )
            {
                errors.Add(
                    $"{path}.capture.video.time must be a non-negative range with end >= start."
                );
            }

            ValidateCrop(capture.Crop, $"{path}.capture.crop", errors);
        }

        if (options.Appearance is { } appearance)
        {
            if (appearance.Font?.Size is <= 0)
            {
                errors.Add($"{path}.appearance.font.size must be greater than zero.");
            }
            if (appearance.Size?.Width is <= 0 || appearance.Size?.Height is <= 0)
            {
                errors.Add($"{path}.appearance.size dimensions must be greater than zero.");
            }
            if (appearance.Opacity is < 0 or > 1)
            {
                errors.Add($"{path}.appearance.opacity must be between zero and one.");
            }
            if (appearance.Background is { Length: > 2 })
            {
                errors.Add($"{path}.appearance.background accepts at most two values.");
            }
            if (
                appearance.Padding is < 0
                || appearance.Margin is < 0
                || appearance.PcPadding is < 0
            )
            {
                errors.Add($"{path}.appearance padding and margin values must be non-negative.");
            }
        }

        if (options.Render?.Adjust is not null and not "spacing" and not "spacingAndGlyphs")
        {
            errors.Add($"{path}.render.adjust must be 'spacing' or 'spacingAndGlyphs'.");
        }
        if (
            options.Converter?.SvgConverter
            is not null
                and not "auto"
                and not "ffmpeg"
                and not "rsvg"
                and not "rsvg-convert"
                and not "resvg"
        )
        {
            errors.Add(
                $"{path}.converter.svg-converter must be auto, ffmpeg, rsvg, rsvg-convert, or resvg."
            );
        }

        return errors;
    }

    private static void ValidateDimensions(
        TerminalOptions options,
        string path,
        List<string> errors
    )
    {
        if (options.Width is <= 0)
        {
            errors.Add($"{path}.terminal.width must be greater than zero.");
        }
        if (options.Height is <= 0)
        {
            errors.Add($"{path}.terminal.height must be greater than zero.");
        }
    }

    private static void ValidateCrop(CropOptions? crop, string path, List<string> errors)
    {
        if (crop is null)
        {
            return;
        }

        foreach (
            var (name, value) in new[]
            {
                ("top", crop.Top),
                ("right", crop.Right),
                ("bottom", crop.Bottom),
                ("left", crop.Left),
            }
        )
        {
            if (value is null)
            {
                continue;
            }

            try
            {
                _ = ConsoleToSvg.Svg.CropValue.Parse(value);
            }
            catch (ArgumentException)
            {
                errors.Add($"{path}.{name} has an invalid crop value.");
            }
        }
    }

    private static bool IsCoalesceValid(string value) =>
        value == "auto"
        || double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var milliseconds
        )
            && double.IsFinite(milliseconds)
            && milliseconds >= 0;

    private static void ValidateCommandSteps(
        ScenarioCommandStep[]? steps,
        string path,
        List<string> errors
    )
    {
        if (steps is null)
        {
            return;
        }

        for (var index = 0; index < steps.Length; index++)
        {
            var step = steps[index];
            if (step.Type is not null and not "command")
            {
                errors.Add($"{path}[{index}].type must be 'command'.");
            }
            if (string.IsNullOrWhiteSpace(step.Command))
            {
                errors.Add($"{path}[{index}].command is required.");
            }
        }
    }

    private static void ValidateStep(ScenarioStep step, string path, List<string> errors)
    {
        if (
            step.Type
            is not "send"
                and not "wait"
                and not "resize"
                and not "capture"
                and not "command"
        )
        {
            errors.Add($"{path}.type is missing or unsupported.");
            return;
        }

        if (step.Type != "capture" && step.Options is not null)
        {
            errors.Add($"{path}.options is only valid for capture steps.");
        }
        if (
            step.Options?.Terminal is not null
            || step.Options?.Interactive is not null
            || step.Options?.LiveServer is not null
            || step.Options?.Environment is not null
        )
        {
            errors.Add(
                $"{path}.options may only contain capture, render, appearance, and converter."
            );
        }

        switch (step.Type)
        {
            case "send":
                if (step.Inputs is not { Length: > 0 })
                {
                    errors.Add($"{path}.inputs must contain at least one input.");
                }
                break;
            case "wait":
                if (
                    string.IsNullOrWhiteSpace(step.Args?.Text)
                    == string.IsNullOrWhiteSpace(step.Args?.Regex)
                )
                {
                    errors.Add($"{path}.args must specify exactly one of text or regex.");
                }
                if (step.Args?.Until is not null and not "present" and not "absent")
                {
                    errors.Add($"{path}.args.until must be 'present' or 'absent'.");
                }
                break;
            case "resize":
                if (step.Args?.Width is <= 0 || step.Args?.Height is <= 0)
                {
                    errors.Add($"{path}.args.width and height must be greater than zero.");
                }
                break;
            case "command":
                if (string.IsNullOrWhiteSpace(step.Command))
                {
                    errors.Add($"{path}.command is required.");
                }
                break;
        }
    }
}
