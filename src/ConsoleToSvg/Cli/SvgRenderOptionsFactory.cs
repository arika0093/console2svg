using System;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

public static class SvgRenderOptionsFactory
{
    private static readonly char[] PathChars = ['/', '\\', '.'];

    public static SvgRenderOptions Create(AppOptions appOptions)
    {
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
            Crop = CropOptions.Parse(appOptions.CropTop, appOptions.CropRight, appOptions.CropBottom, appOptions.CropLeft),
            Frame = appOptions.Frame,
            Time = appOptions.Time,
            TimeStart = appOptions.TimeStart,
            TimeEnd = appOptions.TimeEnd,
            Font = appOptions.Font,
            FontSize = appOptions.FontSize ?? 14d,
            Chrome = chrome,
            Padding = appOptions.Padding ?? 8d,
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
            SizeWidth = appOptions.SizeWidth,
            SizeHeight = appOptions.SizeHeight,
            MaskPatterns = appOptions.MaskPatterns.Count > 0 ? appOptions.MaskPatterns.ToArray() : null,
        };
    }
}
