using System;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Svg;

public sealed class SvgRenderOptions
{
    public string Theme { get; set; } = "dark";

    public CropOptions Crop { get; set; } = CropOptions.Parse("0", "0", "0", "0");

    public int? Frame { get; set; }

    public string? Font { get; set; }

    public double FontSize { get; set; } = 14d;

    /// <summary>Window chrome definition. null = no chrome (transparent/plain).</summary>
    public ChromeDefinition? Chrome { get; set; }

    public double Padding { get; set; }

    public bool Loop { get; set; }

    public double VideoFps { get; set; } = 12d;

    public double VideoSleep { get; set; } = 1d;

    public double VideoFadeOut { get; set; } = 0d;

    public VideoTimingMode VideoTiming { get; set; } = VideoTimingMode.Deterministic;

    public double Opacity { get; set; } = 1d;

    public int? HeightRows { get; set; }

    public string? CommandHeader { get; set; }

    public string? ForeColor { get; set; }

    public string LengthAdjust { get; set; } = "spacing";

    /// <summary>Background specification: null = default, 1 element = solid color or image path, 2 elements = gradient.</summary>
    public string[]? Background { get; set; }

    /// <summary>Override the terminal's own background color. null = use theme default.</summary>
    public string? BackColor { get; set; }

    private static readonly char[] PathChars = ['/', '\\', '.'];

    public static SvgRenderOptions FromAppOptions(AppOptions appOptions)
    {
        // Resolve effective window name: --pcmode appends -pc to any builtin style that doesn't already have it.
        // Skip suffixing for "none", empty values, and file paths (contain path separators or dots).
        var windowName = appOptions.Window;
        if (
            appOptions.PcMode
            && !string.IsNullOrWhiteSpace(windowName)
            && !string.Equals(windowName, "none", StringComparison.OrdinalIgnoreCase)
            && !windowName.EndsWith("-pc", StringComparison.OrdinalIgnoreCase)
            && windowName.IndexOfAny(PathChars) < 0
        )
        {
            windowName = windowName + "-pc";
        }

        var chrome = ChromeLoader.Load(windowName);
        if (chrome != null && appOptions.PcPadding.HasValue)
        {
            chrome.DesktopPadding = appOptions.PcPadding.Value;
        }

        var effectivePadding = appOptions.Padding ?? 8d;
        var prompt = string.IsNullOrWhiteSpace(appOptions.Prompt) ? "$" : appOptions.Prompt;
        string? commandHeader = null;
        if (!string.IsNullOrWhiteSpace(appOptions.Header))
        {
            commandHeader = $"{prompt} {appOptions.Header}";
        }
        else if (appOptions.WithCommand && !string.IsNullOrWhiteSpace(appOptions.Command))
        {
            commandHeader = $"{prompt} {appOptions.Command}";
        }

        return new SvgRenderOptions
        {
            Theme = appOptions.Theme,
            Crop = CropOptions.Parse(
                appOptions.CropTop,
                appOptions.CropRight,
                appOptions.CropBottom,
                appOptions.CropLeft
            ),
            Frame = appOptions.Frame,
            Font = appOptions.Font,
            FontSize = appOptions.FontSize ?? 14d,
            Chrome = chrome,
            Padding = effectivePadding,
            Loop = appOptions.Loop,
            VideoFps = appOptions.VideoFps,
            VideoTiming = appOptions.VideoTiming,
            VideoSleep = appOptions.VideoSleep,
            VideoFadeOut = appOptions.VideoFadeOut,
            HeightRows = appOptions.Height,
            Opacity = appOptions.Opacity,
            CommandHeader = commandHeader,
            ForeColor = appOptions.ForeColor,
            LengthAdjust = appOptions.LengthAdjust,
            Background = appOptions.Background.Count > 0 ? appOptions.Background.ToArray() : null,
            BackColor = appOptions.BackColor,
        };
    }
}
